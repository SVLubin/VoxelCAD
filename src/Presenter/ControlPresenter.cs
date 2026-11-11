using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Preferences;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Drives the handover: turns the arbiter's state into the banner, runs the
/// countdown, watches an agent for silence, and marks the history with whoever is
/// editing. This is where the decisions the arbiter deliberately left out — the
/// ones that need a clock or preferences — are made.
/// </summary>
/// <remarks>
/// Every threshold comes from preferences, read at the moment it matters rather
/// than cached, so a setting changed between one handover and the next is honoured.
/// Time enters only through <see cref="Tick"/>; there is no <c>DateTime.Now</c>
/// here, which is what lets the whole handover be tested by advancing a number.
/// <para>
/// The <b>queue</b> lives here for the same reason the countdown does: it is a
/// decision with a clock in it. Agents are served in the order they asked, the head
/// of the line is granted the editor when the holder <i>releases</i> or falls
/// silent — never when the <b>user</b> takes it back, since a user who reached for
/// the Stop button has said "not now", and handing the editor straight to the next
/// agent in line would make that button a joke. The line is short by policy
/// (<see cref="MaximumQueuedAgents"/>): a handful of agents wanting one editor is a
/// case worth answering in order, a hundred is a case worth refusing.
/// </para>
/// </remarks>
internal sealed class ControlPresenter : PresenterBase
{
    /// <summary>
    /// The longest one <c>take-control</c> call is allowed to sit blocked. It is a
    /// cap on the <i>call</i>, not on the waiting: the reservation outlives it, so
    /// an agent waits an hour by asking again, and every client's own tool-call
    /// timeout stays comfortably out of the way.
    /// </summary>
    private const double MaximumWaitCallSeconds = 120.0;

    /// <summary>
    /// How many agents may stand in line. A bound rather than a judgement about
    /// fairness: the line is served in order, and this only says that an editor with
    /// a crowd behind it is a situation to refuse plainly rather than to promise a
    /// turn nobody will live to see. It also keeps a rude client from growing the
    /// line without limit, since a client id is whatever a header says.
    /// </summary>
    private const int MaximumQueuedAgents = 8;

    private readonly ControlArbiter _arbiter;
    private readonly IControlView _view;
    private readonly ISceneView _sceneView;
    private readonly IEditorPreferences _preferences;

    /// <summary>
    /// The calls left blocked, each waiting to be told how its request ended. Small
    /// by construction — one agent may wait, and an agent has one call in flight
    /// unless it retried before its last one came back.
    /// </summary>
    private readonly List<PendingWait> _waitsList = new();

    /// <summary>
    /// The agents waiting their turn, head first. Order is arrival order and nothing
    /// else — the only fairness rule that needs no explaining to whoever loses by it.
    /// </summary>
    private readonly List<QueuedAgent> _queueList = new();

    private IHistory _history;

    /// <summary>
    /// Whether the agent held control on the last change, so a transition into or
    /// out of that state can be acted on once, on its edge.
    /// </summary>
    private bool _agentHadControl;

    /// <summary>The user's camera as it was when the handover began, to put back on release.</summary>
    private CameraPose _savedPose;

    /// <summary>
    /// Whether the saved camera is restored when control returns. True by default
    /// and reset so on every handover; only an agent releasing with keep-camera
    /// turns it off, and only for that one return.
    /// </summary>
    private bool _restoreCamera = true;

    /// <summary>Seconds left on the countdown before an agent is granted control.</summary>
    private double _remaining;

    /// <summary>Total time a request has been pending, whether or not the user is busy.</summary>
    private double _pending;

    /// <summary>How long the agent has held control — the running clock on the banner.</summary>
    private double _elapsed;

    /// <summary>Time since the agent's last command; reset by every one it sends.</summary>
    private double _silence;

    /// <summary>The last state painted, so repainting is skipped when nothing changed.</summary>
    private ControlBannerState _painted;

    /// <summary>
    /// The status the last change left, so the clocks reset only on an actual
    /// transition. The arbiter's Changed also fires for a restated topic, and a
    /// topic must not zero the running clock of a session it did not start.
    /// </summary>
    private ControlStatus _lastStatus;

    /// <summary>
    /// How many grants there have been, which is what stamps a record with the
    /// session it belongs to. Counted here rather than in the arbiter because it
    /// is bookkeeping for the history's benefit, not part of deciding who holds
    /// the editor. It outlives the document along with this presenter, so two
    /// sessions never share a number even across an open.
    /// </summary>
    private int _session;

    public ControlPresenter(
        ControlArbiter arbiter,
        IControlView view,
        ISceneView sceneView,
        IHistory history,
        IEditorPreferences preferences)
    {
        _arbiter = arbiter;
        _view = view;
        _sceneView = sceneView;
        _history = history;
        _preferences = preferences;

        _arbiter.Changed += OnControlChanged;
        _arbiter.AgentCommandReceived += OnAgentCommand;
        _view.CancelControlRequested += OnCancel;
        _view.StopControlRequested += OnStop;
        _view.UserInteractionBegan += OnInteractionBegan;
        _view.UserInteractionEnded += OnInteractionEnded;

        Refresh();
    }

    /// <summary>
    /// Points the actor stamp at another document. This presenter outlives the
    /// document — a handover in progress is not interrupted by opening a file —
    /// so only the stamp moves; the banner, the clocks and the busy count stay
    /// exactly where they were.
    /// </summary>
    public void SetHistory(IHistory history)
    {
        if (ReferenceEquals(_history, history))
            return;

        _history.CurrentActor = default;
        _history = history;

        ApplyActor();
    }

    protected override void OnDispose()
    {
        _arbiter.Changed -= OnControlChanged;
        _arbiter.AgentCommandReceived -= OnAgentCommand;
        _view.CancelControlRequested -= OnCancel;
        _view.StopControlRequested -= OnStop;
        _view.UserInteractionBegan -= OnInteractionBegan;
        _view.UserInteractionEnded -= OnInteractionEnded;
    }

    /// <summary>Advances the clocks. Called once per frame from the composition root.</summary>
    public void Tick(double delta)
    {
        // Before the phase clocks: the head of the line promoted this frame becomes
        // a pending request, and the countdown below is the one that runs it.
        TickWaits(delta);
        TickQueue(delta);

        switch (_arbiter.Status)
        {
            case ControlStatus.Pending:
                TickPending(delta);
                break;

            case ControlStatus.Granted:
                TickGranted(delta);
                break;
        }
    }

    /// <summary>
    /// An agent asks for the editor, optionally waiting for its turn. Everything
    /// the arbiter cannot decide alone is decided here: whether a newcomer meets a
    /// refusal or a reservation, and whether the answer is sent now or held until
    /// the request is settled.
    /// </summary>
    /// <param name="waitSeconds">
    /// How long the caller is prepared to sit blocked. Zero answers at once —
    /// which for an editor another agent holds is a refusal, never a status that
    /// could be mistaken for a grant.
    /// </param>
    public CommandResult RequestControl(
        string clientId,
        string agentName,
        string topic,
        double waitSeconds)
    {
        if (_arbiter.IsClaimedByAnother(clientId))
            return RequestBehindAnother(clientId, agentName, topic, waitSeconds);

        _arbiter.Request(clientId, agentName, topic);

        // Granted already (an agent asking twice) needs no waiting; a pending
        // request does, and that is what a caller asking to wait is waiting for.
        if (waitSeconds > 0.0 && _arbiter.Status == ControlStatus.Pending)
            return BeginWait(clientId, waitSeconds);

        return GetStatus(clientId);
    }

    /// <summary>
    /// The agent restates its topic. Refused for anybody but the agent in the
    /// negotiation: the plaque is the user's window onto who is working, and a
    /// bystander writing on it would make it lie.
    /// </summary>
    public CommandResult ReportTopic(string clientId, string topic)
    {
        if (!_arbiter.SetTopic(clientId, topic))
        {
            return CommandResult.Failure(
                _arbiter.IsClaimedByAnother(clientId)
                    ? "Another agent holds the editor, so this topic would be written on its "
                      + "plaque. Ask for control first — 'control-status' says who has it."
                    : "You are not in the handover, so there is nobody to attribute a topic to. "
                      + "Call 'take-control' first.");
        }

        return GetStatus(clientId);
    }

    /// <summary>
    /// The agent hands control back. keep-camera means it wants to leave the user
    /// looking at what it built, so the pre-handover view is not restored; without
    /// it the default holds and the user's camera is put back.
    /// </summary>
    /// <remarks>
    /// Refused for anybody but the holder. A release from another agent used to end
    /// a session it had never started — the sharpest of the confusions that came of
    /// the editor knowing only that <i>an</i> agent held it.
    /// </remarks>
    public CommandResult ReleaseControl(string clientId, bool keepCamera)
    {
        if (_arbiter.IsHeldBy(clientId))
        {
            _restoreCamera = !keepCamera;
            _arbiter.Release(clientId);

            return GetStatus(clientId);
        }

        // Waiting rather than holding is the same sentence one step earlier — "I no
        // longer want the editor" — and it has to be sayable: a place in line nobody
        // can give up stands until it goes stale, and the agent that changed its mind
        // is the one handed the editor when the holder lets go.
        if (RemoveFromLine(clientId))
        {
            SettleWaitsFor(clientId, CommandResult.Success(BuildReport(clientId)));
            Refresh();

            return GetStatus(clientId);
        }

        return CommandResult.Failure(
            _arbiter.IsClaimedByAnother(clientId)
                ? "You do not hold the editor — another agent does, and its session is not "
                  + "yours to end. 'control-status' says who has it."
                : "You do not hold the editor, so there is nothing to hand back.");
    }

    /// <summary>What this caller is told about the editor — see <see cref="ControlStatusReport"/>.</summary>
    public CommandResult GetStatus(string clientId) =>
        CommandResult.Success(BuildReport(clientId));

    /// <summary>
    /// The transport is gone, so nobody is left to hand anything to: the handover is
    /// dropped, the reservation with it, and every blocked call is answered rather
    /// than left hanging on a task nothing will ever complete.
    /// </summary>
    public void Abandon()
    {
        _arbiter.Abandon();
        _queueList.Clear();
        SettleAll(CommandResult.Failure("The editor's agent endpoint went down while you waited."));
    }

    /// <summary>
    /// Runs the countdown before an agent is granted control. A busy user holds it
    /// off — but only until the maximum wait falls through, so a leaked interaction
    /// token can delay the handover, never stall it for good.
    /// </summary>
    private void TickPending(double delta)
    {
        _pending += delta;

        if (_arbiter.IsUserBusy && !HasWaitedLongEnough())
        {
            Refresh();
            return;
        }

        _remaining -= delta;

        if (_remaining <= 0.0)
        {
            // Grant raises Changed, which repaints the banner as AgentWorking.
            _arbiter.Grant();
            return;
        }

        Refresh();
    }

    /// <summary>
    /// Watches an agent that holds control. Long work is not the enemy — silence
    /// is. Past the warning threshold the banner says the agent is not responding;
    /// past a further grace period control returns to the user, and never without
    /// that warning having been shown first.
    /// </summary>
    private void TickGranted(double delta)
    {
        _elapsed += delta;
        _silence += delta;

        if (_silence >= _preferences.SilenceWarningSeconds + _preferences.SilenceGraceSeconds)
        {
            // TimeOut raises Changed, which clears the banner.
            _arbiter.TimeOut();
            return;
        }

        Refresh();
    }

    /// <summary>
    /// Counts down every blocked call and answers the ones that ran out. Running out
    /// is not a failure: the reservation stands, so the honest answer is the status
    /// with the hint to ask again.
    /// </summary>
    private void TickWaits(double delta)
    {
        for (int i = _waitsList.Count - 1; i >= 0; i--)
        {
            PendingWait wait = _waitsList[i];
            wait.Remaining -= delta;

            if (wait.Remaining <= 0.0)
                Settle(i, GetStatus(wait.ClientId));
        }
    }

    /// <summary>
    /// Moves the line along: the head becomes the pending request as soon as the
    /// editor is free, and anybody who stops asking loses their place.
    /// </summary>
    private void TickQueue(double delta)
    {
        if (_queueList.Count == 0)
            return;

        if (!_arbiter.HasAgent)
        {
            PromoteHeadOfLine();
            return;
        }

        var dropped = false;

        for (int i = _queueList.Count - 1; i >= 0; i--)
        {
            QueuedAgent queued = _queueList[i];

            // A call in flight is proof enough: the client is sitting on the other
            // end of it. Only a place nobody is holding open can go stale.
            if (HasWaitInFlight(queued.ClientId))
            {
                queued.Silence = 0.0;
                continue;
            }

            queued.Silence += delta;

            if (queued.Silence < _preferences.SilenceWarningSeconds + _preferences.SilenceGraceSeconds)
                continue;

            _queueList.RemoveAt(i);
            dropped = true;
        }

        if (dropped)
            Refresh();
    }

    /// <summary>
    /// The editor is free and somebody was waiting: the first in line has their
    /// request put in, and from there it is an ordinary handover — countdown, plaque,
    /// and a user who may still refuse it.
    /// </summary>
    private void PromoteHeadOfLine()
    {
        QueuedAgent head = _queueList[0];
        _queueList.RemoveAt(0);

        _arbiter.Request(head.ClientId, head.Name, head.Topic);
    }

    /// <summary>
    /// A newcomer meeting an editor another agent has. Waiting is opt-in, and
    /// anything else is a refusal — the one answer a second agent must not be able to
    /// read as permission to edit.
    /// </summary>
    private CommandResult RequestBehindAnother(
        string clientId,
        string agentName,
        string topic,
        double waitSeconds)
    {
        if (waitSeconds <= 0.0)
        {
            return CommandResult.Failure(
                $"{Describe()} Pass 'wait=<seconds>' to join the line and be told when the "
                + "editor is yours; nothing you send meanwhile will edit the document.");
        }

        QueuedAgent? queued = FindQueued(clientId);

        if (queued != null)
        {
            // Asking again is the same place in line, renewed — not a second place,
            // and not the back of the queue as a penalty for checking in.
            queued.Name = agentName;
            queued.Topic = topic;
            queued.Silence = 0.0;
        }
        else
        {
            if (_queueList.Count >= MaximumQueuedAgents)
            {
                return CommandResult.Failure(
                    $"{Describe()} {_queueList.Count} agents are already waiting for it, which "
                    + "is as many as the line takes — come back later rather than being promised "
                    + "a turn that far off.");
            }

            _queueList.Add(new QueuedAgent
            {
                ClientId = clientId,
                Name = agentName,
                Topic = topic
            });
        }

        Refresh();

        return BeginWait(clientId, waitSeconds);
    }

    /// <summary>
    /// Holds this call's answer until the caller's request is settled — granted, or
    /// refused — or until the cap runs out. The immediate value is a complete answer
    /// in itself, which is what lets a transport that cannot wait ignore the rest.
    /// </summary>
    private CommandResult BeginWait(string clientId, double waitSeconds)
    {
        var wait = new PendingWait
        {
            ClientId = clientId,
            Remaining = Math.Min(waitSeconds, MaximumWaitCallSeconds),

            // The main thread completes these, from inside a command or a frame
            // tick; a continuation must not run there.
            Completion = new TaskCompletionSource<CommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };

        _waitsList.Add(wait);

        return CommandResult.Waiting(BuildReport(clientId), wait.Completion.Task);
    }

    /// <summary>
    /// Answers whichever blocked calls now have their answer: the editor became
    /// theirs, or their request was refused. Everyone else keeps waiting.
    /// </summary>
    private void SettleDecidedWaits()
    {
        for (int i = _waitsList.Count - 1; i >= 0; i--)
        {
            string clientId = _waitsList[i].ClientId;

            if (_arbiter.IsHeldBy(clientId))
            {
                Settle(i, GetStatus(clientId));
                continue;
            }

            if (_arbiter.Status is ControlStatus.Denied or ControlStatus.TimedOut
                && _arbiter.IsVerdictFor(clientId))
            {
                Settle(
                    i,
                    CommandResult.Failure(
                        _arbiter.Status == ControlStatus.Denied
                            ? "The user refused to hand the editor over. That is their decision; "
                              + "do not ask again unless they say so."
                            : "You lost the editor to your own silence before your turn came."));
            }
        }
    }

    private bool HasWaitInFlight(string clientId)
    {
        foreach (PendingWait wait in _waitsList)
        {
            if (wait.ClientId == clientId)
                return true;
        }

        return false;
    }

    private void Settle(int index, CommandResult result)
    {
        PendingWait wait = _waitsList[index];
        _waitsList.RemoveAt(index);

        wait.Completion.TrySetResult(result);
    }

    private void SettleAll(CommandResult result)
    {
        for (int i = _waitsList.Count - 1; i >= 0; i--)
            Settle(i, result);
    }

    private void SettleWaitsFor(string clientId, CommandResult result)
    {
        for (int i = _waitsList.Count - 1; i >= 0; i--)
        {
            if (_waitsList[i].ClientId == clientId)
                Settle(i, result);
        }
    }

    private QueuedAgent? FindQueued(string clientId)
    {
        foreach (QueuedAgent queued in _queueList)
        {
            if (queued.ClientId == clientId)
                return queued;
        }

        return null;
    }

    private bool RemoveFromLine(string clientId)
    {
        for (var i = 0; i < _queueList.Count; i++)
        {
            if (_queueList[i].ClientId != clientId)
                continue;

            _queueList.RemoveAt(i);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Where this caller stands, counting from one; zero when it is not in the line.
    /// One-based because it is a place in a queue as a person would say it, and the
    /// status reports it as such.
    /// </summary>
    private int GetQueuePosition(string clientId)
    {
        for (var i = 0; i < _queueList.Count; i++)
        {
            if (_queueList[i].ClientId == clientId)
                return i + 1;
        }

        return 0;
    }

    /// <summary>
    /// What the first agent in line calls itself; empty when the line is empty, and
    /// empty too when it never introduced itself — which is why the count, not this,
    /// is what says whether anybody is waiting.
    /// </summary>
    private string NextInLine => _queueList.Count > 0 ? _queueList[0].Name : string.Empty;

    private Dictionary<string, object?> BuildReport(string clientId) =>
        ControlStatusReport.Build(
            _arbiter,
            clientId,
            _elapsed,
            new ControlQueueSnapshot(_queueList.Count, NextInLine, GetQueuePosition(clientId)));

    /// <summary>Who has the editor and for how long, for a refusal that has to say so.</summary>
    private string Describe()
    {
        string name = _arbiter.AgentName.Length > 0 ? $"'{_arbiter.AgentName}'" : "Another agent";
        string topic = _arbiter.Topic.Length > 0 ? $" ({_arbiter.Topic})" : string.Empty;

        return _arbiter.Status == ControlStatus.Granted
            ? $"{name}{topic} holds the editor, and has for {(int)_elapsed}s."
            : $"{name}{topic} has already asked for the editor and is being granted it.";
    }

    private void OnControlChanged()
    {
        // The edge first: it is what advances the session counter, and the stamp
        // applied below has to carry the number this grant will be filed under.
        UpdateAgentControlEdge();
        ApplyActor();

        bool transitioned = _arbiter.Status != _lastStatus;
        ControlStatus previous = _lastStatus;
        _lastStatus = _arbiter.Status;

        if (transitioned)
        {
            switch (_arbiter.Status)
            {
                case ControlStatus.Pending:
                    _remaining = GetCountdownSeconds();
                    _pending = 0.0;
                    break;

                case ControlStatus.Granted:
                    _elapsed = 0.0;
                    _silence = 0.0;
                    break;

                case ControlStatus.Denied:
                    OnUserRefused(previous);
                    break;
            }
        }

        SettleDecidedWaits();
        Refresh();
    }

    /// <summary>
    /// The user cancelled a countdown or stopped an agent. The whole line is let go
    /// rather than promoted: the button that says "not now" would mean nothing if the
    /// next agent in line took the editor a frame later, and it means it for
    /// everybody — the user refused agents, not one agent's turn.
    /// </summary>
    private void OnUserRefused(ControlStatus previous)
    {
        if (_queueList.Count == 0)
            return;

        var message = previous == ControlStatus.Granted
            ? "The user took the editor back from the agent that had it, so the whole line was "
              + "let go rather than served straight after. Ask again if you still need it."
            : "The user refused the request ahead of yours, and the line behind it. Ask again "
              + "if you still need the editor.";

        // Copied out first: settling a wait can reach back here, and the refused
        // agent's own blocked call is answered by SettleDecidedWaits from its verdict.
        var refusedList = new List<string>(_queueList.Count);

        foreach (QueuedAgent queued in _queueList)
            refusedList.Add(queued.ClientId);

        _queueList.Clear();

        foreach (string clientId in refusedList)
            SettleWaitsFor(clientId, CommandResult.Failure(message));
    }

    /// <summary>
    /// A command arrived, so that agent is alive. The holder's silence clock starts
    /// over; a waiting agent's does too, which is what keeps a reservation alive
    /// across the calls that renew it. A poll from anybody else costs nothing.
    /// </summary>
    private void OnAgentCommand(string clientId)
    {
        if (_arbiter.IsHeldBy(clientId))
        {
            _silence = 0.0;
            Refresh();

            return;
        }

        QueuedAgent? queued = FindQueued(clientId);

        if (queued != null)
            queued.Silence = 0.0;
    }

    /// <summary>
    /// Acts on the one edge that matters for the viewport: an agent taking the
    /// editor, and giving it back by any path. On the way in the user's camera is
    /// remembered and the focus marker hidden, so the agent's clean frame carries
    /// none of the user's furniture; on the way out the marker returns and — unless
    /// the agent asked to keep its view — the camera does too. Every exit —
    /// release, stop, time-out, abandon — leaves the arbiter ungranted, so all of
    /// them pass through here.
    /// </summary>
    private void UpdateAgentControlEdge()
    {
        bool agentHasControl = _arbiter.Status == ControlStatus.Granted;

        if (agentHasControl == _agentHadControl)
            return;

        _agentHadControl = agentHasControl;
        _sceneView.SetAgentControlled(agentHasControl);

        if (agentHasControl)
        {
            // Remembered here, on the grant, so every path back — however it comes —
            // has the same view to restore. Restore is the default until the agent
            // says otherwise, whatever the last handover left it as.
            _savedPose = _sceneView.GetCameraPose();
            _restoreCamera = true;

            // A new session begins, and everything filed until control returns
            // belongs to it. Counted on the grant rather than on the request, so a
            // countdown the user cancels consumes no number.
            _session++;
        }
        else if (_restoreCamera)
        {
            _sceneView.SetCameraPose(_savedPose);
        }
    }

    private void ApplyActor() =>
        _history.CurrentActor = _arbiter.Status == ControlStatus.Granted
            ? new HistoryActor(ActorKind.Agent, _arbiter.AgentName) { Session = _session }
            : default;

    /// <summary>
    /// How long the user gets, read at the moment a countdown starts rather than
    /// held in a field — the setting can change between one handover and the next.
    /// Turning it off means no grace period, not no waiting: a busy user still
    /// holds the handover, which is what the token is for.
    /// </summary>
    private double GetCountdownSeconds() =>
        _preferences.CountdownEnabled ? _preferences.CountdownSeconds : 0.0;

    private bool HasWaitedLongEnough() => _pending >= _preferences.MaximumWaitSeconds;

    private void OnCancel() => _arbiter.Deny();

    private void OnStop() => _arbiter.Stop();

    private void OnInteractionBegan()
    {
        _arbiter.BeginInteraction();

        // Repaint at once: a request that was counting down should say it is
        // waiting the moment the user starts typing.
        Refresh();
    }

    private void OnInteractionEnded()
    {
        _arbiter.EndInteraction();

        // The countdown restarts rather than resuming — the user just stopped, and
        // deserves the full grace period before losing the editor. Once the maximum
        // wait has fallen through, though, interacting no longer buys any: the
        // countdown is running regardless and must be allowed to finish.
        if (_arbiter.Status == ControlStatus.Pending && !HasWaitedLongEnough())
            _remaining = GetCountdownSeconds();

        Refresh();
    }

    /// <summary>
    /// Paints the banner, but only when what it would show has changed. The clock
    /// ticks in whole seconds while this runs every frame, so most calls find the
    /// same state and return without touching the view or formatting a string.
    /// </summary>
    private void Refresh()
    {
        ControlBannerState state = BuildState();

        if (state == _painted)
            return;

        _painted = state;
        _view.SetControlBanner(state);
    }

    private ControlBannerState BuildState()
    {
        switch (_arbiter.Status)
        {
            case ControlStatus.Pending:
                return BuildPendingState();

            case ControlStatus.Granted:
                return BuildGrantedState();

            default:
                return new ControlBannerState(
                    ControlBannerKind.None,
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty,
                    0,
                    string.Empty);
        }
    }

    private ControlBannerState BuildPendingState()
    {
        // Waiting while the user is busy and the maximum wait has not fallen
        // through; counting down otherwise — including once it has, so the user
        // still sees a countdown rather than control snapping across.
        bool countingDown = !_arbiter.IsUserBusy || HasWaitedLongEnough();

        return countingDown
            ? new ControlBannerState(
                ControlBannerKind.AgentCountdown,
                _arbiter.AgentName,
                _arbiter.Topic,
                SecondsFrom(_remaining),
                string.Empty,
                _queueList.Count,
                NextInLine)
            : new ControlBannerState(
                ControlBannerKind.AgentWaiting,
                _arbiter.AgentName,
                _arbiter.Topic,
                0,
                string.Empty,
                _queueList.Count,
                NextInLine);
    }

    private ControlBannerState BuildGrantedState()
    {
        string elapsed = ElapsedTimeHelper.Format(_elapsed);

        if (_silence < _preferences.SilenceWarningSeconds)
            return new ControlBannerState(
                ControlBannerKind.AgentWorking,
                _arbiter.AgentName,
                _arbiter.Topic,
                0,
                elapsed,
                _queueList.Count,
                NextInLine);

        double returnsIn =
            _preferences.SilenceWarningSeconds + _preferences.SilenceGraceSeconds - _silence;

        return new ControlBannerState(
            ControlBannerKind.AgentSilent,
            _arbiter.AgentName,
            _arbiter.Topic,
            SecondsFrom(returnsIn),
            elapsed,
            _queueList.Count,
            NextInLine);
    }

    private static int SecondsFrom(double remaining) => (int)Math.Ceiling(Math.Max(0.0, remaining));

    /// <summary>One agent standing in line, and how long since it last said anything.</summary>
    private sealed class QueuedAgent
    {
        public string ClientId { get; init; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Topic { get; set; } = string.Empty;

        public double Silence { get; set; }
    }

    /// <summary>One <c>take-control</c> call left blocked until its request is settled.</summary>
    private sealed class PendingWait
    {
        public string ClientId { get; init; } = string.Empty;

        public double Remaining { get; set; }

        public TaskCompletionSource<CommandResult> Completion { get; init; } = new();
    }
}
