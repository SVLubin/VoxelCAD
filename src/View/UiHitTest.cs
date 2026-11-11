using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Which control a point actually lands on, read off a snapshot rather than off
/// the engine. It is what turns "aim at this button" into "…and refuse if
/// something is drawn over it".
/// </summary>
/// <remarks>
/// An approximation of Godot's own hit test, and knowingly so: it reads the same
/// three things the engine does — visible, stops the mouse, contains the point —
/// in the same draw order, but it does not model a container clipping its
/// contents. What it is for is catching the case that actually happens, which is
/// a full-rect overlay (the handover dimmer, a modal's shade) sitting over the
/// control an agent meant to press. A click that reported success and did nothing
/// is the failure worth spending this on.
/// </remarks>
internal static class UiHitTest
{
    /// <summary>
    /// The topmost control at a point, or -1. Deeper windows win outright, and
    /// within one window the later entry wins — that is tree order, which is draw
    /// order.
    /// </summary>
    /// <remarks>
    /// The window layer has to be asked first, and tree order alone is wrong
    /// without it: a menu button's popup is a child of a button near the top of a
    /// dock, and it is drawn over every panel that comes after that button in the
    /// tree. Ranking by position alone reported the panel underneath as covering
    /// the popup, and refused a click that would have landed perfectly well.
    /// </remarks>
    public static int FindTopmost(IReadOnlyList<UiControl> controlsList, Vector2 point)
    {
        var best = -1;

        for (var index = 0; index < controlsList.Count; index++)
        {
            UiControl control = controlsList[index];

            if (!control.Visible || !control.TakesMouse || !control.Rect.HasPoint(point))
                continue;

            if (best < 0 || control.Layer >= controlsList[best].Layer)
                best = index;
        }

        return best;
    }

    /// <summary>
    /// Whether a hit on <paramref name="hitReference"/> counts as reaching
    /// <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// A descendant counts because a click on a button's own label is a click on
    /// the button. An ancestor counts because a control that ignores the mouse —
    /// a label, a layout box — never wins the hit test itself, and refusing to aim
    /// at one because its container answered instead would be refusing the common
    /// case. Anything else is something in the way, and is named.
    /// </remarks>
    public static bool Reaches(string hitReference, string target) =>
        UiReference.IsUnder(hitReference, target) || UiReference.IsUnder(target, hitReference);
}
