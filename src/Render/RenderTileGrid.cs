using System;

namespace VoxelCAD.Render;

/// <summary>
/// The image cut into rectangles the tracer can be aimed at one at a time.
/// </summary>
/// <remarks>
/// A render used to be one dispatch over the whole picture, which is the right
/// shape when every pixel is worth the same. It is not: somebody looking at a
/// corner of a large render is waiting for that corner, and the rest of the
/// picture is converging at their expense.
/// <para>
/// The grid is what makes "spend the next sample here" sayable. It decides
/// nothing about where — that is <see cref="TilePriority"/> and
/// <see cref="TileScheduler"/> — and holds no samples of its own.
/// </para>
/// </remarks>
internal sealed class RenderTileGrid
{
    /// <summary>
    /// The side of a tile in pixels. A multiple of the shader's own workgroup, so
    /// a full tile dispatches without a partial group, and small enough that the
    /// smallest region worth aiming at is a fraction of the picture rather than a
    /// quarter of it.
    /// </summary>
    public const int TileSize = 64;

    private readonly int _width;
    private readonly int _height;

    public RenderTileGrid(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "An image has a positive size.");

        _width = width;
        _height = height;

        Columns = (width + TileSize - 1) / TileSize;
        Rows = (height + TileSize - 1) / TileSize;
    }

    public int Columns { get; }

    public int Rows { get; }

    public int Count => Columns * Rows;

    /// <summary>
    /// Where a tile stands and how big it is. The last column and row are cut off
    /// by the image rather than hanging past it, so the tiles tile it exactly.
    /// </summary>
    public RenderTile this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            int column = index % Columns;
            int row = index / Columns;

            int x = column * TileSize;
            int y = row * TileSize;

            return new RenderTile(
                x,
                y,
                Math.Min(TileSize, _width - x),
                Math.Min(TileSize, _height - y));
        }
    }

    /// <summary>Which tile a pixel of the image belongs to.</summary>
    public int TileAt(int x, int y) =>
        Math.Clamp(y / TileSize, 0, Rows - 1) * Columns
            + Math.Clamp(x / TileSize, 0, Columns - 1);

    /// <summary>
    /// How much of a tile lies inside a rectangle of the image, from nought to
    /// one. The fraction rather than a yes or no, so a tile the viewer is showing
    /// half of is worth half the attention of one it is showing whole — which is
    /// what keeps the boundary of a priority region from being a visible line in
    /// the noise.
    /// </summary>
    public float Overlap(int index, RenderTile region)
    {
        RenderTile tile = this[index];

        int left = Math.Max(tile.X, region.X);
        int top = Math.Max(tile.Y, region.Y);
        int right = Math.Min(tile.X + tile.Width, region.X + region.Width);
        int bottom = Math.Min(tile.Y + tile.Height, region.Y + region.Height);

        if (right <= left || bottom <= top)
            return 0f;

        return (float)((right - left) * (bottom - top)) / (tile.Width * tile.Height);
    }
}
