using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// The turn between this document's Y-up cells and MagicaVoxel's Z-up ones, and
/// the one place either direction of it is written down.
/// </summary>
/// <remarks>
/// It is a proper rotation — the cell-index form of a +90° turn about X — so a
/// model changes hands upright and unmirrored rather than being flipped into
/// place. Both directions live here because the writer and the reader must be
/// exact inverses: two copies of a convention are two chances to disagree, and a
/// disagreement here is a model that comes back mirrored from its own file.
/// <para>
/// The <c>-1</c> is what makes it an <em>index</em> map rather than a point map. A
/// cell occupies the unit box from its own index, so flipping an axis sends the
/// box at <c>k</c> to the box at <c>-k-1</c>; without the offset the cells would
/// tile with a one-cell gap.
/// </para>
/// </remarks>
internal static class VoxAxis
{
    /// <summary>A world cell (Y-up) as MagicaVoxel's own (Z-up).</summary>
    public static Vector3I ToMagica(Vector3I world) => new(world.X, -world.Z - 1, world.Y);

    /// <summary>A MagicaVoxel cell (Z-up) as this document's own (Y-up).</summary>
    public static Vector3I ToWorld(Vector3I magica) => new(magica.X, magica.Z, -magica.Y - 1);

    /// <summary>
    /// The linear half of <see cref="ToWorld"/> — a direction rather than a cell,
    /// so no offset. What a translation measured in MagicaVoxel's axes becomes in
    /// ours.
    /// </summary>
    public static Vector3I TurnToWorld(Vector3I magica) => new(magica.X, magica.Z, -magica.Y);

    /// <summary>
    /// The rotation itself, as a basis: MagicaVoxel's axes expressed in ours. Used
    /// to carry a rotation across the seam by conjugation, <c>P⁻¹ · R · P</c>.
    /// </summary>
    public static Basis ToWorldBasis { get; } = new(
        column0: new Vector3(1f, 0f, 0f),
        column1: new Vector3(0f, 0f, -1f),
        column2: new Vector3(0f, 1f, 0f));

    /// <summary>The same turn the other way, <c>P</c>.</summary>
    public static Basis ToMagicaBasis { get; } = ToWorldBasis.Transposed();

    /// <summary>
    /// A rotation stated in MagicaVoxel's axes, restated in ours. Conjugation
    /// rather than multiplication: the rotation is the same physical turn, seen
    /// from a frame that is itself turned.
    /// </summary>
    public static Basis TurnToWorld(Basis magica) => ToWorldBasis * magica * ToMagicaBasis;
}
