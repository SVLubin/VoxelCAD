using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The other half of the coordinate contract: between the canvas units a
/// <see cref="Control"/>'s rect is measured in and the window pixels input is
/// aimed in. (<see cref="FrameCoordinates"/> covers the image side.)
/// </summary>
/// <remarks>
/// These are not the same space, and the difference is not small — the window's
/// own stretch transform was measured at 1.002 in one run of this project and at
/// 2.58 in another, on the same machine, differing only in how big the window
/// was. So the transform is read live from the window every time a snapshot is
/// taken; a cached one is a click in the wrong place after the first resize.
/// </remarks>
internal static class UiCoordinates
{
    /// <summary>
    /// A control's rect put into input coordinates. The transform is the window's
    /// own stretch, which is a scale and an offset and never a rotation — so the
    /// size goes through the basis and the corner through the whole thing.
    /// </summary>
    public static Rect2 ToInput(Rect2 canvasRect, Transform2D canvasToInput) =>
        new(canvasToInput * canvasRect.Position, canvasToInput.BasisXform(canvasRect.Size));

    public static Vector2 ToInput(Vector2 canvasPoint, Transform2D canvasToInput) =>
        canvasToInput * canvasPoint;

    /// <summary>
    /// The point inside a rect a click should be aimed at: its centre, or an
    /// offset within it where the caller wants somewhere else.
    /// </summary>
    /// <remarks>
    /// The offset runs 0..1 across the rect, so it means the same thing whatever
    /// the control's size, and it is clamped rather than refused — a caller asking
    /// for 1.5 wants the far edge, not an exception.
    /// </remarks>
    public static Vector2 GetPointInside(Rect2 rect, Vector2 offset) =>
        rect.Position + new Vector2(
            rect.Size.X * Mathf.Clamp(offset.X, 0f, 1f),
            rect.Size.Y * Mathf.Clamp(offset.Y, 0f, 1f));

    /// <summary>The centre, which is where an unqualified click at a control goes.</summary>
    public static Vector2 GetCentre(Rect2 rect) => GetPointInside(rect, new Vector2(0.5f, 0.5f));
}
