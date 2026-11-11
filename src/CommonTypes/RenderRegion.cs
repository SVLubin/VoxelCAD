namespace VoxelCAD.CommonTypes;

/// <summary>
/// A rectangle of a render, in its own pixels — what part of it somebody is
/// looking at, and so what part of it is worth tracing first.
/// </summary>
/// <remarks>
/// A type of its own rather than four numbers because it crosses the view seam
/// and means one particular thing there: empty is not a degenerate rectangle, it
/// is "all of it, equally", which is what a picture nobody has zoomed into asks
/// for.
/// </remarks>
public readonly record struct RenderRegion(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
