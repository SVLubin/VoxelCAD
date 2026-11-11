using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// A placement of one cell lattice inside another: <c>w = Offset + Rotation * u</c>,
/// where <see cref="Rotation"/> is a signed permutation and both sides are cell
/// indices rather than points.
/// </summary>
/// <remarks>
/// Index space is what makes an importer's arithmetic exact. A voxel format places
/// a model by turning it about a cell and translating by whole cells, so every
/// transform in the chain is a signed permutation plus an integer offset — a set
/// closed under composition. Working in points instead drags a half-cell through
/// every step, and a half-cell carried through a rotation stops being a half-cell:
/// <c>R · (½,½,½)</c> has components of either sign. So the half is introduced
/// exactly once, where a cell index becomes an element's origin, and never
/// composed.
/// <para>
/// The rotation's entries are whole numbers held in a <see cref="Basis"/>. Floats
/// represent them exactly and composition of signed permutations stays exact, so
/// nothing here rounds.
/// </para>
/// </remarks>
internal readonly record struct CellTransform(Basis Rotation, Vector3I Offset)
{
    public static CellTransform Identity => new(Basis.Identity, Vector3I.Zero);

    /// <summary>
    /// This transform applied after <paramref name="inner"/>, so that the result
    /// maps <paramref name="inner"/>'s source lattice into this one's target.
    /// </summary>
    public CellTransform Compose(CellTransform inner) => new(
        Rotation * inner.Rotation,
        Offset + Apply(Rotation, inner.Offset));

    /// <summary>Where a cell of the source lattice lands in the target one.</summary>
    public Vector3I Apply(Vector3I cell) => Offset + Apply(Rotation, cell);

    /// <summary>
    /// A signed permutation applied to an integer vector, kept integral. Godot's
    /// <c>Basis * Vector3I</c> does not exist and going through <see cref="Vector3"/>
    /// would round; a permutation moves whole numbers, so this rounds nothing.
    /// </summary>
    public static Vector3I Apply(Basis rotation, Vector3I cell)
    {
        Vector3 turned = rotation * new Vector3(cell.X, cell.Y, cell.Z);

        return new Vector3I(
            Mathf.RoundToInt(turned.X),
            Mathf.RoundToInt(turned.Y),
            Mathf.RoundToInt(turned.Z));
    }
}
