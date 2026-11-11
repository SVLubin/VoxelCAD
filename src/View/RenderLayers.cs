namespace VoxelCAD.View;

/// <summary>
/// Render layer bits so box lighting can ignore voxel shadows.
/// </summary>
internal static class RenderLayers
{
    public const uint Voxels = 1 << 0;
    public const uint SceneBox = 1 << 1;
}
