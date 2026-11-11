using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Turning "aim at this control" into a point, or into a refusal saying why not.
/// </summary>
/// <remarks>
/// It refuses rather than guesses, and that is the whole design. A click into
/// empty space that reports success is the worst outcome available here: the
/// command looks like it worked, the editor did nothing, and whoever is reading
/// the transcript starts looking for the bug somewhere else entirely. So every
/// way a control can fail to be somewhere to aim — gone, hidden, disabled,
/// sizeless, covered — is a named refusal.
/// <para>
/// Pure, over a snapshot somebody else took: the walk needs the node tree, this
/// needs only what the walk found.
/// </para>
/// </remarks>
internal static class InputTargeting
{
    public static InputTargetResolution Resolve(
        IReadOnlyList<UiControl> controlsList,
        string reference,
        Vector2 offset)
    {
        if (!TryFind(controlsList, reference, out UiControl target))
            return Refuse($"No control is called '{reference}'. Run 'ui' to see what there is.");

        if (!target.Visible)
            return Refuse($"'{reference}' is not on screen.", target);

        if (target.Disabled)
            return Refuse($"'{reference}' is disabled.", target);

        if (target.Rect.Size.X <= 0f || target.Rect.Size.Y <= 0f)
        {
            return Refuse(
                $"'{reference}' has no size to aim at. A control scrolled out of view inside "
                + "a clipping panel reads this way — scroll it into view first — and so does "
                + "a window the platform drew outside this one, which nothing here can reach.",
                target);
        }

        Vector2 point = UiCoordinates.GetPointInside(target.Rect, offset);
        int topmost = UiHitTest.FindTopmost(controlsList, point);

        if (topmost >= 0 && !UiHitTest.Reaches(controlsList[topmost].Reference, reference))
        {
            return Refuse(
                $"'{reference}' is covered at that point by '{controlsList[topmost].Reference}'.",
                target);
        }

        return new InputTargetResolution(true, string.Empty, point, target);
    }

    private static bool TryFind(
        IReadOnlyList<UiControl> controlsList,
        string reference,
        out UiControl found)
    {
        foreach (UiControl control in controlsList)
        {
            if (control.Reference != reference)
                continue;

            found = control;

            return true;
        }

        found = default;

        return false;
    }

    private static InputTargetResolution Refuse(string reason, UiControl control = default) =>
        new(false, reason, Vector2.Zero, control);
}
