using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The commands that look at the interface rather than at the document. Looking is
/// free — neither is gated on control — and what comes back has to say which
/// coordinate space it is measured in, or aiming at it later is guesswork.
/// </summary>
public sealed class InterfaceCommandsTests
{
    private const string Client = "interface-client";

    private readonly ControlArbiter _arbiter = new();
    private readonly FakeSceneView _view = new();
    private readonly FakeUiDriver _ui = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly ICommandDispatcher _dispatcher;

    public InterfaceCommandsTests()
    {
        var hostCommands = new List<CommandDefinition>();
        hostCommands.AddRange(CameraCommands.Create(_view));
        hostCommands.AddRange(InterfaceCommands.Create(_view, _ui));

        ICommandDispatcher inner = CommandFactory.CreateDispatcher(
            new CommandContext(_project),
            hostCommands);

        _dispatcher = new ControlGatedDispatcher(inner, _arbiter, new EditorSession(), Client);
    }

    [Fact]
    public void CapturingTheWindowIsAllowedWithoutControl()
    {
        Assert.True(Run("window").Ok);
        Assert.Equal(1, _view.CaptureCount);
    }

    /// <summary>
    /// The whole difference between the two commands: one asks for the chrome and
    /// the other does not, and both go through the same crop-scale-encode path.
    /// </summary>
    [Fact]
    public void WindowAsksForTheChromeAndFrameDoesNot()
    {
        Run("window");
        Assert.True(_view.LastCaptureOptions.IncludeChrome);

        Run("frame");
        Assert.False(_view.LastCaptureOptions.IncludeChrome);
    }

    [Fact]
    public void WindowHonoursTheSameDialsAsFrame()
    {
        Run("window", ("format", "jpeg"), ("scale", "0.5"), ("quality", "0.6"));

        Assert.Equal(0.5f, _view.LastCaptureOptions.Scale);
        Assert.Equal(FrameFormat.Jpeg, _view.LastCaptureOptions.Format);
        Assert.Equal(0.6f, _view.LastCaptureOptions.Quality);
        Assert.Equal("image/jpeg", Field(Run("window", ("format", "jpeg")), "mimeType"));
    }

    /// <summary>
    /// The picture is only half the answer. Without the window's size in input
    /// coordinates and the scale that got applied, a pixel read off it cannot be
    /// turned into a point to aim at.
    /// </summary>
    [Fact]
    public void WindowReportsTheMappingBackToInputCoordinates()
    {
        _view.WindowSize = new Vector2I(1489, 649);

        CommandResult result = Run("window");

        Assert.Equal(new[] { 1489, 649 }, Field(result, "windowSize"));
        Assert.Equal(new[] { 0.0, 0.0 }, Field(result, "origin"));
        Assert.Equal(new[] { 1.0, 1.0 }, Field(result, "scale"));
    }

    /// <summary>A scale other than one has to come back, or every click is off by it.</summary>
    [Fact]
    public void WindowReportsANonOneScaleFactor()
    {
        CommandResult result = Run("window", ("scale", "0.5"));

        Assert.Equal(new[] { 0.5, 0.5 }, Field(result, "scale"));
    }

    [Fact]
    public void WindowReportsTheCropOriginSoAPixelStillMapsBack()
    {
        CommandResult result = Run("window", ("region", "100,40,200,120"));

        Assert.Equal(new[] { 100.0, 40.0 }, Field(result, "origin"));
        Assert.Equal(200, Field(result, "width"));
    }

    /// <summary>The image rides as base64 beside its type, which is what the transport turns into a picture.</summary>
    [Fact]
    public void WindowCarriesTheImageAndItsType()
    {
        CommandResult result = Run("window");

        Assert.NotNull(Field(result, "image"));
        Assert.Equal("image/png", Field(result, "mimeType"));
    }

    // --- ui ---

    [Fact]
    public void ReadingTheControlTreeIsAllowedWithoutControl()
    {
        _ui.Add("UiRoot/Undo", new Rect2(10f, 20f, 80f, 24f), text: "Undo");

        Assert.True(Run("ui").Ok);
    }

    /// <summary>
    /// What a row has to carry to be worth anything: an address the input commands
    /// take, and a rect in the space they aim in.
    /// </summary>
    [Fact]
    public void EachControlCarriesItsReferenceTypeAndRect()
    {
        _ui.Add("UiRoot/HierarchyDock/HistoryRow/Undo", new Rect2(10f, 20f, 80f, 24f), text: "Undo");

        var row = (IReadOnlyDictionary<string, object?>)Rows(Run("ui"))[0]!;

        Assert.Equal("UiRoot/HierarchyDock/HistoryRow/Undo", row["ref"]);
        Assert.Equal("Button", row["type"]);
        Assert.Equal("Undo", row["text"]);
        Assert.Equal(new[] { 10.0, 20.0, 80.0, 24.0 }, row["rect"]);
        Assert.Equal(true, row["visible"]);
        Assert.Equal(false, row["disabled"]);
    }

    /// <summary>Most of the tree is containers; a blank string on every one of them is a third of the answer.</summary>
    [Fact]
    public void AControlWithNothingToSayCarriesNoText()
    {
        _ui.Add("UiRoot/Row", new Rect2(0f, 0f, 10f, 10f), type: "HBoxContainer");

        var row = (IReadOnlyDictionary<string, object?>)Rows(Run("ui"))[0]!;

        Assert.False(row.ContainsKey("text"));
    }

    [Fact]
    public void UiReportsTheWindowSizeTheRectsAreMeasuredIn()
    {
        _ui.WindowSize = new Vector2I(1489, 649);

        Assert.Equal(new[] { 1489, 649 }, Field(Run("ui"), "windowSize"));
    }

    [Fact]
    public void UiPassesTheWindowingArgumentsThrough()
    {
        Run("ui", ("root", "UiRoot/HierarchyDock"), ("depth", "2"), ("invisible", "true"));

        Assert.Equal("UiRoot/HierarchyDock", _ui.LastOptions.Root);
        Assert.Equal(2, _ui.LastOptions.MaxDepth);
        Assert.True(_ui.LastOptions.IncludeInvisible);
    }

    [Fact]
    public void UiDefaultsToTheWholeShellAtEveryDepthAndVisibleOnly()
    {
        Run("ui");

        Assert.Equal(string.Empty, _ui.LastOptions.Root);
        Assert.Equal(int.MaxValue, _ui.LastOptions.MaxDepth);
        Assert.False(_ui.LastOptions.IncludeInvisible);
    }

    /// <summary>
    /// Past the cap the answer is refused rather than truncated, and the refusal
    /// names the arguments that would window it — the same bargain a slice makes.
    /// </summary>
    [Fact]
    public void AnOverLargeReadingIsRefusedNamingWhatWouldWindowIt()
    {
        _ui.Add("UiRoot/Undo", new Rect2(0f, 0f, 10f, 10f));
        _ui.TotalInScope = 900;
        _ui.Limit = 500;

        CommandResult result = Run("ui");

        Assert.False(result.Ok);
        Assert.Contains("900", result.Error);
        Assert.Contains("500", result.Error);
        Assert.Contains("root", result.Error);
        Assert.Contains("depth", result.Error);
    }

    private static IReadOnlyList<object?> Rows(CommandResult result) =>
        (IReadOnlyList<object?>)Field(result, "controls")!;

    private static object? Field(CommandResult result, string key) =>
        ((IReadOnlyDictionary<string, object?>)result.Value!)[key];

    private CommandResult Run(string name, params (string Key, string Value)[] arguments) =>
        _dispatcher.Execute(name, arguments.ToDictionary(pair => pair.Key, pair => pair.Value));
}
