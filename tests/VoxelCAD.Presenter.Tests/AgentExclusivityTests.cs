using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VoxelCAD.Commands;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Two agents pointed at one editor. Control is exclusive to <b>one</b> of them,
/// and the second one is refused rather than quietly let in: it cannot edit, it
/// cannot end the first one's session, it cannot write on its plaque, and — this is
/// the one that used to bite — it cannot read the first one's grant as its own.
/// </summary>
/// <remarks>
/// Waiting is the answer the refusal offers: a line, served in the order agents
/// asked, whose head becomes an ordinary request the moment the editor is free.
/// </remarks>
public sealed class AgentExclusivityTests
{
    private const string First = "agent-a";
    private const string Second = "agent-b";
    private const string Third = "agent-c";

    private readonly ControlArbiter _arbiter = new();
    private readonly FakeControlView _view = new();
    private readonly FakeSceneView _sceneView = new();
    private readonly FakePreferences _preferences = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly ControlPresenter _control;
    private readonly IAgentDispatcherProvider _dispatchers;

    public AgentExclusivityTests()
    {
        _control = new ControlPresenter(_arbiter, _view, _sceneView, _project.History, _preferences);

        var context = new CommandContext(_project);

        _dispatchers = new AgentDispatcherProvider(
            _arbiter,
            _control,
            new EditorSession(),
            controlCommands => CommandFactory.CreateDispatcher(context, controlCommands));
    }

    [Fact]
    public void ASecondAgentAskingForControlIsRefusedAndToldWhoHasIt()
    {
        GrantTo(First, "Claude", "Building a tank");

        CommandResult result = Run(Second, "take-control", ("name", "Other"));

        Assert.False(result.Ok);
        Assert.Contains("Claude", result.Error);
        Assert.Contains("Building a tank", result.Error);
        Assert.Contains("wait=", result.Error);
    }

    /// <summary>
    /// The hole this whole arrangement closes: the status a second agent reads must
    /// never be the word it would act on. It used to read "Granted" — the first
    /// agent's grant, reported back as if it were its own.
    /// </summary>
    [Fact]
    public void ASecondAgentIsNeverToldTheEditorIsGrantedToIt()
    {
        GrantTo(First, "Claude", string.Empty);

        IReadOnlyDictionary<string, object?> status = Status(Second);

        Assert.Equal("HeldByAnotherAgent", status["status"]);
        Assert.Equal(false, status["youHoldIt"]);

        // And the holder's own reading is unchanged by the newcomer's.
        Assert.Equal("Granted", Status(First)["status"]);
        Assert.Equal(true, Status(First)["youHoldIt"]);
    }

    [Fact]
    public void ASecondAgentCannotEditWhileTheFirstHoldsControl()
    {
        GrantTo(First, "Claude", string.Empty);

        CommandResult result = Run(Second, "create-element", ("name", "Group"));

        Assert.False(result.Ok);
        Assert.Contains("Claude", result.Error);
        Assert.Contains("holds the editor", result.Error);

        // And the first agent's own edits still pass, which is the other half of
        // "exclusive": the gate refuses the newcomer, not everybody.
        Assert.True(Run(First, "create-element", ("name", "Group")).Ok);
    }

    [Fact]
    public void ASecondAgentCannotEndTheFirstOnesSession()
    {
        GrantTo(First, "Claude", string.Empty);

        CommandResult result = Run(Second, "release-control");

        Assert.False(result.Ok);
        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
        Assert.True(_arbiter.IsHeldBy(First));
    }

    [Fact]
    public void ASecondAgentCannotWriteOnTheFirstOnesPlaque()
    {
        GrantTo(First, "Claude", "Building a tank");

        CommandResult result = Run(Second, "report-topic", ("topic", "Something else"));

        Assert.False(result.Ok);
        Assert.Equal("Building a tank", _arbiter.Topic);
        Assert.Equal("Building a tank", _view.Topic);
    }

    /// <summary>
    /// Waiting is what the refusal offers instead of a queue: the call comes back
    /// the moment the editor is the caller's, and until then it is not an answer at
    /// all.
    /// </summary>
    [Fact]
    public async Task AWaitingAgentIsGrantedTheEditorWhenTheHolderReleasesIt()
    {
        GrantTo(First, "Claude", string.Empty);

        CommandResult reserved = Run(Second, "take-control", ("name", "Other"), ("wait", "60"));

        Assert.True(reserved.Ok);
        Assert.Equal("Waiting", Field(reserved.Value, "status"));

        Task<CommandResult>? pending = reserved.Pending;
        Assert.NotNull(pending);
        Assert.False(pending!.IsCompleted);

        Run(First, "release-control");

        // One frame promotes the reservation into an ordinary request; the countdown
        // then runs exactly as it would for an agent that had asked from cold.
        _control.Tick(0.1);
        Assert.Equal(ControlStatus.Pending, _arbiter.Status);
        Assert.False(pending.IsCompleted);

        _control.Tick(_preferences.CountdownSeconds);

        Assert.Equal(ControlStatus.Granted, _arbiter.Status);
        Assert.True(_arbiter.IsHeldBy(Second));
        Assert.True(pending.IsCompleted);
        Assert.Equal("Granted", Field((await pending).Value, "status"));
        Assert.True(Run(Second, "create-element", ("name", "Group")).Ok);
    }

    /// <summary>
    /// The line is served in arrival order — the only fairness rule that needs no
    /// explaining to whoever loses by it — and each agent is told where it stands.
    /// </summary>
    [Fact]
    public async Task AgentsAreServedInTheOrderTheyAsked()
    {
        GrantTo(First, "Claude", string.Empty);

        CommandResult secondWait = Run(Second, "take-control", ("name", "Other"), ("wait", "60"));
        CommandResult thirdWait = Run(Third, "take-control", ("name", "Third"), ("wait", "60"));

        Assert.True(secondWait.Ok);
        Assert.True(thirdWait.Ok);
        Assert.Equal(1, Status(Second)["queuePosition"]);
        Assert.Equal(2, Status(Third)["queuePosition"]);
        Assert.Equal(2, Status(First)["waitingAgents"]);

        Run(First, "release-control");
        _control.Tick(0.1);
        _control.Tick(_preferences.CountdownSeconds);

        // The head of the line got it, and the one behind moved up rather than being
        // granted anything.
        Assert.True(_arbiter.IsHeldBy(Second));
        Assert.True(secondWait.Pending!.IsCompleted);
        Assert.False(thirdWait.Pending!.IsCompleted);
        Assert.Equal(1, Status(Third)["queuePosition"]);

        Run(Second, "release-control");
        _control.Tick(0.1);
        _control.Tick(_preferences.CountdownSeconds);

        Assert.True(_arbiter.IsHeldBy(Third));
        Assert.Equal("Granted", Field((await thirdWait.Pending!).Value, "status"));
    }

    /// <summary>
    /// Asking again while already in line renews the place rather than taking a
    /// second one — or a polling agent would push itself to the back of the queue it
    /// was politely holding.
    /// </summary>
    [Fact]
    public void AskingAgainKeepsTheSamePlaceInLine()
    {
        GrantTo(First, "Claude", string.Empty);

        Run(Second, "take-control", ("name", "Other"), ("wait", "60"));
        Run(Third, "take-control", ("name", "Third"), ("wait", "60"));
        Run(Second, "take-control", ("name", "Other"), ("wait", "60"));

        Assert.Equal(2, Status(First)["waitingAgents"]);
        Assert.Equal(1, Status(Second)["queuePosition"]);
        Assert.Equal(2, Status(Third)["queuePosition"]);
    }

    /// <summary>
    /// The line is bounded. Past its length a newcomer is told plainly rather than
    /// promised a turn that far off — and a rude client cannot grow it without limit
    /// by minting session ids.
    /// </summary>
    [Fact]
    public void TheLineHasAnEnd()
    {
        GrantTo(First, "Claude", string.Empty);

        for (var i = 0; i < 8; i++)
            Assert.True(Run($"queued-{i}", "take-control", ("name", $"Agent {i}"), ("wait", "60")).Ok);

        CommandResult overflow = Run("one-too-many", "take-control", ("name", "Late"), ("wait", "60"));

        Assert.False(overflow.Ok);
        Assert.Contains("as many as the line takes", overflow.Error);
        Assert.Equal(8, Status(First)["waitingAgents"]);
    }

    /// <summary>The user, and anybody reading the status, is told who is queued.</summary>
    [Fact]
    public void TheQueueIsShownOnThePlaqueAndInTheStatus()
    {
        GrantTo(First, "Claude", string.Empty);
        Run(Second, "take-control", ("name", "Other"), ("wait", "60"));
        Run(Third, "take-control", ("name", "Third"), ("wait", "60"));

        Assert.Equal(2, _view.State.QueuedAgents);
        Assert.Equal("Other", _view.State.WaitingAgent);

        IReadOnlyDictionary<string, object?> status = Status(First);
        Assert.Equal(2, status["waitingAgents"]);
        Assert.Equal("Other", status["nextInLine"]);
        Assert.False(status.ContainsKey("queuePosition"));

        // And the holder's own place is nowhere, which is not the same as first.
        Assert.Equal("Waiting", Status(Second)["status"]);
        Assert.Equal(1, Status(Second)["queuePosition"]);
    }

    /// <summary>
    /// The call is capped, the waiting is not: running out of call is answered with
    /// the status and the place in line still standing, so an agent waits an hour by
    /// asking again rather than by holding one request open for one.
    /// </summary>
    [Fact]
    public async Task AWaitThatRunsOutKeepsThePlaceInLine()
    {
        GrantTo(First, "Claude", string.Empty);

        CommandResult reserved = Run(Second, "take-control", ("name", "Other"), ("wait", "5"));
        Task<CommandResult> pending = reserved.Pending!;

        _control.Tick(5.0);

        Assert.True(pending.IsCompleted);

        CommandResult settled = await pending;
        Assert.True(settled.Ok);
        Assert.Equal("Waiting", Field(settled.Value, "status"));
        Assert.Equal(1, _view.State.QueuedAgents);

        // Asking again is the same place renewed, not a second one refused.
        Assert.True(Run(Second, "take-control", ("name", "Other"), ("wait", "5")).Ok);
    }

    /// <summary>
    /// The Stop button means "not now", and it would mean nothing if the next agent
    /// in line took the editor a frame later. So a refusal lets the whole line go,
    /// and tells everybody in it.
    /// </summary>
    [Fact]
    public async Task TheUserTakingControlBackLetsTheWholeLineGo()
    {
        GrantTo(First, "Claude", string.Empty);

        CommandResult reserved = Run(Second, "take-control", ("name", "Other"), ("wait", "60"));
        CommandResult behind = Run(Third, "take-control", ("name", "Third"), ("wait", "60"));
        Task<CommandResult> pending = reserved.Pending!;

        _arbiter.Stop();

        Assert.True(pending.IsCompleted);
        Assert.False((await pending).Ok);
        Assert.True(behind.Pending!.IsCompleted);
        Assert.False((await behind.Pending!).Ok);
        Assert.Equal(0, _view.State.QueuedAgents);

        // Nothing is promoted: the editor stays the user's until somebody asks again.
        _control.Tick(10.0);
        Assert.True(_arbiter.IsUserInControl);
    }

    /// <summary>
    /// A place held for an agent that has gone is a place nobody can use, so a place
    /// with no call in flight expires on the same silence terms a holder's control
    /// does.
    /// </summary>
    [Fact]
    public void APlaceNobodyIsHoldingOpenGoesStale()
    {
        GrantTo(First, "Claude", string.Empty);

        Run(Second, "take-control", ("name", "Other"), ("wait", "5"));

        // The call runs out first; from then on nothing is holding the place.
        _control.Tick(5.0);
        Assert.Equal(1, _view.State.QueuedAgents);

        // The holder keeps speaking meanwhile, so what is being measured is the
        // waiting agent's silence and not the editor changing hands underneath it.
        _control.Tick(50.0);
        Run(First, "tree");
        _control.Tick(45.0);

        Assert.True(_arbiter.IsHeldBy(First));
        Assert.Equal(0, _view.State.QueuedAgents);

        // And the place it lost is a place somebody else can have.
        Assert.True(Run(Third, "take-control", ("name", "Third"), ("wait", "5")).Ok);
        Assert.Equal(1, Status(Third)["queuePosition"]);
    }

    /// <summary>
    /// A holder that falls silent loses the editor to the agent waiting for it —
    /// nobody refused anything here, so unlike the Stop button this promotes.
    /// </summary>
    [Fact]
    public async Task AWaitingAgentInheritsFromAHolderThatFallsSilent()
    {
        GrantTo(First, "Claude", string.Empty);

        CommandResult reserved = Run(Second, "take-control", ("name", "Other"), ("wait", "120"));
        Task<CommandResult> pending = reserved.Pending!;

        // The holder says nothing for long enough to lose the editor. The waiting
        // agent's own calls keep its reservation alive meanwhile.
        _control.Tick(_preferences.SilenceWarningSeconds + _preferences.SilenceGraceSeconds);
        Assert.Equal(ControlStatus.TimedOut, _arbiter.Status);

        _control.Tick(0.1);
        _control.Tick(_preferences.CountdownSeconds);

        Assert.True(_arbiter.IsHeldBy(Second));
        Assert.True(pending.IsCompleted);
        Assert.Equal("Granted", Field((await pending).Value, "status"));
    }

    /// <summary>
    /// Changing its mind is the same sentence one step earlier, so it goes through
    /// the same command. Without this a place in line could not be given up at all: it
    /// would stand until it went stale, and the agent that no longer wanted the
    /// editor would be the one handed it.
    /// </summary>
    [Fact]
    public async Task AWaitingAgentCanWithdrawWithReleaseControl()
    {
        GrantTo(First, "Claude", string.Empty);

        CommandResult reserved = Run(Second, "take-control", ("name", "Other"), ("wait", "60"));
        Task<CommandResult> pending = reserved.Pending!;

        CommandResult withdrawn = Run(Second, "release-control");

        Assert.True(withdrawn.Ok);
        Assert.Equal(0, _view.State.QueuedAgents);
        Assert.True(pending.IsCompleted);
        Assert.True((await pending).Ok);

        // The slot is free again, and letting go really did let go: the editor
        // returns to the user rather than to the agent that withdrew.
        Assert.True(Run(Third, "take-control", ("name", "Third"), ("wait", "60")).Ok);

        Run(First, "release-control");
        _control.Tick(0.1);
        _control.Tick(_preferences.CountdownSeconds);

        Assert.True(_arbiter.IsHeldBy(Third));
    }

    /// <summary>
    /// A refusal reaches the agent it was aimed at and nobody else: an unrelated
    /// agent finds an editor that is simply the user's again.
    /// </summary>
    [Fact]
    public void AVerdictIsReportedToTheAgentItWasAimedAt()
    {
        GrantTo(First, "Claude", string.Empty);
        _arbiter.Stop();

        Assert.Equal("Denied", Status(First)["status"]);
        Assert.Equal("Available", Status(Second)["status"]);
    }

    /// <summary>
    /// The endpoint going down leaves nobody to hand anything to, and a blocked call
    /// must not be left waiting on a task nothing will ever complete.
    /// </summary>
    [Fact]
    public async Task LosingTheEndpointAnswersEverySleepingCall()
    {
        GrantTo(First, "Claude", string.Empty);

        CommandResult reserved = Run(Second, "take-control", ("name", "Other"), ("wait", "60"));
        Task<CommandResult> pending = reserved.Pending!;

        _control.Abandon();

        Assert.True(pending.IsCompleted);
        Assert.False((await pending).Ok);
        Assert.Equal(ControlStatus.Available, _arbiter.Status);
        Assert.Equal(0, _view.State.QueuedAgents);
    }

    private void GrantTo(string clientId, string agentName, string topic)
    {
        Run(clientId, "take-control", ("name", agentName), ("topic", topic));
        _arbiter.Grant();
    }

    private IReadOnlyDictionary<string, object?> Status(string clientId) =>
        (IReadOnlyDictionary<string, object?>)Run(clientId, "control-status").Value!;

    private static object? Field(object? value, string key) =>
        ((IReadOnlyDictionary<string, object?>)value!)[key];

    private CommandResult Run(
        string clientId,
        string name,
        params (string Key, string Value)[] arguments) =>
        _dispatchers
            .ForClient(clientId)
            .Execute(name, arguments.ToDictionary(pair => pair.Key, pair => pair.Value));
}
