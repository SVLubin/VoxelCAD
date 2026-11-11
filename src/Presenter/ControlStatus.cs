namespace VoxelCAD.Presenter;

/// <summary>
/// Who holds the editor, from the agent's point of view. The user and an agent
/// never edit at once, so this is exclusive.
/// </summary>
public enum ControlStatus
{
    /// <summary>The user holds control; no agent is asking.</summary>
    Available,

    /// <summary>An agent has asked and the handover is being negotiated.</summary>
    Pending,

    /// <summary>An agent holds control; the user's interface is suspended.</summary>
    Granted,

    /// <summary>
    /// The last request was refused, by the user cancelling the countdown or
    /// taking control back. Distinct from <see cref="Available"/> so an agent can
    /// tell a refusal from a connection it never had.
    /// </summary>
    Denied,

    /// <summary>
    /// Control returned to the user because the agent went silent — no command of
    /// any kind for long enough that the editor stopped believing in it.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Denied"/> for the same reason that is distinct
    /// from <see cref="Available"/>: both end with the agent holding nothing, and
    /// they mean opposite things. A refusal is a decision the user made and the
    /// agent should not argue with; this one says the agent's own silence lost it
    /// the editor, and asking again is the right response.
    /// </remarks>
    TimedOut
}
