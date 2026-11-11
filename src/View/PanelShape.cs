using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// A chrome surface's whole shape: the rectangle, and the notch bitten out of it
/// below the stub.
/// </summary>
/// <remarks>
/// The two are one value because they are one answer — a 358-wide rect with no
/// notch is a body 36 units too wide, and a 322-wide one with a notch is a notch
/// eating the body. Answered separately, the panel drew a Γ where the outline
/// promising its place drew a rectangle.
/// </remarks>
internal readonly record struct PanelShape(Rect2 Rect, float Cut, bool StubOnRight)
{
    /// <summary>
    /// The stub as the shader takes it: how wide the cut is, and how far down it
    /// reaches. The depth is the collapsed square — a tab is the panel shrunk to
    /// its glyph, so the notch clears exactly one card — and it is stated here
    /// and nowhere else.
    /// </summary>
    public Vector2 Stub =>
        Cut > 0f ? new Vector2(Cut, ChromePanel.CollapsedSide) : Vector2.Zero;

    /// <summary>Which side a column's stub stands on: the one derivation of it.</summary>
    public static PanelShape For(Rect2 rect, float cut, PanelSide side) =>
        new(rect, cut, side == PanelSide.Right);
}
