using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Which of a column's panels is drawn in front of which.
/// </summary>
/// <remarks>
/// Godot takes draw order from child order, and child order was the one output
/// of the column's solve nobody ever wrote down: a panel is appended when it is
/// reparented and never moved again, so depth was a residue of which panel was
/// last taken in — and drifted from the layout the moment a panel was reordered
/// without changing columns. The canon says nothing in the chrome is ever
/// placed outright; this is that rule reaching the one axis it had missed.
/// <para>
/// The answer is the layout's own order, with the carried panel last: the thing
/// under the hand sweeps over its neighbours and must not pass behind them.
/// </para>
/// </remarks>
internal static class PanelDepthOrder
{
    /// <summary>
    /// For each slot, in layout order, its rank in draw order — 0 furthest
    /// back. The caller adds whatever it draws beneath the panels.
    /// </summary>
    public static IReadOnlyList<int> Solve(
        IReadOnlyList<PanelKind> ordered, PanelKind? carried)
    {
        var depths = new int[ordered.Count];
        var next = 0;

        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index] != carried)
                depths[index] = next++;
        }

        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index] == carried)
                depths[index] = next++;
        }

        return depths;
    }
}
