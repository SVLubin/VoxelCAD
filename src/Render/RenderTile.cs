namespace VoxelCAD.Render;

/// <summary>A rectangle of the image, in pixels from its top left corner.</summary>
internal readonly record struct RenderTile(int X, int Y, int Width, int Height)
{
    public int Area => Width * Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;
}
