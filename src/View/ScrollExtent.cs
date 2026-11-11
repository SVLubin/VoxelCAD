namespace VoxelCAD.View;

/// <summary>
/// Where a scrolling body stands: how far it has gone, and how far it could.
/// </summary>
/// <remarks>
/// The pair is one value because the two are only ever meaningful together —
/// an offset says nothing about whether anything is cut, and a maximum says
/// nothing about which end is. It is also the shape that stops an <em>estimate</em>
/// standing in for the second number: a tree's content height measured with its
/// chrome added on overstates the maximum by a dozen units, which is a body
/// dissolving at a bottom edge that has nothing past it.
/// </remarks>
internal readonly record struct ScrollExtent(float Offset, float Maximum)
{
    /// <summary>Nothing to scroll, so nothing is cut at either end.</summary>
    public static readonly ScrollExtent None = new(0f, 0f);
}
