using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// A cell-lattice placement read as this document's transform: quarter turns and
/// a position that names the pivot.
/// </summary>
/// <remarks>
/// This is the one place a half-cell enters the arithmetic, and it enters once.
/// A cell index is a corner while a transform acts on points, so the two differ by
/// the half-cell from a corner to a centre — and that half, carried through a
/// rotation, comes back as plus or minus a half. The difference is always a whole
/// number of cells, which is exactly why an imported element's local origin is
/// integral for every one of the twenty-four rotations and its position never
/// degrades out of <see cref="CommonTypes.FieldMode.Integer"/>.
/// </remarks>
internal static class ImportPlacement
{
    private static readonly Vector3 HalfCell = new(0.5f, 0.5f, 0.5f);

    /// <summary>Rotation in degrees, as whole quarter turns.</summary>
    /// <remarks>
    /// Read off the basis with Godot's own decomposition rather than derived, so
    /// it agrees with <c>TransformHelper</c> by construction — the same YXZ order
    /// the document composes with. Which of several equivalent decompositions it
    /// picks does not matter: what has to hold is that the basis comes back, and
    /// only a test of the basis can say so.
    /// </remarks>
    public static Vector3 RotationDegrees(Basis rotation)
    {
        Vector3 euler = rotation.GetEuler();

        return new Vector3(
            QuarterTurns(euler.X),
            QuarterTurns(euler.Y),
            QuarterTurns(euler.Z));
    }

    /// <summary>
    /// Where the element's pivot stands in its parent, which is what the position
    /// channel names.
    /// </summary>
    public static Vector3 Position(CellTransform placement, Vector3 pivot)
    {
        Vector3 origin = Origin(placement);

        return origin + placement.Rotation * pivot;
    }

    /// <summary>
    /// The element's local origin: where its cell (0,0,0) has its corner. Always a
    /// whole number of cells, which is the property the whole scheme rests on.
    /// </summary>
    private static Vector3 Origin(CellTransform placement)
    {
        Vector3 corner = placement.Rotation * HalfCell - HalfCell;

        return new Vector3(
            placement.Offset.X - corner.X,
            placement.Offset.Y - corner.Y,
            placement.Offset.Z - corner.Z);
    }

    private static float QuarterTurns(float radians) =>
        Mathf.Round(Mathf.RadToDeg(radians) / 90f) * 90f;
}
