namespace VoxelCAD.CommonTypes;

/// <summary>What the control banner shows, if anything.</summary>
public enum ControlBannerKind
{
    /// <summary>The user is in control; no banner.</summary>
    None,

    /// <summary>An agent is asking, but the user is mid-interaction; wait quietly.</summary>
    AgentWaiting,

    /// <summary>The countdown before an agent takes control, with a chance to cancel.</summary>
    AgentCountdown,

    /// <summary>An agent holds control; the interface is suspended.</summary>
    AgentWorking,

    /// <summary>
    /// An agent still holds control but has sent nothing for long enough to look
    /// gone. The interface stays suspended and the banner says so, with the wait
    /// before control returns on its own — control is never taken back without
    /// having been seen to be coming.
    /// </summary>
    AgentSilent
}
