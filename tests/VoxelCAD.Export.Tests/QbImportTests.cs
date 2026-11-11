using System;
using System.Linq;
using Godot;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// The Qubicle reader: the two colour orders, the two handedness settings, the
/// plain and the run-length stream, and the visibility mask.
/// </summary>
public sealed class QbImportTests
{
    [Fact]
    public void APlainStreamIsIndexedXFastestThenYThenZ()
    {
        var builder = QbBuilder.File();

        // Right handed, so nothing is mirrored and the indexing is the only thing
        // this test is about. What handedness does has its own test.
        builder.ZAxisOrientation = 1;

        uint filled = builder.Cell(1, 2, 3, 255);
        var cells = new uint[2 * 2 * 2];

        // The cell at index 1 is x = 1; at index 2, y = 1; at index 4, z = 1.
        cells[1] = filled;
        cells[2] = filled;
        cells[4] = filled;

        ImportedNode node = Assert.Single(Read(builder.Matrix("M", 2, 2, 2, (0, 0, 0), cells)).Roots);

        Assert.Equal(3, node.Volume!.Cells.Count);
        Assert.Contains(node.Volume.Cells, cell => cell.Cell == new Vector3I(1, 0, 0));
        Assert.Contains(node.Volume.Cells, cell => cell.Cell == new Vector3I(0, 1, 0));
        Assert.Contains(node.Volume.Cells, cell => cell.Cell == new Vector3I(0, 0, 1));
    }

    [Fact]
    public void TheRunLengthStreamReadsTheSameCellsAsThePlainOne()
    {
        var cells = new uint[4 * 4 * 4];
        var builder = QbBuilder.File();
        uint red = builder.Cell(255, 0, 0, 255);
        uint blue = builder.Cell(0, 0, 255, 255);

        // A long run to reach the repeat code, and a pair too short for it.
        for (var i = 0; i < 16; i++)
            cells[i] = red;

        cells[20] = blue;
        cells[21] = blue;

        ImportedFile plain = Read(QbBuilder.File().Matrix("M", 4, 4, 4, (0, 0, 0), cells));

        var compressed = QbBuilder.File();
        compressed.Compressed = 1;

        ImportedFile coded = Read(compressed.Matrix("M", 4, 4, 4, (0, 0, 0), cells));

        Assert.Equal(18, plain.Roots[0].Volume!.Cells.Count);
        Assert.Equal(
            plain.Roots[0].Volume!.Cells.OrderBy(Key).ToList(),
            coded.Roots[0].Volume!.Cells.OrderBy(Key).ToList());
    }

    [Theory]
    [InlineData(0u, 10, 20, 30)]
    [InlineData(1u, 30, 20, 10)]
    public void TheColourOrderFollowsTheHeader(uint colorFormat, int red, int green, int blue)
    {
        var builder = QbBuilder.File();
        builder.ColorFormat = colorFormat;

        ImportedFile file = Read(builder.Matrix(
            "M", 1, 1, 1, (0, 0, 0), new[] { builder.Cell(10, 20, 30, 255) }));

        ImportedMaterial material = Assert.Single(file.Materials);

        Assert.Equal(red, Byte(material.Color.R));
        Assert.Equal(green, Byte(material.Color.G));
        Assert.Equal(blue, Byte(material.Color.B));
    }

    /// <summary>
    /// A left-handed file measures depth the other way, so the cells inside a
    /// matrix are mirrored too — which a matrix one cell deep cannot show, the
    /// mirror being a no-op there.
    /// </summary>
    [Theory]
    [InlineData(0u, true)]
    [InlineData(1u, false)]
    public void HandednessMirrorsTheCellsInsideAMatrix(uint orientation, bool mirrored)
    {
        var builder = QbBuilder.File();
        builder.ZAxisOrientation = orientation;

        // One cell at the near face of a matrix four deep. Read the other way
        // round it lands at the far face instead.
        var cells = new uint[1 * 1 * 4];
        cells[0] = builder.Cell(9, 9, 9, 255);

        ImportedFile file = Read(builder.Matrix("M", 1, 1, 4, (0, 0, 0), cells));
        ImportedCell cell = Assert.Single(Assert.Single(file.Roots).Volume!.Cells);

        Assert.Equal(mirrored ? 3 : 0, cell.Cell.Z);
    }

    /// <summary>
    /// A left-handed file measures depth the other way, so the whole placement is
    /// mirrored — the matrix's position with it, not only the cells inside it.
    /// Two matrices at different depths are what tells the two apart.
    /// </summary>
    [Theory]
    [InlineData(0u, -6, -11)]
    [InlineData(1u, 5, 10)]
    public void HandednessMirrorsThePlacementAndNotJustTheCells(
        uint orientation,
        int firstDepth,
        int secondDepth)
    {
        var builder = QbBuilder.File();
        builder.ZAxisOrientation = orientation;

        uint filled = builder.Cell(1, 1, 1, 255);
        ImportedFile file = Read(builder
            .Matrix("Near", 1, 1, 1, (0, 0, 5), new[] { filled })
            .Matrix("Far", 1, 1, 1, (0, 0, 10), new[] { filled }));

        Assert.Equal(firstDepth, Placed(file.Roots[0]).Z);
        Assert.Equal(secondDepth, Placed(file.Roots[1]).Z);

        // Whichever way round it is read, the two stay ten cells apart and in the
        // order the file put them in — a mirror that only reached inside a matrix
        // would leave the parts of a model shuffled along the depth axis.
        Assert.Equal(5, Math.Abs(Placed(file.Roots[1]).Z - Placed(file.Roots[0]).Z));
    }

    [Fact]
    public void AVisibilityMaskInTheAlphaMeansOccupiedAndNothingElse()
    {
        var builder = QbBuilder.File();
        builder.VisibilityMaskEncoded = 1;

        // The specification's own test is that a mask of zero is an empty voxel;
        // every other value names which sides Qubicle would draw, which is none of
        // this document's business.
        ImportedFile file = Read(builder.Matrix("M", 4, 1, 1, (0, 0, 0), new[]
        {
            builder.Cell(255, 0, 0, 0),
            builder.Cell(255, 0, 0, 1),
            builder.Cell(255, 0, 0, 33),
            builder.Cell(255, 0, 0, 255)
        }));

        Assert.Equal(3, Assert.Single(file.Roots).Volume!.Cells.Count);

        // And the alpha never reaches the material: a .qb has no opacity.
        ImportedMaterial material = Assert.Single(file.Materials);

        Assert.Equal(1f, material.Color.A);
        Assert.False(material.Appearance.Transparent);
    }

    [Fact]
    public void OneColourAcrossSeveralMatricesIsOneMaterial()
    {
        var builder = QbBuilder.File();
        uint skin = builder.Cell(200, 150, 120, 255);
        uint cloth = builder.Cell(20, 40, 80, 255);

        ImportedFile file = Read(builder
            .Matrix("Head", 1, 1, 1, (0, 0, 0), new[] { skin })
            .Matrix("Hand", 1, 1, 1, (2, 0, 0), new[] { skin })
            .Matrix("Coat", 1, 1, 1, (4, 0, 0), new[] { cloth }));

        Assert.Equal(2, file.Materials.Count);
        Assert.Equal(new[] { "#C89678", "#142850" }, file.Materials.Select(m => m.Name));
    }

    [Fact]
    public void AMatrixKeepsTheNameTheFileGaveIt()
    {
        var builder = QbBuilder.File();

        ImportedFile file = Read(builder.Matrix(
            "K_Foot_Right", 1, 1, 1, (0, 0, 0), new[] { builder.Cell(1, 1, 1, 255) }));

        Assert.Equal("K_Foot_Right", Assert.Single(file.Roots).Name);
    }

    [Fact]
    public void ATruncatedFileIsRefusedRatherThanHalfRead()
    {
        var builder = QbBuilder.File();
        byte[] whole = builder
            .Matrix("M", 2, 2, 2, (0, 0, 0), Enumerable
                .Repeat(builder.Cell(1, 2, 3, 255), 8)
                .ToArray())
            .Build();

        for (var length = 0; length < whole.Length; length += 3)
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => new QbImportFormat().Read(whole[..length], "part"));

            Assert.Contains(".qb file cannot be read", error.Message);
        }
    }

    /// <summary>
    /// A run-length count is a 32-bit number the file chooses, so it can name
    /// more cells than the matrix has room for. Refused by name rather than
    /// written past the end of the slice.
    /// </summary>
    [Fact]
    public void ARunLongerThanTheSliceIsRefused()
    {
        var builder = QbBuilder.File();
        builder.Compressed = 1;

        byte[] bytes = builder
            .Matrix("M", 2, 2, 2, (0, 0, 0), new uint[8])
            .Build();

        // Replace the first slice with a single run claiming the whole address
        // space: header (24) + name length (1) + name (1) + size (12) + position
        // (12) is where the stream starts.
        var at = 24 + 1 + 1 + 12 + 12;
        var overrun = new byte[at + 12];

        Array.Copy(bytes, overrun, at);
        Array.Copy(BitConverter.GetBytes(2u), 0, overrun, at, 4);
        Array.Copy(BitConverter.GetBytes(uint.MaxValue), 0, overrun, at + 4, 4);
        Array.Copy(BitConverter.GetBytes(0xFF0000FFu), 0, overrun, at + 8, 4);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new QbImportFormat().Read(overrun, "part"));

        Assert.Contains("more cells than the matrix has room for", error.Message);
    }

    [Fact]
    public void AFileDeclaringMoreMatricesThanCouldExistIsRefused()
    {
        var bytes = new byte[24];

        Array.Copy(BitConverter.GetBytes(0x0000_0101u), 0, bytes, 0, 4);
        Array.Copy(BitConverter.GetBytes(uint.MaxValue), 0, bytes, 20, 4);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new QbImportFormat().Read(bytes, "part"));

        Assert.Contains("matrices", error.Message);
    }

    /// <summary>
    /// A matrix is a dense grid, so a size alone can ask for more memory than
    /// exists. Refused on the declared size, before a byte of it is read.
    /// </summary>
    [Fact]
    public void AMatrixTooLargeToBeAModelIsRefused()
    {
        var builder = QbBuilder.File();
        byte[] bytes = builder
            .Matrix("M", 1, 1, 1, (0, 0, 0), new[] { builder.Cell(1, 1, 1, 255) })
            .Build();

        // 2000 cubed is eight billion cells and forty megabytes of declared
        // stream; nothing beyond the header has to be read to know it is not one.
        var at = 24 + 1 + 1;

        Array.Copy(BitConverter.GetBytes(2000u), 0, bytes, at, 4);
        Array.Copy(BitConverter.GetBytes(2000u), 0, bytes, at + 4, 4);
        Array.Copy(BitConverter.GetBytes(2000u), 0, bytes, at + 8, 4);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new QbImportFormat().Read(bytes, "part"));

        Assert.Contains("which is not a model", error.Message);
    }

    /// <summary>
    /// A run-length stream states a filled million in twelve bytes, so a bound per
    /// matrix is no bound at all — a file may hold as many matrices as it likes.
    /// The ceiling is what one import may create in total, and a kilobyte of file
    /// must not be able to ask for a machine's worth of memory.
    /// </summary>
    [Fact]
    public void AFileAskingForMoreCellsThanAnImportWillMakeIsRefused()
    {
        var builder = QbBuilder.File();
        builder.Compressed = 1;

        uint filled = builder.Cell(200, 30, 30, 255);

        // Eight matrices, each a quarter of the ceiling and every cell of them
        // filled, each slice stated as one run: a couple of kilobytes of file
        // asking for over thirty million cells.
        for (var i = 0; i < 8; i++)
        {
            builder.Matrix($"M{i}", 512, 512, 16, (0, 0, 0), Enumerable
                .Repeat(filled, 512 * 512 * 16)
                .ToArray());
        }

        byte[] bytes = builder.Build();

        Assert.True(bytes.Length < 8192, $"the file itself is {bytes.Length} bytes");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new QbImportFormat().Read(bytes, "part"));

        Assert.Contains("an import will create", error.Message);
    }

    [Fact]
    public void AMatrixWithNoSizeIsRefused()
    {
        var builder = QbBuilder.File();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Read(builder.Matrix("M", 0, 1, 1, (0, 0, 0), Array.Empty<uint>())));

        Assert.Contains("a side of 0 cells", error.Message);
    }

    private static Vector3I Placed(ImportedNode node) =>
        node.Placement.Apply(node.Volume!.Cells[0].Cell);

    private static (int, int, int) Key(ImportedCell cell) => (cell.Cell.X, cell.Cell.Y, cell.Cell.Z);

    private static int Byte(float channel) => (int)MathF.Round(channel * 255f);

    private static ImportedFile Read(QbBuilder builder) =>
        new QbImportFormat().Read(builder.Build(), "part");
}
