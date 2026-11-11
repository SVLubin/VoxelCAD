using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Where the marker for one cell of a voxel model goes.
/// </summary>
/// <remarks>
/// One line of arithmetic, out here rather than in the node, because it is the
/// line that was wrong: the cell offset has to be applied in the element's own
/// frame. <c>Transform3D.Translated</c> applies it in the parent's, so the marker
/// kept the element's rotation and scale but stood where an unrotated element
/// would have put that cell — visibly beside the model as soon as anything was
/// turned or scaled, while the voxel a click placed was in the right place all
/// along.
/// </remarks>
internal static class BrushPreview
{
    /// <summary>
    /// The unit cube covering <paramref name="cell"/>, placed by the element's
    /// world transform — so it is rotated, scaled and positioned exactly as the
    /// voxel a click would leave there.
    /// </summary>
    public static Transform3D GetCellTransform(Transform3D world, Vector3I cell)
    {
        Vector3 centre = new(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

        return world.TranslatedLocal(centre);
    }
}
