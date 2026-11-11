using System;

namespace VoxelCAD.Presenter;

/// <summary>
/// Arbitrates control of the editor between the user and one agent. Pure state
/// and transitions — the decisions that need Godot or preferences (is the window
/// focused, is there a countdown, how long) belong to the presenter that drives
/// this.
/// </summary>
/// <remarks>
/// "Busy" is wider than a history batch: typing in a field, dragging a tree item
/// or an open dialog all hold an interaction token, and a handover waits while
/// any is held. A leaked token only delays the handover, never corrupts anything,
/// and the presenter's maximum-wait falls through regardless.
/// <para>
/// <b>Exclusivity is per agent, not merely per side.</b> Every transition an agent
/// can ask for names the client asking for it, and one that names anybody but the
/// holder is refused — otherwise a second agent's <c>take-control</c> read the
/// first one's grant as its own, its edits passed the gate, and its
/// <c>release-control</c> ended a session it never started. Who a client is comes
/// from the transport (<see cref="ControlGatedDispatcher"/>); this class only
/// insists that it is the same one throughout.
/// </para>
/// </remarks>
internal sealed class ControlArbiter
{
    private int _busyCount;

    /// <summary>
    /// The client the last verdict was aimed at, kept after the agent itself is
    /// cleared. A refusal is news for the agent that was refused and nobody else:
    /// without this, an unrelated agent reading the status would meet a "Denied"
    /// meant for someone else and take it for its own.
    /// </summary>
    private string _verdictId = string.Empty;

    public event Action? Changed;

    /// <summary>
    /// An agent sent a command — proof it is still there. Read or write, held
    /// control or not: every command counts, which is what lets a long build or a
    /// poll of <c>control-status</c> keep an agent from being cut off. The presenter
    /// listens and resets the silence it measures — for the holder and for a waiting
    /// agent alike, which is why the client id rides along.
    /// </summary>
    public event Action<string>? AgentCommandReceived;

    public ControlStatus Status { get; private set; } = ControlStatus.Available;

    /// <summary>
    /// The transport client holding or requesting control; empty when none. This is
    /// identity, where <see cref="AgentName"/> is only what the agent calls itself:
    /// two agents may share a name, and the plaque is welcome to show it, but the
    /// editor may never confuse them.
    /// </summary>
    public string AgentId { get; private set; } = string.Empty;

    /// <summary>The agent currently holding or requesting control; empty when none.</summary>
    public string AgentName { get; private set; } = string.Empty;

    /// <summary>
    /// What that agent says it is doing, in its own words. Announced with the
    /// request and updatable while the agent is around; cleared whenever the
    /// agent is, because a topic with nobody behind it is a stale claim.
    /// </summary>
    public string Topic { get; private set; } = string.Empty;

    public bool IsUserBusy => _busyCount > 0;

    /// <summary>
    /// The user's interface stays live right up until control is actually
    /// granted: while a request is only pending, they are still driving.
    /// </summary>
    public bool IsUserInControl => Status != ControlStatus.Granted;

    /// <summary>
    /// Whether an agent is in the negotiation at all — holding the editor, or
    /// waiting out a countdown that will hand it over.
    /// </summary>
    public bool HasAgent => Status is ControlStatus.Pending or ControlStatus.Granted;

    public bool IsHeldBy(string clientId) =>
        Status == ControlStatus.Granted && IsSameClient(clientId);

    /// <summary>
    /// Whether some other agent has the editor or the pending request for it — the
    /// one question a newcomer has to be answered honestly, and the gate's rule for
    /// every mutating command.
    /// </summary>
    public bool IsClaimedByAnother(string clientId) => HasAgent && !IsSameClient(clientId);

    /// <summary>Whether the last verdict was aimed at this client rather than another.</summary>
    public bool IsVerdictFor(string clientId) => string.Equals(_verdictId, clientId, StringComparison.Ordinal);

    /// <summary>
    /// An agent asks for control. Moves to <see cref="ControlStatus.Pending"/>;
    /// the presenter decides whether to grant at once, count down, or wait for the
    /// user to stop interacting.
    /// </summary>
    /// <returns>
    /// False when another agent already holds the editor or is pending for it, and
    /// nothing changed. The caller reports that as a refusal rather than a status:
    /// an agent that cannot tell "granted to me" from "granted to somebody else"
    /// edits a document under another agent's hands.
    /// </returns>
    public bool Request(string clientId, string agentName, string topic)
    {
        if (IsClaimedByAnother(clientId))
            return false;

        // Asking again is not asking anew: an agent that already holds the editor,
        // or is already counting down for it, keeps its place and only restates who
        // it is and what it is doing.
        if (HasAgent)
        {
            SetIdentity(agentName, topic);

            return true;
        }

        AgentId = clientId;
        AgentName = agentName;
        Topic = topic;

        SetStatus(ControlStatus.Pending);

        return true;
    }

    /// <summary>
    /// The agent restates what it is working on — a long session moves through
    /// phases, and the plaque should follow. Only from the agent actually here:
    /// a topic from anyone else names nobody, and would let a bystander write on
    /// the plaque the user is reading.
    /// </summary>
    public bool SetTopic(string clientId, string topic)
    {
        if (!HasAgent || !IsSameClient(clientId))
            return false;

        if (Topic == topic)
            return true;

        Topic = topic;
        Changed?.Invoke();

        return true;
    }

    /// <summary>Hand control to the waiting agent. Only valid while a request is pending.</summary>
    public void Grant()
    {
        if (Status != ControlStatus.Pending)
            return;

        SetStatus(ControlStatus.Granted);
    }

    /// <summary>The user refused the pending request by cancelling the countdown.</summary>
    public void Deny()
    {
        if (Status != ControlStatus.Pending)
            return;

        LeaveVerdict(ControlStatus.Denied);
    }

    /// <summary>
    /// The agent gave control back on its own. Refused for anyone but the holder:
    /// releasing a session somebody else opened is the sharpest form of the
    /// confusion this class exists to prevent.
    /// </summary>
    public bool Release(string clientId)
    {
        if (!IsHeldBy(clientId))
            return false;

        AgentId = string.Empty;
        AgentName = string.Empty;
        Topic = string.Empty;

        SetStatus(ControlStatus.Available);

        return true;
    }

    /// <summary>The user took control back while the agent held it.</summary>
    public void Stop()
    {
        if (Status != ControlStatus.Granted)
            return;

        LeaveVerdict(ControlStatus.Denied);
    }

    /// <summary>
    /// The agent held control but fell silent for too long, so the editor took it
    /// back. <see cref="ControlStatus.TimedOut"/> rather than
    /// <see cref="ControlStatus.Denied"/> so the agent can tell its own silence
    /// from the user's refusal.
    /// </summary>
    public void TimeOut()
    {
        if (Status != ControlStatus.Granted)
            return;

        LeaveVerdict(ControlStatus.TimedOut);
    }

    /// <summary>
    /// The transport is gone — the endpoint stopped, or the editor is closing — so
    /// whatever handover was in flight is dropped and the user has the editor back.
    /// A crashed agent is not around to read a status, so this returns to the clean
    /// <see cref="ControlStatus.Available"/> rather than leaving a verdict behind.
    /// </summary>
    public void Abandon()
    {
        if (Status == ControlStatus.Available)
            return;

        AgentId = string.Empty;
        AgentName = string.Empty;
        Topic = string.Empty;
        _verdictId = string.Empty;

        SetStatus(ControlStatus.Available);
    }

    /// <summary>Records an agent command, so the presenter's silence clock resets.</summary>
    public void NoteAgentCommand(string clientId) => AgentCommandReceived?.Invoke(clientId);

    public void BeginInteraction() => _busyCount++;

    public void EndInteraction()
    {
        if (_busyCount > 0)
            _busyCount--;
    }

    private bool IsSameClient(string clientId) =>
        string.Equals(AgentId, clientId, StringComparison.Ordinal);

    /// <summary>
    /// Ends the session with a verdict the agent can read once it asks. The agent
    /// is cleared like any other exit; only which client the verdict belongs to is
    /// kept, so it is delivered to that agent and to nobody else.
    /// </summary>
    private void LeaveVerdict(ControlStatus status)
    {
        _verdictId = AgentId;

        AgentId = string.Empty;
        AgentName = string.Empty;
        Topic = string.Empty;

        SetStatus(status);
    }

    private void SetIdentity(string agentName, string topic)
    {
        if (AgentName == agentName && Topic == topic)
            return;

        AgentName = agentName;
        Topic = topic;

        Changed?.Invoke();
    }

    private void SetStatus(ControlStatus status)
    {
        if (Status == status)
            return;

        Status = status;
        Changed?.Invoke();
    }
}
