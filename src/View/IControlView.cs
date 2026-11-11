using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The banner surface of the shell, narrowed for the control presenter so it sees
/// the handover overlay and nothing else of the chrome.
/// </summary>
public interface IControlView
{
    event Action CancelControlRequested;

    event Action StopControlRequested;

    /// <summary>
    /// The user started something a handover should not interrupt — typing in a
    /// field, dragging, an open dialog. Raised and cleared in pairs; the arbiter
    /// counts them, so overlapping interactions are fine.
    /// </summary>
    event Action UserInteractionBegan;

    event Action UserInteractionEnded;

    /// <summary>
    /// Paints the handover overlay from a value that already holds every decision —
    /// which phase, the running clock, the seconds left. The view renders it and
    /// works nothing out; the presenter throttles the calls to when it changes.
    /// </summary>
    void SetControlBanner(ControlBannerState state);
}
