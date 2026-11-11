using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// What a fresh element's field modes are, per kind.
/// </summary>
/// <remarks>
/// <para>
/// Integer / Snap90 / Integer is the CAD default and the reason is geometric: it
/// puts a new element's own grid on top of the scene's, which is what lets the
/// brush place cells against the region's walls. Whole cells and right angles are
/// what a part is built on.
/// </para>
/// <para>
/// A camera and a reference have neither a grid nor a brush, and the same
/// reasoning therefore gives
/// the opposite answer: it stands wherever it is pointed. Under Snap90 an aim of
/// thirty degrees would arrive as nought and a camera placed from the viewport
/// would be quantised out of the shot it was placed to keep — which is the trap
/// this exists to close, since nothing about either would say what had happened.
/// Scale is left where every other element has it: a camera ignores it entirely.
/// </para>
/// </remarks>
internal static class ElementTransformDefaults
{
    public static TransformModes For(ComponentKind kind) => kind switch
    {
        ComponentKind.Camera => new TransformModes(
            FieldMode.Float,
            FieldMode.Float,
            FieldMode.Integer),
        // A picture is hung on a wall: whole cells and right angles, which is
        // also what puts its corner on the lattice so one pixel can land on one
        // voxel. Its size is its own, so Integer scale would only be a second,
        // coarser way of resizing it.
        ComponentKind.ReferenceImage => new TransformModes(
            FieldMode.Integer,
            FieldMode.Snap90,
            FieldMode.Float),

        // Somebody else's model arrives at whatever scale and orientation they
        // built it, and is stood next to the work by eye. Integer scale cannot
        // express the 0.4 a two-metre character needs to become sixteen cells.
        ComponentKind.ReferenceModel => new TransformModes(
            FieldMode.Float,
            FieldMode.Float,
            FieldMode.Float),

        _ => new TransformModes(FieldMode.Integer, FieldMode.Snap90, FieldMode.Integer)
    };
}
