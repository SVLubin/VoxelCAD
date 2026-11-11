using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// The unit conversion between where a mirror plane is <b>stored</b> — absolute,
/// in the element's own cells — and how every user-facing surface <b>speaks</b> of
/// it: as an offset from the element's pivot, where 0 is a plane through the
/// pivot itself.
/// </summary>
/// <remarks>
/// Stored absolute on purpose: the brush's orbit and the lasers are about cells,
/// and moving the pivot must not move a mirror. Spoken relative on purpose: the
/// pivot is the one point the interface names, and "mirror at 0" says "mirror
/// through the middle" without anyone reading a coordinate first. Moving the
/// pivot therefore re-labels the offsets and moves nothing. Both lattices are
/// half-cell, so the conversion is integer arithmetic on doubled values and
/// exact by construction. One class, because the inspector and the command
/// surface both convert and must not disagree.
/// </remarks>
public static class SymmetryUnits
{
    /// <summary>
    /// Twice the pivot's coordinate on one axis. Away from zero, not banker's:
    /// Godot's own Round taught this codebase what a midpoint quietly snapping
    /// to even looks like on a grid. The pivot is exact halves, so in practice
    /// this only undoes the division that produced the float.
    /// </summary>
    public static int PivotDoubled(Vector3 pivot, int axis) =>
        (int)Math.Round(pivot[axis] * 2f, MidpointRounding.AwayFromZero);

    /// <summary>A stored plane as the offset the surfaces show, in doubled units.</summary>
    public static int ToOffsetDoubled(int planeDoubled, Vector3 pivot, int axis) =>
        planeDoubled - PivotDoubled(pivot, axis);

    /// <summary>A spoken offset as the absolute plane the model stores, in doubled units.</summary>
    public static int ToAbsoluteDoubled(int offsetDoubled, Vector3 pivot, int axis) =>
        offsetDoubled + PivotDoubled(pivot, axis);

    /// <summary>All three slots re-expressed as offsets, for one push to a panel.</summary>
    /// <remarks>
    /// A slot still in its default state — never enabled, never placed — shows an
    /// offset of zero rather than the distance to the cell it happens to rest in:
    /// its stored position is the local origin, a point nobody chose, and the
    /// meaningful default for a mirror about to be switched on is <em>through the
    /// pivot</em>. The cost is deliberate and small: a plane parked exactly at
    /// cell zero and then disabled is indistinguishable from never-touched, and
    /// re-enabling it lands on the pivot instead — the nearest thing to what was
    /// meant either way.
    /// </remarks>
    public static SymmetryValues ToOffsets(SymmetryValues planes, Vector3 pivot) =>
        new(
            ToOffsetSlot(planes.X, pivot, 0),
            ToOffsetSlot(planes.Y, pivot, 1),
            ToOffsetSlot(planes.Z, pivot, 2));

    /// <summary>
    /// The plane one axis's row shows, back in the absolute doubled units the
    /// model stores — on or off, since a disabled slot keeps its position, and
    /// through the pivot for a slot never touched, exactly as enabling it would
    /// land. What "mirror now" mirrors across when nobody names a plane, asked by
    /// the inspector's button and the <c>mirror-element</c> command alike.
    /// </summary>
    public static int ShownPlaneDoubled(SymmetryValues planes, Vector3 pivot, int axis)
    {
        SymmetryValues offsets = ToOffsets(planes, pivot);
        SymmetryPlane slot = axis switch
        {
            0 => offsets.X,
            1 => offsets.Y,
            _ => offsets.Z
        };

        return ToAbsoluteDoubled(slot.PlaneDoubled, pivot, axis);
    }

    private static SymmetryPlane ToOffsetSlot(SymmetryPlane slot, Vector3 pivot, int axis) =>
        slot == default
            ? slot
            : slot with { PlaneDoubled = ToOffsetDoubled(slot.PlaneDoubled, pivot, axis) };
}
