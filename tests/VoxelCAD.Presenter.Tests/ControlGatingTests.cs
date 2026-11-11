using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The rule an agent meets over the wire: it may look and it may ask for control,
/// but it cannot edit until the user's control is handed over. What happens when a
/// <i>second</i> agent turns up is <see cref="AgentExclusivityTests"/>.
/// </summary>
public sealed class ControlGatingTests
{
    private const string Client = "agent-a";

    private readonly ControlArbiter _arbiter = new();
    private readonly FakeControlView _view = new();
    private readonly FakeSceneView _sceneView = new();
    private readonly FakePreferences _preferences = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly ControlPresenter _control;
    private readonly EditorSession _session = new();
    private readonly ICommandDispatcher _dispatcher;

    public ControlGatingTests()
    {
        // The editor already has a project open when an agent connects, so the
        // context is seeded rather than created through the gated 'new'. The control
        // presenter is the real one: the commands are a surface over it, and every
        // decision they report — refusals included — is its.
        _control = new ControlPresenter(_arbiter, _view, _sceneView, _project.History, _preferences);

        var context = new CommandContext(_project);
        var dispatchers = new AgentDispatcherProvider(
            _arbiter,
            _control,
            _session,
            controlCommands => CommandFactory.CreateDispatcher(context, controlCommands));

        _dispatcher = dispatchers.ForClient(Client);
    }

    [Fact]
    public void ReadingIsAllowedWithoutControl()
    {
        Assert.True(Run("tree").Ok);
    }

    [Fact]
    public void EditingIsRefusedWithoutControl()
    {
        CommandResult result = Run("create-element", ("name", "Group"));

        Assert.False(result.Ok);
        Assert.Contains("take-control", result.Error);
    }

    [Fact]
    public void TakingControlIsAllowedWithoutControl()
    {
        Assert.True(Run("take-control", ("name", "Claude")).Ok);
        Assert.Equal(ControlStatus.Pending, _arbiter.Status);
    }

    [Fact]
    public void EditingIsAllowedOnceControlIsGranted()
    {
        Run("take-control", ("name", "Claude"));
        _arbiter.Grant();

        Assert.True(Run("create-element", ("name", "Group")).Ok);
    }

    [Fact]
    public void EditingIsRefusedAgainAfterControlIsReleased()
    {
        Run("take-control", ("name", "Claude"));
        _arbiter.Grant();
        Run("release-control");

        Assert.False(Run("create-element", ("name", "Group")).Ok);
    }

    [Fact]
    public void EditingIsRefusedAfterTheUserTakesControlBack()
    {
        Run("take-control", ("name", "Claude"));
        _arbiter.Grant();
        _arbiter.Stop();

        Assert.False(Run("create-element", ("name", "Group")).Ok);
    }

    [Fact]
    public void ControlStatusReportsTheHolder()
    {
        Run("take-control", ("name", "Claude"));
        _arbiter.Grant();

        CommandResult result = Run("control-status");
        var value = (IReadOnlyDictionary<string, object?>)result.Value!;

        Assert.Equal("Granted", value["status"]);
        Assert.Equal(true, value["youHoldIt"]);
        Assert.Equal("Claude", value["agent"]);
    }

    [Fact]
    public void UndoIsGatedLikeAnyOtherEdit()
    {
        // Undo changes the document, so an agent must hold control to reach it.
        CommandResult result = Run("undo");

        Assert.False(result.Ok);
        Assert.Contains("take-control", result.Error);
    }

    /// <summary>
    /// Every command an agent sends is proof it is still there — a read, and even
    /// an edit the gate refuses, so a polling agent is never cut off for silence.
    /// </summary>
    [Fact]
    public void EveryCommandStampsLiveness()
    {
        var stamps = 0;
        _arbiter.AgentCommandReceived += client =>
        {
            Assert.Equal(Client, client);
            stamps++;
        };

        Run("tree");
        Assert.Equal(1, stamps);

        // Refused for want of control, but the attempt still counts as life.
        Run("create-element", ("name", "Group"));
        Assert.Equal(2, stamps);
    }

    [Fact]
    public void ReleasePassesKeepCameraThrough()
    {
        Run("take-control", ("name", "Claude"));
        _arbiter.Grant();

        CameraPose agentPose = new(new Godot.Vector3(9f, 9f, 9f), 90f, 0f, 40f);
        _sceneView.CameraPose = agentPose;

        Run("release-control", ("keep-camera", "true"));

        Assert.Equal(agentPose, _sceneView.CameraPose);
    }

    [Fact]
    public void ReleaseDefaultsToRestoringTheCamera()
    {
        CameraPose userPose = new(new Godot.Vector3(1f, 2f, 3f), 10f, -20f, 24f);
        _sceneView.CameraPose = userPose;

        Run("take-control", ("name", "Claude"));
        _arbiter.Grant();

        _sceneView.CameraPose = new CameraPose(new Godot.Vector3(9f, 9f, 9f), 90f, 0f, 40f);
        Run("release-control");

        Assert.Equal(userPose, _sceneView.CameraPose);
    }

    [Fact]
    public void ReleasingWithoutHoldingControlIsRefused()
    {
        CommandResult result = Run("release-control");

        Assert.False(result.Ok);
        Assert.Contains("nothing to hand back", result.Error);
    }

    [Fact]
    public void ControlStatusTellsATimeoutFromARefusal()
    {
        Run("take-control", ("name", "Claude"));
        _arbiter.Grant();
        _arbiter.TimeOut();

        CommandResult result = Run("control-status");
        var value = (IReadOnlyDictionary<string, object?>)result.Value!;

        // Distinct from "Denied", which is what a user cancelling would leave.
        Assert.Equal("TimedOut", value["status"]);
    }

    /// <summary>
    /// The canon's "anything that starts a new action ends the one in flight first",
    /// reaching the surface that is the GUI's peer. A colour field holds its gesture
    /// open while it has the keyboard, so an agent's edit arriving meanwhile would
    /// otherwise be recorded <i>before</i> the earlier one — and an 'undo' would then
    /// reverse the record underneath and take the user's edit with it, with nothing
    /// in the history to say either had happened. Driven on the running editor before
    /// it was fixed.
    /// </summary>
    [Fact]
    public void AnAgentsEditEndsAGestureTheInterfaceLeftOpen()
    {
        Run("take-control", ("name", "Claude"));
        _arbiter.Grant();

        Guid materialId = _project.Materials[0].Id;

        _session.BeginGesture(_project.History.CreateBatch(
            ActionKind.ChangeMaterial, Guid.Empty, materialId, _project.Materials[0].Name));

        _project.SetMaterialColor(materialId, Colors.Red);

        // Nothing published while the gesture is open — that is what makes the
        // ordering wrong rather than merely untidy.
        Assert.Empty(_project.History.Records);

        Run("create-element", ("name", "Group"));

        Assert.Equal(
            new[] { ActionKind.ChangeMaterial, ActionKind.CreateElement },
            _project.History.Records.Select(record => record.Kind));
    }

    /// <summary>
    /// And only an edit does: looking is what an agent does while a user is
    /// mid-drag, and a read that cut the drag in half would be the cure doing the
    /// disease's work.
    /// </summary>
    [Fact]
    public void AReadLeavesAGestureAlone()
    {
        Run("take-control", ("name", "Claude"));
        _arbiter.Grant();

        Guid materialId = _project.Materials[0].Id;

        _session.BeginGesture(_project.History.CreateBatch(
            ActionKind.ChangeMaterial, Guid.Empty, materialId, _project.Materials[0].Name));

        _project.SetMaterialColor(materialId, Colors.Red);

        Assert.True(Run("tree").Ok);
        Assert.Empty(_project.History.Records);

        _session.EndGesture();
        Assert.Single(_project.History.Records);
    }

    private CommandResult Run(string name, params (string Key, string Value)[] arguments) =>
        _dispatcher.Execute(name, arguments.ToDictionary(pair => pair.Key, pair => pair.Value));

    /// <summary>
    /// The topic rides the same negotiation: announced with take-control,
    /// restated with report-topic, echoed by control-status — and report-topic,
    /// being a read of nothing and a write of nothing in the document, needs no
    /// control to send.
    /// </summary>
    [Fact]
    public void TheTopicIsAnnouncedRestatedAndEchoed()
    {
        Run("take-control", ("name", "Claude"), ("topic", "Building a tank"));
        Assert.Equal("Building a tank", _arbiter.Topic);

        _arbiter.Grant();
        Assert.True(Run("report-topic", ("topic", "Painting the turret")).Ok);
        Assert.Equal("Painting the turret", _arbiter.Topic);

        CommandResult status = Run("control-status");
        Assert.Equal("Painting the turret", ((IReadOnlyDictionary<string, object?>)status.Value!)["topic"]);
    }

    /// <summary>
    /// A bare take-control — a user's own three-line script, not a polished
    /// client — passes no name and no topic, and nothing here invents one: the
    /// arbiter keeps the emptiness, and the plaque and the history word the
    /// anonymous case themselves instead of quoting a placeholder as if it were
    /// a chosen name.
    /// </summary>
    [Fact]
    public void ABareRequestStaysAnonymousRatherThanWearingAPlaceholder()
    {
        Assert.True(Run("take-control").Ok);

        Assert.Equal(string.Empty, _arbiter.AgentName);
        Assert.Equal(string.Empty, _arbiter.Topic);
    }
}
