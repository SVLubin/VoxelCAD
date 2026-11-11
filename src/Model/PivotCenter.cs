using Godot;
using VoxelCAD.Geometry;

namespace VoxelCAD.Model;

/// <summary>
/// The one-shot "centre the pivot" calculation: the middle of what the element
/// holds, in its own cells. A calculation and not a rule — the answer is fed to
/// <see cref="IScene.SetPivot"/> once, and nothing recomputes it afterwards. That
/// is the whole design: a pivot that followed the cells could never be the point
/// the position channel names, because painting would then have to move either the
/// geometry or the number (see the architecture canon).
/// </summary>
/// <remarks>
/// Public because both the inspector's Centre button and the command surface's
/// <c>point=center</c> ask it, and the two must not disagree. For a voxel model
/// the answer lands on the half-cell lattice exactly; for a group it is the middle
/// of everything underneath, brought into the group's frame and rounded by
/// <c>SetPivot</c> like any other request.
/// </remarks>
public static class PivotCenter
{
    /// <summary>False when there is nothing to centre on, which changes nothing.</summary>
    public static bool TryCalculate(IHierarchyElement element, out Vector3 center)
    {
        if (element.Component is IVoxelModel model)
        {
            VoxelExtent extent = VoxelExtent.Measure(model.Voxels.Keys);

            center = extent.Center;

            return !extent.IsEmpty;
        }

        // A reference plane's content is the plane: a rectangle spanning from the
        // element's own zero, so its middle is half its size. Without this the grip
        // would sit at the picture's bottom corner and a quarter-turn would swing
        // the whole drawing away from where it was hung.
        if (element.Component is IReferenceImage image)
        {
            Vector2 size = image.Settings.SizeInCells;

            center = new Vector3(size.X / 2f, size.Y / 2f, 0f);

            return true;
        }

        if (TryMeasureSubtree(element, Transform3D.Identity, out Aabb box))
        {
            center = box.GetCenter();

            return true;
        }

        center = default;

        return false;
    }

    /// <summary>
    /// The middle of everything a container holds, in its own frame. What a fresh
    /// prefab instance's pivot is set to: the instance has no cells of its own, and
    /// handles at its local origin would sit in a corner of the room rather than on
    /// the thing they move.
    /// </summary>
    /// <remarks>
    /// Measured on the source rather than on the instance, because the contents are
    /// expanded after the element is built — and the two frames agree, the contents
    /// standing at the same local transforms in both.
    /// </remarks>
    public static bool TryCalculate(IHierarchyContainer container, out Vector3 center)
    {
        if (TryMeasureSubtree(container, Transform3D.Identity, out Aabb box))
        {
            center = box.GetCenter();

            return true;
        }

        center = default;

        return false;
    }

    /// <summary>
    /// Unions the boxes of every voxel model below <paramref name="container"/>,
    /// each carried up through the transforms between them by its eight corners:
    /// a rotated child's box in the parent's frame is not its own box turned, and
    /// the shortcut would put the pivot off to one side.
    /// </summary>
    private static bool TryMeasureSubtree(
        IHierarchyContainer container,
        Transform3D fromChild,
        out Aabb box)
    {
        box = default;
        var found = false;

        if (container is IHierarchyElement element && element.Component is IVoxelModel model)
        {
            VoxelExtent extent = VoxelExtent.Measure(model.Voxels.Keys);

            if (!extent.IsEmpty)
            {
                box = Enclose(fromChild, extent);
                found = true;
            }
        }

        foreach (IHierarchyElement child in container.Children)
        {
            Transform3D toParent = fromChild
                * TransformHelper.ToTransform(child.Transform, child.PivotPoint);

            if (!TryMeasureSubtree(child, toParent, out Aabb childBox))
                continue;

            box = found ? box.Merge(childBox) : childBox;
            found = true;
        }

        return found;
    }

    private static Aabb Enclose(Transform3D transform, VoxelExtent extent)
    {
        Vector3 corner = extent.Corner;
        Vector3 far = extent.FarCorner;
        var box = new Aabb(transform * corner, Vector3.Zero);

        for (var index = 1; index < 8; index++)
        {
            box = box.Expand(transform * new Vector3(
                (index & 1) == 0 ? corner.X : far.X,
                (index & 2) == 0 ? corner.Y : far.Y,
                (index & 4) == 0 ? corner.Z : far.Z));
        }

        return box;
    }
}
