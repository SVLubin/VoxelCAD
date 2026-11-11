using Godot;

namespace VoxelCAD.Model;

/// <summary>
/// The unit conversion between where the pivot is <b>stored</b> — absolute, in
/// the element's own cells — and how every user-facing surface <b>speaks</b> of
/// it: as an offset from the middle of what the element holds, where 0 is the
/// centre of the model itself.
/// </summary>
/// <remarks>
/// A fresh element's local zero coincides with the scene's corner, which is a
/// point nobody chose and nobody cares about — a pivot printed in those cells
/// reads as noise. The middle of the content is the point the question "where on
/// the model is the grip?" is naturally measured from, so that is the zero the
/// surfaces use, completing the chain: position is about the room, the pivot is
/// about the model, the mirrors are about the pivot. The anchor is
/// <see cref="PivotCenter"/>'s own answer — the same one the Center button and
/// <c>point=center</c> land on, so "offset 0" and "centred" cannot disagree —
/// falling back to the local zero when there is nothing to centre on.
/// <para>
/// Painting can move the anchor, so an edit that grows the box re-labels the
/// offset over an unmoved pivot — the same honesty as a pivot move re-labelling
/// the mirror offsets. The voxel anchor lands on the half-cell lattice, so for
/// models the conversion is exact; a group's anchor is a float box centre, and
/// <see cref="IScene.SetPivot"/> rounds the result to the lattice as it always
/// did.
/// </para>
/// </remarks>
public static class PivotUnits
{
    /// <summary>The point offsets are measured from: the content's middle, else local zero.</summary>
    public static Vector3 Anchor(IHierarchyElement element) =>
        PivotCenter.TryCalculate(element, out Vector3 center) ? center : Vector3.Zero;

    /// <summary>The stored pivot as the offset the surfaces show.</summary>
    public static Vector3 ToOffset(Vector3 pivotPoint, IHierarchyElement element) =>
        pivotPoint - Anchor(element);

    /// <summary>A spoken offset as the absolute point the model stores.</summary>
    public static Vector3 ToAbsolute(Vector3 offset, IHierarchyElement element) =>
        offset + Anchor(element);
}
