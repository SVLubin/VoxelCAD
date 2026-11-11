using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>Where a carried panel would land if it were let go now.</summary>
internal readonly record struct PanelDropTarget(PanelSide Side, bool Pinned, int Index);

/// <summary>
/// What one column offers a drop: the span its pinned stack occupies, and the
/// middle of everything already standing in each of its two blocks.
/// </summary>
/// <param name="PinnedTop">
/// Where the pinned stack begins, <b>counting the carried panel's own reserved
/// place</b> — that place is still held open and the ghost is still drawn in
/// it, so the span has to be the stack the hand can see.
/// </param>
/// <param name="PinnedBottom">Where it ends, on the same terms.</param>
/// <param name="PinnedCentres">
/// In order, and <b>without the panel being carried</b> — a block that counted
/// the carried panel against itself would answer differently depending on where
/// it had just put it, and chatter between two places.
/// </param>
internal readonly record struct PanelColumnDrop(
    float PinnedTop,
    float PinnedBottom,
    IReadOnlyList<float> PinnedCentres,
    IReadOnlyList<float> TabCentres)
{
    /// <summary>
    /// Whether this column has a pinned stack at all.
    /// </summary>
    /// <remarks>
    /// Asked of the span, never of <see cref="PinnedCentres"/>, which leaves
    /// the panel in hand out: a column whose one pinned panel is the one being
    /// carried has an empty list and a perfectly good stack, and reading the
    /// list meant that panel could not be dropped back into the very place it
    /// was lifted from.
    /// </remarks>
    public bool HasStack => PinnedBottom > PinnedTop;
}

/// <summary>
/// Turns a pointer into a place in a column. Pure, and separate from the column
/// that will act on it, because "which side, which block, and how far down" is
/// the whole of what a drag decides and it is the part worth holding to a test.
/// </summary>
internal static class PanelDropTargets
{
    public static PanelDropTarget Solve(
        float pointerX,
        float pointerY,
        float windowWidth,
        PanelColumnDrop left,
        PanelColumnDrop right)
    {
        // The half of the window the hand is in, and nothing cleverer. A panel
        // dragged across the middle changes sides; there is no drop zone to
        // find and nothing to hit.
        PanelSide side = pointerX < windowWidth * 0.5f
            ? PanelSide.Left
            : PanelSide.Right;

        PanelColumnDrop column = side == PanelSide.Left ? left : right;

        // Between the pinned panels means pinned. What a panel is follows where
        // it is put rather than what it was, which is the only reading under
        // which the preview can be honest: dropped into the stack it is drawn
        // in the stack, carried back out it is a tab again, and nothing is
        // decided until the hand lets go. An empty stack has no span, so a rail
        // can never pin by accident.
        bool pinned = column.HasStack
            && pointerY >= column.PinnedTop
            && pointerY <= column.PinnedBottom;

        return new PanelDropTarget(
            side,
            pinned,
            IndexFor(pointerY, pinned ? column.PinnedCentres : column.TabCentres));
    }

    /// <summary>
    /// How many of them the pointer has passed: the place it would be inserted.
    /// </summary>
    public static int IndexFor(float pointerY, IReadOnlyList<float> centres)
    {
        var index = 0;

        foreach (float centre in centres)
        {
            if (pointerY < centre)
                break;

            index++;
        }

        return index;
    }
}
