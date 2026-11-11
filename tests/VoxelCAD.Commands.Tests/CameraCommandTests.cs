using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// The camera surface: the lens, the aim, and which camera the picture is taken
/// through. Creating one and moving one are deliberately not here — a camera is
/// an element, so 'create-element' and 'set-transform' already do both.
/// </summary>
public sealed class CameraCommandTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly ICommandDispatcher _dispatcher;
    private readonly IScene _scene;

    public CameraCommandTests()
    {
        _scene = _project.Scenes[0];
        _dispatcher = CommandFactory.CreateDispatcher(new CommandContext(_project));
    }

    [Fact]
    public void ACameraIsCreatedThroughTheOrdinaryElementCommand()
    {
        CommandResult result = Run("create-element", ("name", "Cam"), ("kind", "Camera"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(ComponentKind.Camera, _scene.Children[0].ComponentKind);
    }

    /// <summary>
    /// Named rather than whole, so one number can be turned without restating the
    /// four beside it — and so a call cannot silently undo what somebody else set.
    /// </summary>
    [Fact]
    public void SettingOneValueLeavesTheOthersWhereTheyWere()
    {
        Guid camera = Camera("Cam");

        Run("set-camera", ("camera", "Cam"), ("fov", "24"), ("blur", "1.5"));
        Run("set-camera", ("camera", "Cam"), ("projection", "orthographic"));

        CameraLens lens = Lens(camera);

        Assert.Equal(RenderProjection.Orthographic, lens.Projection);
        Assert.Equal(24f, lens.FovDegrees, 3);
        Assert.Equal(1.5f, lens.Aperture, 3);
    }

    /// <summary>
    /// Clamped rather than refused, the bargain every dial in this editor makes:
    /// a form and a command must not be able to raise an exception between them.
    /// </summary>
    [Fact]
    public void AnImpossibleLensIsBroughtIntoRange()
    {
        Guid camera = Camera("Cam");

        Assert.True(Run("set-camera", ("camera", "Cam"), ("fov", "900"), ("blur", "-2")).Ok);

        Assert.Equal(CameraLimits.MaximumFov, Lens(camera).FovDegrees);
        Assert.Equal(0f, Lens(camera).Aperture);
    }

    [Fact]
    public void SettingALensOnSomethingThatIsNotACameraIsRefusedByName()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        CommandResult result = Run("set-camera", ("camera", "Model"), ("fov", "30"));

        Assert.False(result.Ok);
        Assert.Contains("not a camera", result.Error);
    }

    /// <summary>
    /// The arithmetic a caller would otherwise have to do to write Euler degrees
    /// that point somewhere. Checked by the direction that comes back rather than
    /// by the angles, which is the only thing anyone actually wants from it.
    /// </summary>
    [Theory]
    [InlineData(10f, 0f, 0f)]
    [InlineData(0f, 0f, 10f)]
    [InlineData(-6f, 3f, -4f)]
    public void AimingPointsTheCameraAtThePoint(float x, float y, float z)
    {
        Guid camera = Camera("Cam");

        _scene.SetTransform(camera, TransformChannel.Position, new Vector3(2f, 5f, 8f));

        Assert.True(Run("aim-camera", ("camera", "Cam"), ("at", $"{x},{y},{z}")).Ok);

        var target = new Vector3(x, y, z);
        Transform3D world = Element(camera).WorldTransform;
        Vector3 wanted = (target - world.Origin).Normalized();

        Assert.True(
            (-world.Basis.Z.Normalized()).IsEqualApprox(wanted),
            $"looked along {-world.Basis.Z.Normalized()}, wanted {wanted}");
    }

    /// <summary>
    /// Straight up leaves "which way is up" undefined, and Godot answers that by
    /// refusing — so the aim names an up that cannot be parallel to it.
    /// </summary>
    [Fact]
    public void AimingStraightDownIsAnsweredRatherThanRefused()
    {
        Guid camera = Camera("Cam");

        _scene.SetTransform(camera, TransformChannel.Position, new Vector3(4f, 20f, 4f));

        Assert.True(Run("aim-camera", ("camera", "Cam"), ("at", "4,0,4")).Ok);
        Assert.True((-Element(camera).WorldTransform.Basis.Z.Normalized())
            .IsEqualApprox(Vector3.Down));
    }

    /// <summary>
    /// Aiming a camera under a group writes the number its own channel holds, and
    /// that number is local — without composing through the parent it would look
    /// right at the top level and be wrong everywhere else.
    /// </summary>
    [Fact]
    public void AimingWorksThroughARotatedParent()
    {
        Guid rig = _scene.CreateElement(_scene.Id, ComponentKind.None, "Rig");
        Guid camera = _scene.CreateElement(rig, ComponentKind.Camera, "Cam");

        _scene.SetTransform(rig, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));
        _scene.SetTransform(rig, TransformChannel.Position, new Vector3(6f, 0f, 0f));
        _scene.SetTransform(camera, TransformChannel.Position, new Vector3(0f, 4f, 0f));

        Assert.True(Run("aim-camera", ("camera", "Cam"), ("at", "16,4,16")).Ok);

        Transform3D world = Element(camera).WorldTransform;
        Vector3 wanted = (new Vector3(16f, 4f, 16f) - world.Origin).Normalized();

        Assert.True((-world.Basis.Z.Normalized()).IsEqualApprox(wanted));
    }

    [Fact]
    public void AimingAtWhereItAlreadyStandsIsRefusedRatherThanLeavingItSpinning()
    {
        Guid camera = Camera("Cam");

        _scene.SetTransform(camera, TransformChannel.Position, new Vector3(3f, 3f, 3f));

        CommandResult result = Run("aim-camera", ("camera", "Cam"), ("at", "3,3,3"));

        Assert.False(result.Ok);
        Assert.Contains("no direction", result.Error);
    }

    [Fact]
    public void ChoosingWhichCameraRendersIsOnTheSurface()
    {
        Camera("First");

        Guid second = Camera("Second");

        Assert.True(Run("set-active-camera", ("camera", "Second")).Ok);
        Assert.Equal(second, _scene.ActiveCameraId);
    }

    [Fact]
    public void ChoosingSomethingThatIsNotACameraIsRefusedByName()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        CommandResult result = Run("set-active-camera", ("camera", "Model"));

        Assert.False(result.Ok);
        Assert.Contains("not a camera", result.Error);
    }

    /// <summary>
    /// What to read before asking for a picture. A camera's own position is local
    /// like every element's, so where it actually stands and which way it faces
    /// are reported beside it rather than left to be composed.
    /// </summary>
    [Fact]
    public void TheReportSaysWhereEachCameraStandsAndWhichOneRenders()
    {
        Guid rig = _scene.CreateElement(_scene.Id, ComponentKind.None, "Rig");
        Guid nested = _scene.CreateElement(rig, ComponentKind.Camera, "Nested");

        _scene.SetTransform(rig, TransformChannel.Position, new Vector3(10f, 0f, 0f));
        _scene.SetTransform(nested, TransformChannel.Position, new Vector3(0f, 4f, 0f));

        Camera("Loose");

        var answer = (Dictionary<string, object?>)Run("cameras").Value!;
        var camerasList = (List<object?>)answer["cameras"]!;

        Assert.Equal(2, camerasList.Count);

        var first = (Dictionary<string, object?>)camerasList[0]!;

        Assert.Equal("Nested", first["name"]);
        Assert.Equal(new[] { 10f, 4f, 0f }, (float[])first["standsAt"]!);
        Assert.Equal(true, first["renders"]);
        Assert.Equal(nested.ToString(), answer["active"]);
    }

    /// <summary>
    /// A lens is the whole of what a camera is, so an element read that named the
    /// kind and nothing else would send a reader to a second command to find out
    /// what the shot actually is.
    /// </summary>
    [Fact]
    public void AnOrdinaryElementReadAlsoCarriesTheLens()
    {
        Guid camera = Camera("Cam");

        Run("set-camera", ("camera", "Cam"), ("fov", "31"), ("blur", "0.75"));

        var answer = (Dictionary<string, object?>)Run("element", ("element", "Cam")).Value!;
        var lens = (Dictionary<string, object?>)answer["lens"]!;

        Assert.Equal(31d, Convert.ToDouble(lens["fov"]));
        Assert.Equal(0.75d, Convert.ToDouble(lens["blur"]));

        // And the two reads agree, which is why there is one function.
        var reported = (Dictionary<string, object?>)
            ((List<object?>)((Dictionary<string, object?>)Run("cameras").Value!)["cameras"]!)[0]!;

        Assert.Equal(lens, reported["lens"]);
        Assert.NotEqual(Guid.Empty, camera);
    }

    [Fact]
    public void TheReportSaysSoWhenThereIsNoCamera()
    {
        var answer = (Dictionary<string, object?>)Run("cameras").Value!;

        Assert.Empty((List<object?>)answer["cameras"]!);
        Assert.Null(answer["active"]);
        Assert.Contains("create-element", (string)answer["note"]!);
    }

    /// <summary>Looking is free: reading the cameras must not need control.</summary>
    [Fact]
    public void ReadingTheCamerasDoesNotMutate()
    {
        Assert.False(_dispatcher.Commands.Single(d => d.Name == "cameras").Mutates);
    }

    private Guid Camera(string name) =>
        _scene.CreateElement(_scene.Id, ComponentKind.Camera, name);

    private IHierarchyElement Element(Guid id)
    {
        _scene.TryGetElement(id, out IHierarchyElement element);

        return element;
    }

    private CameraLens Lens(Guid id) => ((ICamera)Element(id).Component!).Lens;

    private CommandResult Run(string name, params (string Key, string Value)[] arguments)
    {
        var map = new Dictionary<string, string>();

        foreach ((string key, string value) in arguments)
            map[key] = value;

        return _dispatcher.Execute(name, map);
    }
}
