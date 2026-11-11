using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The camera an agent aims and reads over the command surface. Aiming moves the
/// user's own view, so it is gated like an edit; looking is free, like a slice.
/// </summary>
public sealed class CameraCommandsTests
{
    private const string Client = "camera-client";

    private readonly ControlArbiter _arbiter = new();
    private readonly FakeSceneView _view = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly ICommandDispatcher _dispatcher;

    public CameraCommandsTests()
    {
        var hostCommands = new List<CommandDefinition>();
        hostCommands.AddRange(CameraCommands.Create(_view));

        ICommandDispatcher inner = CommandFactory.CreateDispatcher(
            new CommandContext(_project),
            hostCommands);

        _dispatcher = new ControlGatedDispatcher(inner, _arbiter, new EditorSession(), Client);
    }

    [Fact]
    public void AimingIsRefusedWithoutControl()
    {
        CommandResult result = Run("orbit", ("yaw", "45"));

        Assert.False(result.Ok);
        Assert.Contains("take-control", result.Error);
    }

    [Fact]
    public void ReadingTheCameraIsAllowedWithoutControl()
    {
        Assert.True(Run("camera").Ok);
    }

    [Fact]
    public void CapturingAFrameIsAllowedWithoutControl()
    {
        Assert.True(Run("frame").Ok);
        Assert.Equal(1, _view.CaptureCount);
    }

    [Fact]
    public void FrameDefaultsToAFullSizePng()
    {
        CommandResult result = Run("frame");

        Assert.Equal("image/png", Field(result, "mimeType"));
        Assert.Equal(1f, _view.LastCaptureOptions.Scale);
        Assert.Equal(FrameFormat.Png, _view.LastCaptureOptions.Format);
    }

    [Fact]
    public void FrameHonoursTheScaleFormatAndQualityDials()
    {
        CommandResult result = Run("frame", ("format", "jpeg"), ("scale", "0.5"), ("quality", "0.6"));

        Assert.Equal("image/jpeg", Field(result, "mimeType"));
        Assert.Equal(0.5f, _view.LastCaptureOptions.Scale);
        Assert.Equal(FrameFormat.Jpeg, _view.LastCaptureOptions.Format);
        Assert.Equal(0.6f, _view.LastCaptureOptions.Quality);
    }

    /// <summary>Out-of-range dials are clamped, not refused — a look must never throw.</summary>
    [Fact]
    public void FrameClampsScaleAndQualityRatherThanRefusing()
    {
        Run("frame", ("format", "webp"), ("scale", "9"), ("quality", "9"));

        Assert.Equal(1f, _view.LastCaptureOptions.Scale);
        Assert.Equal(1f, _view.LastCaptureOptions.Quality);
        Assert.Equal(FrameFormat.Webp, _view.LastCaptureOptions.Format);
    }

    [Fact]
    public void FramePassesTheRegionThroughAsAPixelRect()
    {
        Run("frame", ("region", "10,20,100,80"));

        Assert.Equal(new Godot.Rect2I(10, 20, 100, 80), _view.LastCaptureOptions.Region);
    }

    [Fact]
    public void FrameLeavesTheRegionNullWhenUnasked()
    {
        Run("frame");

        Assert.Null(_view.LastCaptureOptions.Region);
    }

    /// <summary>A malformed region is a typo to correct, so it is refused rather than clamped.</summary>
    [Fact]
    public void FrameRefusesAMalformedRegion()
    {
        CommandResult result = Run("frame", ("region", "10,20,100"));

        Assert.False(result.Ok);
        Assert.Contains("region", result.Error);
    }

    private static object? Field(CommandResult result, string key) =>
        ((IReadOnlyDictionary<string, object?>)result.Value!)[key];

    [Fact]
    public void OrbitSetsTheAnglesAndReportsThem()
    {
        Grant();

        CommandResult result = Run("orbit", ("yaw", "45"), ("pitch", "-20"));

        Assert.Equal(45f, _view.CameraPose.YawDegrees);
        Assert.Equal(-20f, _view.CameraPose.PitchDegrees);

        var value = (IReadOnlyDictionary<string, object?>)result.Value!;
        Assert.Equal(45.0, value["yaw"]);
        Assert.Equal(-20.0, value["pitch"]);
    }

    /// <summary>Omitting an angle keeps it, so the two axes can be aimed apart.</summary>
    [Fact]
    public void OrbitLeavesAnUnnamedAngleAlone()
    {
        Grant();
        _view.CameraPose = new CameraPose(Godot.Vector3.Zero, 10f, 30f, 24f);

        Run("orbit", ("yaw", "90"));

        Assert.Equal(90f, _view.CameraPose.YawDegrees);
        Assert.Equal(30f, _view.CameraPose.PitchDegrees);
    }

    /// <summary>
    /// Gated exactly as <c>orbit</c> is, and for the same reason: it turns the
    /// user's own camera.
    /// </summary>
    [Fact]
    public void TheNamedViewIsRefusedWithoutControl()
    {
        CommandResult result = Run("view", ("direction", "front"));

        Assert.False(result.Ok);
        Assert.Contains("take-control", result.Error);
    }

    [Theory]
    [InlineData("front", 0.0, 0.0)]
    [InlineData("back", 180.0, 0.0)]
    [InlineData("right", 90.0, 0.0)]
    [InlineData("left", -90.0, 0.0)]
    [InlineData("front-right", 45.0, 0.0)]
    [InlineData("top-front-right", 45.0, -35.264)]
    public void ANamedViewAimsThereAndReportsThePose(string direction, double yaw, double pitch)
    {
        Grant();

        CommandResult result = Run("view", ("direction", direction));

        var value = (IReadOnlyDictionary<string, object?>)result.Value!;

        Assert.True(result.Ok);
        Assert.Equal(yaw, (double)value["yaw"]!, 3);
        Assert.Equal(pitch, (double)value["pitch"]!, 3);
        Assert.Equal((float)yaw, _view.CameraPose.YawDegrees, 3);
    }

    /// <summary>
    /// Standing on the pitch the camera will hold rather than the ±90° it would
    /// silently correct, and keeping the yaw that has no meaning up there.
    /// </summary>
    [Fact]
    public void TopKeepsTheYawAndStopsShortOfStraightDown()
    {
        Grant();
        _view.CameraPose = new CameraPose(Godot.Vector3.Zero, 123f, -10f, 24f);

        Run("view", ("direction", "top"));

        Assert.Equal(123f, _view.CameraPose.YawDegrees, 3);
        Assert.True(_view.CameraPose.PitchDegrees > -90f);
        Assert.True(_view.CameraPose.PitchDegrees < -80f);
    }

    /// <summary>The one thing a turn to a standard view must never do.</summary>
    [Fact]
    public void ANamedViewLeavesTheFocusPointAndTheDistanceAlone()
    {
        Grant();
        _view.CameraPose = new CameraPose(new Godot.Vector3(4f, 5f, 6f), 0f, 0f, 37f);

        Run("view", ("direction", "bottom-back-left"));

        Assert.Equal(new Godot.Vector3(4f, 5f, 6f), _view.CameraPose.Pivot);
        Assert.Equal(37f, _view.CameraPose.Distance);
    }

    /// <summary>Any order of the parts, one spelling back out.</summary>
    [Fact]
    public void ThePartsOfANameMayArriveInAnyOrder()
    {
        Grant();

        Run("view", ("direction", "right-front-top"));

        Assert.Equal(45f, _view.CameraPose.YawDegrees, 3);
        Assert.Equal(-35.264f, _view.CameraPose.PitchDegrees, 3);
    }

    [Fact]
    public void AnUnknownDirectionIsRefusedWithTheListOfRealOnes()
    {
        Grant();
        CameraPose before = _view.CameraPose;

        CommandResult result = Run("view", ("direction", "sideways"));

        Assert.False(result.Ok);
        Assert.Contains("sideways", result.Error);
        Assert.Contains("top-front-right", result.Error);
        Assert.Equal(before, _view.CameraPose);
    }

    [Fact]
    public void ZoomSetsTheDistance()
    {
        Grant();

        Run("zoom", ("distance", "30"));

        Assert.Equal(30f, _view.CameraPose.Distance);
    }

    [Fact]
    public void FocusMovesTheFocusPointToAPoint()
    {
        Grant();

        Run("focus", ("at", "5,6,7"));

        Assert.Equal(new Godot.Vector3(5f, 6f, 7f), _view.CameraPose.Pivot);
    }

    [Fact]
    public void FocusOnAnElementFramesThatElement()
    {
        IScene scene = _project.Scenes[0];
        System.Guid elementId = scene.CreateElement(scene.Id, ComponentKind.VoxelModel, "Model");
        Grant();

        Run("focus", ("element", "Model"));

        Assert.Equal(elementId, _view.FocusTarget);
    }

    [Fact]
    public void FocusWithNothingNamedFramesTheWholeScene()
    {
        Grant();

        Run("focus");

        Assert.Equal(_project.Scenes[0].Id, _view.FocusTarget);
    }

    /// <summary>
    /// A preview is a read: a material's whole point is how it looks, and making an
    /// agent ask for the editor before it may look at one would be as odd as gating
    /// a slice.
    /// </summary>
    [Fact]
    public void APreviewIsAllowedWithoutControl()
    {
        Assert.True(Run("material-preview", ("material", _project.Materials[0].Name)).Ok);

        Assert.Single(_view.PreviewRequests);
        Assert.Equal(_project.Materials[0].Color, _view.PreviewRequests[0].Color);
        Assert.Equal(192, _view.PreviewRequests[0].Size);
    }

    [Fact]
    public void APreviewWithNoAngleIsTheThreeQuarterView()
    {
        Run("material-preview", ("material", _project.Materials[0].Name));

        Assert.Equal(MaterialPreviewTurn.Default, _view.PreviewTurns[0]);
    }

    /// <summary>
    /// The panel's block turns, so a picture has to be askable at another angle:
    /// half of what a facture does is only visible from the side, and an agent that
    /// can only see one face cannot answer a question a hand can.
    /// </summary>
    [Fact]
    public void APreviewCanBeTurned()
    {
        CommandResult result = Run(
            "material-preview",
            ("material", _project.Materials[0].Name),
            ("yaw", "120"),
            ("pitch", "40"));

        Assert.True(result.Ok);
        Assert.Equal(new MaterialPreviewTurn(120f, 40f), _view.PreviewTurns[0]);

        var value = (IReadOnlyDictionary<string, object?>)result.Value!;
        Assert.Equal(120d, value["yaw"]);
        Assert.Equal(40d, value["pitch"]);
    }

    /// <summary>
    /// And what it answers with is the turn it actually used: the pitch stops short
    /// of the pole and the yaw wraps, so an agent stepping round the block has to be
    /// told where it ended up rather than where it asked to be.
    /// </summary>
    [Fact]
    public void APreviewReportsTheTurnItWasAbleToTake()
    {
        CommandResult result = Run(
            "material-preview",
            ("material", _project.Materials[0].Name),
            ("yaw", "400"),
            ("pitch", "-120"));

        var value = (IReadOnlyDictionary<string, object?>)result.Value!;

        Assert.Equal(40d, value["yaw"]);
        Assert.Equal(-85d, value["pitch"]);
        Assert.Equal(new MaterialPreviewTurn(40f, -85f), _view.PreviewTurns[0]);
    }

    private void Grant()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Grant();
    }

    private CommandResult Run(string name, params (string Key, string Value)[] arguments) =>
        _dispatcher.Execute(name, arguments.ToDictionary(pair => pair.Key, pair => pair.Value));
}
