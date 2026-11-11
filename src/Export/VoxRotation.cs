using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// MagicaVoxel's <c>_r</c> byte: a signed permutation matrix packed into eight
/// bits, which is the whole of how a scene node states its rotation.
/// </summary>
/// <remarks>
/// The spec's own layout: bits 0-1 give the column of the non-zero entry in the
/// first row, bits 2-3 the column in the second row, and the third row takes the
/// column left over; bits 4, 5 and 6 are the signs of the three rows.
/// <para>
/// <b>Row-major, and that is measured rather than assumed.</b> The same eight bits
/// read column-major give the transpose, which for a quarter turn is the opposite
/// turn — and both readings stand a model upright, so a symmetric subject cannot
/// tell them apart. What separates them is where the model's <em>grid</em> lands:
/// MagicaVoxel stands a model on the ground, and only the row-major reading does.
/// Checked against two of MagicaVoxel's own files: <c>cars.vox</c>, whose two cars
/// both sit at z = 0, and <c>Gomez.vox</c>, whose sprite is stood up by
/// <c>_r = 40</c> and lands with its grid on the ground rather than eleven cells
/// above it.
/// </para>
/// <para>
/// Bit 7 is unused, so every rotation has two encodings; the byte also encodes 24
/// <em>reflections</em>, which MagicaVoxel does not write but the format allows —
/// <see cref="IsMirrored"/> is how a reader tells them apart.
/// </para>
/// </remarks>
internal static class VoxRotation
{
    /// <summary>
    /// What an absent <c>_r</c> means: the first row's non-zero in column 0, the
    /// second's in column 1, every sign positive.
    /// </summary>
    public const byte IdentityByte = 4;

    /// <summary>
    /// What an absent <c>_r</c> decodes to, taken from <see cref="IdentityByte"/>
    /// rather than written down a second time — so the constant is what the reader
    /// actually falls back on and the two cannot come to disagree.
    /// </summary>
    public static Basis Identity { get; } =
        TryDecode(IdentityByte, out Basis identity) ? identity : Basis.Identity;

    /// <summary>
    /// The matrix an <c>_r</c> byte stands for, in MagicaVoxel's own axes, or
    /// false when the byte names no matrix at all — two rows claiming one column,
    /// which the encoding permits and no rotation can mean.
    /// </summary>
    public static bool TryDecode(byte value, out Basis rotation)
    {
        rotation = Basis.Identity;

        var firstColumn = value & 0b11;
        var secondColumn = (value >> 2) & 0b11;

        if (firstColumn > 2 || secondColumn > 2 || firstColumn == secondColumn)
            return false;

        var thirdColumn = 3 - firstColumn - secondColumn;
        var columnsList = new[] { firstColumn, secondColumn, thirdColumn };
        var rows = new float[3, 3];

        for (var row = 0; row < 3; row++)
            rows[row, columnsList[row]] = ((value >> (4 + row)) & 1) == 0 ? 1f : -1f;

        // Godot's basis is built from its columns and the byte describes rows, so
        // each column is gathered from one column of the rows — which assembles
        // the very matrix the byte names, not its transpose. Reading the byte the
        // other way round would give the transpose, and for a quarter turn that
        // is the opposite turn; see the class comment for what settled it.
        rotation = new Basis(
            column0: new Vector3(rows[0, 0], rows[1, 0], rows[2, 0]),
            column1: new Vector3(rows[0, 1], rows[1, 1], rows[2, 1]),
            column2: new Vector3(rows[0, 2], rows[1, 2], rows[2, 2]));

        return true;
    }

    /// <summary>
    /// Whether the matrix turns a model inside out rather than turning it round.
    /// The determinant of a signed permutation is exactly ±1, so this needs no
    /// tolerance.
    /// </summary>
    public static bool IsMirrored(Basis rotation) => rotation.Determinant() < 0f;
}
