namespace VoxelCAD.CommonTypes;

/// <summary>
/// Everything the control banner draws, in one piece. The view renders it and
/// decides nothing: which phase it is in, what the clock says and how long is
/// left are all worked out before they get here.
/// </summary>
/// <remarks>
/// It is a value rather than a list of parameters because the presenter compares
/// one against the last to decide whether to repaint at all — the banner is
/// refreshed every frame and its clock changes once a second.
/// </remarks>
/// <param name="SecondsLeft">
/// Seconds until the current phase ends, in whichever direction it is heading:
/// until an agent takes control during the countdown, until control comes back
/// while an agent is silent. Zero when nothing is counting.
/// </param>
/// <param name="Topic">
/// What the agent says it is doing, in its own words — given when it asks for
/// control and updatable while it works. Empty when it never said; the view
/// falls back to a quiet phase phrase of its own.
/// </param>
/// <param name="Elapsed">
/// How long the agent has held control, already formatted — the choice between
/// seconds and minutes is wording, and wording lives in the presenter. Empty
/// while no agent holds it.
/// </param>
/// <param name="QueuedAgents">
/// How many agents are waiting their turn behind this one. The user is told because
/// otherwise the editor being taken again the moment it comes back reads as the same
/// agent refusing to let go. A count rather than a flag beside
/// <paramref name="WaitingAgent"/>, because that name may be empty and the waiting
/// still real: an agent need not introduce itself.
/// </param>
/// <param name="WaitingAgent">What the first of them calls itself; empty when it never said.</param>
public readonly record struct ControlBannerState(
    ControlBannerKind Kind,
    string AgentName,
    string Topic,
    int SecondsLeft,
    string Elapsed,
    int QueuedAgents,
    string WaitingAgent);
