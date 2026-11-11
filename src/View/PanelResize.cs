using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What a resize drag's numbers mean, as pure functions: the grip in the
/// panel's corner reports a pull, and these decide what the pull is worth. Out
/// of the nodes for the reason every decision in this subsystem is — a rule
/// inside <see cref="EdgeColumn"/> is a rule no test can reach.
/// </summary>
internal static class PanelResize
{
    /// <summary>
    /// The body height a slot asks the cascade for: the height the user dragged
    /// it to, or — while they never have — whatever the content measures. The
    /// stored height is floored at the panel's own minimum rather than refused,
    /// the clamp-don't-throw bargain the preferences already make.
    /// </summary>
    /// <remarks>
    /// Deliberately not capped at the content: a fixed-height History that does
    /// not grow with every record is half of what dragging a height is
    /// <em>for</em>, and the body scrolls whatever does not fit.
    /// </remarks>
    public static float RequestedBody(double storedHeight, float natural, float min) =>
        storedHeight > 0.0 ? Math.Max(min, (float)storedHeight) : natural;

    /// <summary>
    /// A width the chrome may actually be, on <see cref="PanelWidths"/>'s own
    /// bounds — the same pair the preferences clamp a stored file to.
    /// </summary>
    public static float ClampWidth(double width) =>
        (float)Math.Clamp(width, PanelWidths.Minimum, PanelWidths.Maximum);

    /// <summary>
    /// A height the cascade would grant: no less than the panel's own minimum.
    /// The ceiling is deliberately absent — the column already shrinks what
    /// does not fit, and a second copy of that rule here would be the one that
    /// drifts.
    /// </summary>
    public static float ClampHeight(double height, float min) =>
        Math.Max(min, (float)height);

    /// <summary>
    /// How far a pull moves the inner edge, signed so that dragging toward the
    /// scene widens on either column: the left column's inner edge is its
    /// right, the right column's is its left.
    /// </summary>
    public static float TowardScene(PanelSide side, float dx) =>
        side == PanelSide.Left ? dx : -dx;
}
