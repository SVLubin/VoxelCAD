using System;
using System.IO;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// A camera is an element with a lens: where it stands is the transform every
/// element has, and which one takes the picture is the scene's own answer.
/// </summary>
public sealed class CameraTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"voxelcad-{Guid.NewGuid():N}.vcad");

    public CameraTests() => _scene = _project.Scenes[0];

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void AFreshCameraIsAPinholeAtTheViewportsOwnAngle()
    {
        var camera = (ICamera)_scene.Element(Create("Camera")).Component!;

        Assert.Equal(CameraLens.Default, camera.Lens);
        Assert.Equal(RenderProjection.Perspective, camera.Lens.Projection);
        Assert.Equal(0f, camera.Lens.Aperture);
    }

    [Fact]
    public void ASceneWithNoCameraSaysSo()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        Assert.Equal(Guid.Empty, _scene.ActiveCameraId);
    }

    /// <summary>
    /// Nobody should have to choose before there is a choice: one camera is the
    /// camera, and the first of several is the one at the top of the panel.
    /// </summary>
    [Fact]
    public void TheFirstCameraRendersWithoutAnybodyChoosing()
    {
        Guid first = Create("First");
        Guid second = Create("Second");

        Assert.Equal(first, _scene.ActiveCameraId);
        Assert.NotEqual(second, _scene.ActiveCameraId);
        Assert.Empty(_project.History.Records.Where(r => r.Kind == ActionKind.ChangeActiveCamera));
    }

    /// <summary>
    /// Tree order rather than whatever the registry hands back, so which camera
    /// stands in for a choice nobody made is the one a reader would guess.
    /// </summary>
    [Fact]
    public void TheFallbackIsTheFirstCameraInTreeOrder()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Rig");
        Guid nested = _scene.CreateElement(group, ComponentKind.Camera, "Nested");

        Create("Later");

        Assert.Equal(nested, _scene.ActiveCameraId);
    }

    [Fact]
    public void ChoosingACameraIsOneUndoableRecord()
    {
        Create("First");

        Guid second = Create("Second");

        _scene.SetActiveCamera(second);

        Assert.Equal(second, _scene.ActiveCameraId);
        Assert.Equal(ActionKind.ChangeActiveCamera, _project.History.Records[^1].Kind);

        _project.History.Undo();

        Assert.NotEqual(second, _scene.ActiveCameraId);
    }

    [Fact]
    public void ChoosingTheCameraAlreadyChosenRecordsNothing()
    {
        Guid first = Create("First");

        _scene.SetActiveCamera(first);

        int before = _project.History.Records.Count;

        _scene.SetActiveCamera(first);

        Assert.Equal(before, _project.History.Records.Count);
    }

    [Fact]
    public void AnElementThatIsNotACameraCannotBeChosen()
    {
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        Assert.Throws<InvalidOperationException>(() => _scene.SetActiveCamera(model));
    }

    /// <summary>
    /// The whole point of the fallback: deleting the camera a picture was taken
    /// through must hand the picture to another rather than leave the scene
    /// unrenderable — and undoing that delete must hand it straight back.
    /// </summary>
    [Fact]
    public void DeletingTheChosenCameraFallsBackAndUndoingBringsItHome()
    {
        Guid first = Create("First");
        Guid second = Create("Second");

        _scene.SetActiveCamera(second);
        _scene.DeleteElement(second);

        Assert.Equal(first, _scene.ActiveCameraId);

        _project.History.Undo();

        Assert.Equal(second, _scene.ActiveCameraId);
    }

    [Fact]
    public void DeletingTheLastCameraLeavesTheSceneWithNone()
    {
        Guid only = Create("Only");

        _scene.DeleteElement(only);

        Assert.Equal(Guid.Empty, _scene.ActiveCameraId);
    }

    [Fact]
    public void SettingTheLensIsOneUndoableRecordAndMovesNothing()
    {
        Guid id = Create("Camera");

        _scene.SetTransform(id, TransformChannel.Position, new Vector3(4f, 5f, 6f));

        var lens = new CameraLens(RenderProjection.Orthographic, 24f, 12f, 1.5f, 30f);

        _scene.SetCameraLens(id, lens);

        Assert.Equal(lens, Lens(id));
        Assert.Equal(ActionKind.ChangeCamera, _project.History.Records[^1].Kind);
        Assert.Equal(new Vector3(4f, 5f, 6f), _scene.Element(id).Transform.Position);

        _project.History.Undo();

        Assert.Equal(CameraLens.Default, Lens(id));
        Assert.Equal(new Vector3(4f, 5f, 6f), _scene.Element(id).Transform.Position);
    }

    [Fact]
    public void ALensEqualToTheCurrentOneRecordsNothing()
    {
        Guid id = Create("Camera");
        int before = _project.History.Records.Count;

        _scene.SetCameraLens(id, CameraLens.Default);

        Assert.Equal(before, _project.History.Records.Count);
    }

    /// <summary>
    /// Clamped rather than refused, the same bargain the transform makes: a form
    /// and a command must not be able to raise an exception between them.
    /// </summary>
    [Fact]
    public void AnImpossibleLensIsClampedRatherThanRefused()
    {
        Guid id = Create("Camera");

        _scene.SetCameraLens(
            id,
            new CameraLens(RenderProjection.Perspective, 900f, -5f, 1000f, -1f));

        CameraLens lens = Lens(id);

        Assert.Equal(CameraLimits.MaximumFov, lens.FovDegrees);
        Assert.Equal(0f, lens.OrthographicHeight);
        Assert.Equal(CameraLimits.MaximumAperture, lens.Aperture);
        Assert.Equal(0f, lens.FocusDistance);
    }

    [Fact]
    public void SettingALensOnSomethingThatIsNotACameraIsRefused()
    {
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        Assert.Throws<InvalidOperationException>(
            () => _scene.SetCameraLens(model, CameraLens.Default));
    }

    [Fact]
    public void ALensAndAChoiceBothSurviveTheRoundTrip()
    {
        Create("First");

        Guid second = Create("Second");
        var lens = new CameraLens(RenderProjection.Orthographic, 31f, 18f, 2f, 44f);

        _scene.SetCameraLens(second, lens);
        _scene.SetActiveCamera(second);

        IScene loaded = SaveAndLoad().Scenes[0];

        Assert.Equal(second, loaded.ActiveCameraId);
        Assert.Equal(lens, ((ICamera)loaded.Element(second).Component!).Lens);
    }

    /// <summary>
    /// A camera nobody has touched writes no lens at all, so the common element
    /// carries no entry — and a file that names no choice still renders.
    /// </summary>
    [Fact]
    public void ACameraAtItsDefaultsWritesNothingAboutItself()
    {
        Guid id = Create("Camera");

        _project.Save(_path);

        var yaml = File.ReadAllText(_path);

        Assert.Contains("component: Camera", yaml);
        Assert.DoesNotContain("camera:", yaml);
        Assert.DoesNotContain("activeCamera:", yaml);
        Assert.Equal(id, ModelFactory.LoadProject(_path).Scenes[0].ActiveCameraId);
    }

    /// <summary>
    /// The choice is written, never the answer: saving the fallback would turn a
    /// camera that merely happened to be first into a decision.
    /// </summary>
    [Fact]
    public void AFallbackIsNotWrittenDownAsAChoice()
    {
        Create("First");
        _project.Save(_path);

        Assert.DoesNotContain("activeCamera:", File.ReadAllText(_path));
    }

    [Fact]
    public void ACopiedCameraCarriesItsLensAndNoLinkToTheOriginal()
    {
        Guid id = Create("Camera");
        var lens = new CameraLens(RenderProjection.Orthographic, 12f, 40f, 0.5f, 8f);

        _scene.SetCameraLens(id, lens);

        Guid copy = _scene.PasteElement(_scene.Id, _scene.CaptureElement(id));

        Assert.Equal(lens, Lens(copy));

        _scene.SetCameraLens(id, CameraLens.Default);

        Assert.Equal(lens, Lens(copy));
    }

    /// <summary>
    /// Nothing that walks the tree for cells may find a camera: it holds no
    /// geometry, so a mesher or an exporter asking it for voxels would be asking
    /// the wrong question.
    /// </summary>
    [Fact]
    public void ACameraHoldsNoVoxelModel()
    {
        Guid id = Create("Camera");

        Assert.Null(_scene.Element(id).Component as IVoxelModel);
        Assert.Equal(ComponentKind.Camera, _scene.Element(id).ComponentKind);
    }

    private Guid Create(string name) =>
        _scene.CreateElement(_scene.Id, ComponentKind.Camera, name);

    private CameraLens Lens(Guid id) => ((ICamera)_scene.Element(id).Component!).Lens;

    private IProject SaveAndLoad()
    {
        _project.Save(_path);

        return ModelFactory.LoadProject(_path);
    }
}
