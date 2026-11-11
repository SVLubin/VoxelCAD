using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The instrument that drives the editor's own interface: off unless it has been
/// switched on, gated on control like any edit, and refusing rather than guessing
/// where a gesture should land.
/// </summary>
public sealed class InputCommandsTests
{
    private const string Client = "input-client";

    private readonly ControlArbiter _arbiter = new();
    private readonly FakeUiDriver _ui = new();
    private readonly FakePreferences _preferences = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly ICommandDispatcher _dispatcher;

    public InputCommandsTests()
    {
        var hostCommands = new List<CommandDefinition>();
        hostCommands.AddRange(InputCommands.Create(_ui, _preferences));

        ICommandDispatcher inner = CommandFactory.CreateDispatcher(
            new CommandContext(_project),
            hostCommands);

        _dispatcher = new ControlGatedDispatcher(inner, _arbiter, _session, Client);

        _ui.Add("UiRoot/Undo", new Rect2(100f, 40f, 80f, 20f), text: "Undo");
        _ui.Add("UiRoot/Tree", new Rect2(0f, 200f, 300f, 400f), type: "Tree");
        _ui.Add("UiRoot/Hidden", new Rect2(0f, 0f, 10f, 10f), visible: false);
        _ui.Add("UiRoot/Disabled", new Rect2(0f, 0f, 10f, 10f), disabled: true);
    }

    // --- the preference gate ---

    /// <summary>
    /// Registered whether or not it is switched on, so an agent that calls one
    /// learns why instead of finding nothing there. A tool that vanishes is worse
    /// than a tool that explains itself — clients cache the list.
    /// </summary>
    [Fact]
    public void TheCommandsExistEvenWhileTheInstrumentIsOff()
    {
        Assert.Contains(_dispatcher.Commands, command => command.Name == "input-click");
        Assert.Contains(_dispatcher.Commands, command => command.Name == "input-drag");
        Assert.Contains(_dispatcher.Commands, command => command.Name == "input-key");
    }

    [Fact]
    public void TheyRefuseWhileTheInstrumentIsOffAndNameTheSetting()
    {
        Grant();

        CommandResult result = Run("input-click", ("target", "UiRoot/Undo"));

        Assert.False(result.Ok);
        Assert.Contains("Preferences", result.Error);
        Assert.Contains("drive the interface", result.Error);
        Assert.Empty(_ui.Injections);
    }

    /// <summary>The refusal points at the semantic surface, not just at the switch.</summary>
    [Fact]
    public void TheRefusalNamesTheCommandsToUseInstead()
    {
        Grant();

        CommandResult result = Run("input-click", ("target", "UiRoot/Undo"));

        Assert.Contains("set-voxels", result.Error);
        Assert.Contains("set-transform", result.Error);
    }

    // --- the control gate ---

    /// <summary>
    /// Injected input changes the document, so it is gated exactly like an edit.
    /// One gate, the one that already exists.
    /// </summary>
    [Fact]
    public void InjectingIsRefusedWithoutControl()
    {
        Enable();

        CommandResult result = Run("input-click", ("target", "UiRoot/Undo"));

        Assert.False(result.Ok);
        Assert.Contains("take-control", result.Error);
        Assert.Empty(_ui.Injections);
    }

    // --- aiming ---

    [Fact]
    public void ATargetResolvesToItsCentreAndSaysWhereItLanded()
    {
        Ready();

        CommandResult result = Run("input-click", ("target", "UiRoot/Undo"));

        Assert.True(result.Ok);
        Assert.Equal(new Vector2(140f, 50f), _ui.LastInjection.Point);
        Assert.Equal(new[] { 140.0, 50.0 }, Field(result, "at"));
        Assert.Equal("UiRoot/Undo", Field(result, "target"));
        Assert.Equal("Undo", Field(result, "text"));
    }

    /// <summary>
    /// The offset form, which is how a row of a list is reached — list rows are
    /// not controls of their own.
    /// </summary>
    [Fact]
    public void AnOffsetAimsElsewhereInsideTheTarget()
    {
        Ready();

        Run("input-click", ("target", "UiRoot/Tree"), ("offset", "0.5,0.1"));

        Assert.Equal(new Vector2(150f, 240f), _ui.LastInjection.Point);
    }

    /// <summary>Coordinates exist for what has no control behind it — a gizmo handle.</summary>
    [Fact]
    public void AnExplicitPointIsAimedAtDirectly()
    {
        Ready();

        CommandResult result = Run("input-move", ("at", "640,480"));

        Assert.Equal(new Vector2(640f, 480f), _ui.LastInjection.Point);
        Assert.Equal(new[] { 640.0, 480.0 }, Field(result, "at"));
        Assert.False(((IReadOnlyDictionary<string, object?>)result.Value!).ContainsKey("target"));
    }

    /// <summary>Aiming at nothing is a refusal that says which of the two to pass.</summary>
    [Fact]
    public void AimingAtNothingIsRefused()
    {
        Ready();

        CommandResult result = Run("input-click");

        Assert.False(result.Ok);
        Assert.Contains("target", result.Error);
        Assert.Contains("at", result.Error);
    }

    [Theory]
    [InlineData("UiRoot/Nonsense", "No control")]
    [InlineData("UiRoot/Hidden", "not on screen")]
    [InlineData("UiRoot/Disabled", "disabled")]
    public void ATargetThatCannotBeAimedAtIsRefusedRatherThanClickedNear(
        string target,
        string expected)
    {
        Ready();

        CommandResult result = Run("input-click", ("target", target));

        Assert.False(result.Ok);
        Assert.Contains(expected, result.Error);
        Assert.Empty(_ui.Injections);
    }

    // --- what each command asks for ---

    [Fact]
    public void ClickCarriesItsButtonModifiersAndCount()
    {
        Ready();

        Run(
            "input-click",
            ("target", "UiRoot/Undo"),
            ("button", "right"),
            ("modifiers", "shift,alt"),
            ("clicks", "2"));

        Injection injection = _ui.LastInjection;

        Assert.Equal("click", injection.Kind);
        Assert.Equal(MouseButton.Right, injection.Button);
        Assert.Equal(InputModifiers.Shift | InputModifiers.Alt, injection.Modifiers);
        Assert.Equal(2, injection.Count);
    }

    [Fact]
    public void DragCarriesBothEndsAndItsStepCount()
    {
        Ready();

        CommandResult result = Run(
            "input-drag",
            ("at", "100,100"),
            ("to", "300,220"),
            ("steps", "8"));

        Injection injection = _ui.LastInjection;

        Assert.Equal("drag", injection.Kind);
        Assert.Equal(new Vector2(100f, 100f), injection.Point);
        Assert.Equal(new Vector2(300f, 220f), injection.To);
        Assert.Equal(8, injection.Count);
        Assert.Equal(new[] { 300.0, 220.0 }, Field(result, "to"));
    }

    [Fact]
    public void DragDefaultsToATwelveStepPath()
    {
        Ready();

        Run("input-drag", ("at", "0,0"), ("to", "10,10"));

        Assert.Equal(12, _ui.LastInjection.Count);
    }

    /// <summary>
    /// The mouse's half of press-and-hold, and the only way anything the editor
    /// shows <em>during</em> a drag can be read: press, move, look, release.
    /// </summary>
    [Fact]
    public void AButtonCanBeHeldDownAndLetGoSeparately()
    {
        Ready();

        CommandResult pressed = Run("input-click", ("at", "10,10"), ("action", "press"));

        Assert.Equal(HoldAction.Press, _ui.LastInjection.Action);
        Assert.Equal("Press", Field(pressed, "action"));

        Run("input-click", ("at", "40,40"), ("action", "release"));

        Assert.Equal(HoldAction.Release, _ui.LastInjection.Action);
    }

    [Fact]
    public void AClickDefaultsToTapping()
    {
        Ready();

        Run("input-click", ("at", "10,10"));

        Assert.Equal(HoldAction.Tap, _ui.LastInjection.Action);
    }

    /// <summary>
    /// A held press is one press and a double click is a pair; asking for both is
    /// asking for nothing in particular, so it is refused rather than resolved by a
    /// rule nobody could guess.
    /// </summary>
    [Fact]
    public void AHeldPressCannotAlsoBeADoubleClick()
    {
        Ready();

        CommandResult result = Run(
            "input-click",
            ("at", "10,10"),
            ("action", "press"),
            ("clicks", "2"));

        Assert.False(result.Ok);
        Assert.Contains("clicks", result.Error);
    }

    [Fact]
    public void AKeyIsNamedRatherThanNumbered()
    {
        Ready();

        CommandResult result = Run("input-key", ("key", "w"));

        Assert.Equal(Key.W, _ui.LastInjection.Key);
        Assert.Equal(HoldAction.Tap, _ui.LastInjection.Action);
        Assert.Equal("W", Field(result, "key"));
    }

    /// <summary>
    /// Press and release exist for what the editor reads as held — the camera
    /// turns for as long as an arrow is down, which a tap cannot express.
    /// </summary>
    [Fact]
    public void AKeyCanBeHeldDownAndLetGoSeparately()
    {
        Ready();

        Run("input-key", ("key", "Left"), ("action", "press"));
        Assert.Equal(HoldAction.Press, _ui.LastInjection.Action);

        Run("input-key", ("key", "Left"), ("action", "release"));
        Assert.Equal(HoldAction.Release, _ui.LastInjection.Action);
    }

    [Fact]
    public void AnUnknownKeyNameIsRefused()
    {
        Ready();

        CommandResult result = Run("input-key", ("key", "Banana"));

        Assert.False(result.Ok);
        Assert.Contains("key", result.Error);
    }

    [Fact]
    public void TextIsTypedAndItsLengthReported()
    {
        Ready();

        CommandResult result = Run("input-text", ("text", "Bracket"));

        Assert.Equal("text", _ui.LastInjection.Kind);
        Assert.Equal("Bracket", _ui.LastInjection.Text);
        Assert.Equal(7, Field(result, "typed"));
    }

    [Fact]
    public void AnAbsurdlyLongStringIsRefused()
    {
        Ready();

        CommandResult result = Run("input-text", ("text", new string('x', 500)));

        Assert.False(result.Ok);
        Assert.Contains("500", result.Error);
        Assert.Empty(_ui.Injections);
    }

    [Fact]
    public void WheelCarriesItsNotchesAndReportsThem()
    {
        Ready();

        CommandResult result = Run("input-wheel", ("at", "600,400"), ("notches", "-3"));

        Assert.Equal("wheel", _ui.LastInjection.Kind);
        Assert.Equal(-3, _ui.LastInjection.Count);
        Assert.Equal(-3, Field(result, "notches"));
    }

    [Fact]
    public void AnUnknownModifierIsRefused()
    {
        Ready();

        CommandResult result = Run("input-click", ("target", "UiRoot/Undo"), ("modifiers", "hyper"));

        Assert.False(result.Ok);
        Assert.Contains("hyper", result.Error);
    }

    private static object? Field(CommandResult result, string key) =>
        ((IReadOnlyDictionary<string, object?>)result.Value!)[key];

    /// <summary>Switched on and holding control — what every gesture needs.</summary>
    private void Ready()
    {
        Enable();
        Grant();
    }

    private void Enable() =>
        _preferences.Apply(_preferences.GetValues() with { InputInjectionEnabled = true });

    private void Grant()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Grant();
    }

    /// <summary>
    /// Injected input is the hand, not an action: it is gated like an edit, because
    /// driving the interface is something an agent does while holding the editor,
    /// but it must not end a gesture — a click, a select-all and a typed value are
    /// three commands making <i>one</i> gesture, and ending it on each of them gave
    /// three records where the interface had made one.
    /// </summary>
    [Fact]
    public void DrivingTheInterfaceDoesNotEndAGestureItIsMaking()
    {
        Grant();

        Guid materialId = _project.Materials[0].Id;

        _session.BeginGesture(_project.History.CreateBatch(
            ActionKind.ChangeMaterial, Guid.Empty, materialId, _project.Materials[0].Name));

        _project.SetMaterialColor(materialId, Colors.Red);

        Run("input-click", ("at", "10,10"));
        Run("input-text", ("text", "#CC2255"));

        Assert.Empty(_project.History.Records);

        _session.EndGesture();
        Assert.Single(_project.History.Records);
    }

    private CommandResult Run(string name, params (string Key, string Value)[] arguments) =>
        _dispatcher.Execute(name, arguments.ToDictionary(pair => pair.Key, pair => pair.Value));
}
