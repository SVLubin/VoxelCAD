using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What a drop does to the stored placements. Pure, and the whole of the
/// bookkeeping a drag needs: which side each panel is on and what order it
/// stands in, renumbered so nothing keeps a stale number.
/// </summary>
internal static class PanelPlacementEdit
{
    /// <summary>
    /// Moves one panel to a side, into the pinned stack or the run of tabs, and
    /// hands back the whole list renumbered.
    /// </summary>
    /// <remarks>
    /// Among the tabs, not among everything, because that is what a drag can
    /// see: a column stacks its pinned panels first and runs the tabs below
    /// them, so an index picked out of the run has to be counted from the end
    /// of the pinned block. Renumbering both sides afterwards is what keeps
    /// `Order` dense — a list that only ever has values inserted into it drifts
    /// into ties, and a tie is decided by whatever order the panels happen to
    /// arrive in.
    /// </remarks>
    public static IReadOnlyList<PanelPlacement> Move(
        IReadOnlyList<PanelPlacement> placements,
        PanelKind kind,
        PanelSide side,
        bool pinned,
        int index)
    {
        PanelPlacement moved = placements.First(placement => placement.Kind == kind);

        var others = placements
            .Where(placement => placement.Kind != kind)
            .ToList();

        // The side it is landing on, in the order the column will draw it:
        // pinned first, then the run, and the carried tab inserted into the run
        // at the place the pointer chose.
        var pinnedBlock = others
            .Where(placement => placement.Side == side && placement.Pinned)
            .OrderBy(placement => placement.Order)
            .ToList();

        var tabs = others
            .Where(placement => placement.Side == side && !placement.Pinned)
            .OrderBy(placement => placement.Order)
            .ToList();

        // Into the block it was dropped in, and wearing that block's answer: a
        // panel put between the pinned ones is pinned, one put in the run is a
        // tab. What it *was* does not decide, because the preview has been
        // drawing it as what it will be for the whole gesture — and a preview
        // that showed one thing and stored another would be a lie.
        var block = pinned ? pinnedBlock : tabs;

        block.Insert(
            Mathf.Clamp(index, 0, block.Count),
            moved with { Side = side, Pinned = pinned });

        var landed = pinnedBlock.Concat(tabs).ToList();

        var elsewhere = others
            .Where(placement => placement.Side != side)
            .OrderBy(placement => placement.Pinned ? 0 : 1)
            .ThenBy(placement => placement.Order)
            .ToList();

        return Renumber(landed)
            .Concat(Renumber(elsewhere))
            .OrderBy(placement => placement.Kind)
            .ToList();
    }

    /// <summary>
    /// Flips one panel's pin, landing it at the <b>boundary</b> between the two
    /// blocks: the head of the run when it is unpinned, the end of the pinned
    /// stack when it is pinned.
    /// </summary>
    /// <remarks>
    /// That is the one place a panel can cross the boundary without moving —
    /// the run begins exactly where the pinned stack ends — and moving is the
    /// whole of what was wrong with unpinning. The flag was flipped in place
    /// before this, so a panel kept its <c>Order</c> and was re-sorted into the
    /// run by it: the one pinned panel in a column, standing at the top, landed
    /// second among the tabs and a third of the way down the column. Reported as
    /// the panel putting itself away, which is the same thing seen from the
    /// chair — it left the hand that had just pressed its pin, and a glance that
    /// is not under the hand has nothing to stay open for.
    /// <para>
    /// Only where the panel is not the last of its block can this still move it,
    /// and then only past whatever stood between it and the boundary.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PanelPlacement> Cross(
        IReadOnlyList<PanelPlacement> placements,
        PanelKind kind)
    {
        PanelPlacement moved = placements.First(placement => placement.Kind == kind);
        bool pinned = !moved.Pinned;

        // A fold is about a panel that is reserving room; unpinning ends that,
        // so it ends the fold with it. Written into the list rather than passed,
        // because Move carries the placement it finds.
        var unfolded = placements
            .Select(placement =>
                placement.Kind == kind ? placement with { Folded = false } : placement)
            .ToList();

        int index = pinned
            ? unfolded.Count(placement =>
                placement.Kind != kind && placement.Side == moved.Side && placement.Pinned)
            : 0;

        return Move(unfolded, kind, moved.Side, pinned, index);
    }

    private static IEnumerable<PanelPlacement> Renumber(IReadOnlyList<PanelPlacement> side) =>
        side.Select((placement, order) => placement with { Order = order });
}
