using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// The MagicaVoxel reader, against files built byte by byte so each test states
/// the file it is about.
/// </summary>
public sealed class VoxImportTests
{
    [Fact]
    public void AColourIndexReadsThePaletteEntryBeforeIt()
    {
        // The format's own note: colours 0..254 are the palette's indices 1..255.
        // So the cell wearing index 1 must come out the colour written at entry 1,
        // which sits at the very start of the chunk.
        ImportedFile file = Read(VoxBuilder.File()
            .Size(1, 1, 1)
            .Voxels((0, 0, 0, 1))
            .Palette((1, 10, 20, 30, 255), (2, 200, 210, 220, 255)));

        ImportedMaterial material = Assert.Single(file.Materials);

        Assert.Equal(10, (int)MathF.Round(material.Color.R * 255f));
        Assert.Equal(20, (int)MathF.Round(material.Color.G * 255f));
        Assert.Equal(30, (int)MathF.Round(material.Color.B * 255f));
    }

    [Fact]
    public void OnlyTheEntriesTheCellsWearBecomeMaterials()
    {
        ImportedFile file = Read(VoxBuilder.File()
            .Size(4, 1, 1)
            .Voxels((0, 0, 0, 7), (1, 0, 0, 7), (2, 0, 0, 200))
            .Palette((7, 1, 2, 3, 255), (200, 4, 5, 6, 255), (99, 9, 9, 9, 255)));

        Assert.Equal(2, file.Materials.Count);
        Assert.Equal(new[] { "Palette 007", "Palette 200" }, file.Materials.Select(m => m.Name));
    }

    [Fact]
    public void HeightAlongZComesBackAlongYWithoutMirroring()
    {
        // A model two wide, three deep and four tall in MagicaVoxel's axes, with a
        // marked cell at the top. Ours are Y-up, so the height has to land on Y and
        // the depth on Z — and the marked cell must stay at the top rather than
        // arriving at the bottom, which is what a mirrored reading would do.
        ImportedFile file = Read(VoxBuilder.File()
            .Size(2, 3, 4)
            .Voxels((0, 0, 0, 1), (0, 0, 3, 2))
            .Palette((1, 255, 0, 0, 255), (2, 0, 0, 255, 255)));

        ImportedNode node = Assert.Single(file.Roots);

        Assert.Equal(new Vector3I(2, 4, 3), node.Volume!.Size);

        ImportedCell top = Assert.Single(node.Volume.Cells, cell => cell.PaletteIndex == 1);
        ImportedCell bottom = Assert.Single(node.Volume.Cells, cell => cell.PaletteIndex == 0);

        Assert.Equal(3, top.Cell.Y);
        Assert.Equal(0, bottom.Cell.Y);
    }

    /// <summary>
    /// The rule the whole placement rests on: a model is placed by its grid's
    /// middle, taken by integer division, so an even side puts the middle one cell
    /// past the halfway mark.
    /// </summary>
    [Theory]
    [InlineData(13, 6)]
    [InlineData(14, 7)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void AModelIsPlacedByTheMiddleOfItsGrid(int height, int middle)
    {
        // One cell at the bottom of a column of the given height, placed so that
        // the grid's middle sits at MagicaVoxel's origin. The bottom of the grid is
        // then `middle` cells below it — and our Y is that same up axis.
        ImportedFile file = Read(VoxBuilder.File()
            .Size(1, 1, height)
            .Voxels((0, 0, 0, 1))
            .Palette((1, 255, 255, 255, 255))
            .Transform(0, 1, Vector3I.Zero)
            .Group(1, 2)
            .Transform(2, 3, Vector3I.Zero)
            .Shape(3, 0));

        ImportedNode node = Assert.Single(file.Roots);

        Assert.Equal(-middle, node.Placement.Apply(node.Volume!.Cells[0].Cell).Y);
    }

    [Fact]
    public void AGroupBecomesAnElementHoldingItsParts()
    {
        ImportedFile file = Read(VoxBuilder.File()
            .Size(1, 1, 1)
            .Voxels((0, 0, 0, 1))
            .Palette((1, 255, 255, 255, 255))
            .Transform(0, 1, Vector3I.Zero)
            .Group(1, 2)
            .Transform(2, 3, new Vector3I(5, 0, 0), name: "Assembly")
            .Group(3, 4, 6)
            .Transform(4, 5, new Vector3I(1, 0, 0), name: "Left")
            .Shape(5, 0)
            .Transform(6, 7, new Vector3I(-1, 0, 0), name: "Right")
            .Shape(7, 0));

        ImportedNode assembly = Assert.Single(file.Roots);

        Assert.Equal("Assembly", assembly.Name);
        Assert.Null(assembly.Volume);
        Assert.Equal(new[] { "Left", "Right" }, assembly.Children.Select(child => child.Name));

        // The group carries its own transform and the parts theirs, so the parts
        // stand where the two compose to.
        Assert.Equal(6, assembly.Placement.Compose(assembly.Children[0].Placement).Offset.X);
        Assert.Equal(4, assembly.Placement.Compose(assembly.Children[1].Placement).Offset.X);
    }

    /// <summary>
    /// Several transform nodes may point at one shape, which is how the format
    /// states an instance. Each arrival is its own element: this document has
    /// prefabs, but building them from a file is a decision of its own.
    /// </summary>
    [Fact]
    public void OneShapeReachedTwiceBecomesTwoElements()
    {
        ImportedFile file = Read(VoxBuilder.File()
            .Size(1, 1, 1)
            .Voxels((0, 0, 0, 1))
            .Palette((1, 255, 255, 255, 255))
            .Transform(0, 1, Vector3I.Zero)
            .Group(1, 2, 4)
            .Transform(2, 6, new Vector3I(10, 0, 0))
            .Transform(4, 6, new Vector3I(20, 0, 0))
            .Shape(6, 0));

        Assert.Equal(2, file.Roots.Count);
        Assert.Equal(10, file.Roots[0].Placement.Offset.X);
        Assert.Equal(20, file.Roots[1].Placement.Offset.X);
    }

    /// <summary>
    /// The frames have to be told apart by something, or reading the last one
    /// passes as well as reading the first: here the first frame is one cell and
    /// the second is two, in a colour the first does not wear.
    /// </summary>
    [Fact]
    public void OnlyTheFirstFrameOfAnAnimationIsRead()
    {
        ImportedFile file = Read(VoxBuilder.File()
            .Size(2, 1, 1)
            .Voxels((0, 0, 0, 1))
            .Size(2, 1, 1)
            .Voxels((0, 0, 0, 2), (1, 0, 0, 2))
            .Palette((1, 255, 0, 0, 255), (2, 0, 255, 0, 255))
            .Transform(0, 1, Vector3I.Zero)
            .Group(1, 2)
            .Transform(2, 3, Vector3I.Zero)
            .Shape(3, 0, 1));

        ImportedNode node = Assert.Single(file.Roots);

        Assert.Single(node.Volume!.Cells);

        ImportedMaterial material = Assert.Single(file.Materials);

        Assert.Equal("Palette 001", material.Name);
        Assert.Equal(1f, material.Color.R);
        Assert.Contains(file.Notes, note => note.Contains("first frame"));
    }

    /// <summary>
    /// A mirrored rotation is folded into the model's own cells and the transform
    /// keeps the proper rotation left over, so the model lands exactly where the
    /// file asks. MagicaVoxel writes none of these; the byte allows them.
    /// </summary>
    [Fact]
    public void AMirroredShapeIsFoldedIntoItsCells()
    {
        // _r = 6: first row's non-zero in column 2, second in column 1, all signs
        // positive — a permutation with an odd number of swaps, so a reflection.
        Assert.True(VoxRotation.TryDecode(6, out Godot.Basis magica));
        Assert.True(VoxRotation.IsMirrored(magica));

        ImportedFile file = Read(VoxBuilder.File()
            .Size(4, 1, 1)
            .Voxels((0, 0, 0, 1))
            .Palette((1, 255, 255, 255, 255))
            .Transform(0, 1, Vector3I.Zero)
            .Group(1, 2)
            .Transform(2, 3, Vector3I.Zero, rotation: 6)
            .Shape(3, 0));

        ImportedNode node = Assert.Single(file.Roots);

        // What the transform carries is a proper rotation; the flip went into the
        // cells, so the cell that was at one end of the model is at the other.
        Assert.True(node.Placement.Rotation.Determinant() > 0f);
        Assert.Equal(3, Assert.Single(node.Volume!.Cells).Cell.X);
    }

    /// <summary>
    /// A mirrored <em>group</em> has no cells to fold a flip into, so it is
    /// refused by name rather than quietly turned into a rotation it is not.
    /// </summary>
    [Fact]
    public void AMirroredGroupIsRefused()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Read(VoxBuilder.File()
                .Size(1, 1, 1)
                .Voxels((0, 0, 0, 1))
                .Transform(0, 1, Vector3I.Zero, rotation: 6)
                .Group(1, 2)
                .Transform(2, 3, Vector3I.Zero)
                .Shape(3, 0)));

        Assert.Contains("mirrored", error.Message);
    }

    [Fact]
    public void AChunkThisReaderHasNeverHeardOfIsSteppedOver()
    {
        ImportedFile file = Read(VoxBuilder.File()
            .Unknown("LAYR", 32)
            .Size(1, 1, 1)
            .Unknown("rOBJ", 64)
            .Voxels((0, 0, 0, 1))
            .Unknown("NOTE", 16)
            .Palette((1, 255, 255, 255, 255)));

        Assert.Single(Assert.Single(file.Roots).Volume!.Cells);
    }

    [Fact]
    public void AFileWithNoPaletteFallsBackToTheFormatsOwn()
    {
        ImportedFile file = Read(VoxBuilder.File().Size(1, 1, 1).Voxels((0, 0, 0, 1)));

        // Index 1 of the default palette is white.
        ImportedMaterial material = Assert.Single(file.Materials);

        Assert.True(material.Color.IsEqualApprox(new Color(1f, 1f, 1f)), $"got {material.Color}");
    }

    [Theory]
    [InlineData("something else entirely")]
    [InlineData("VOX ")]
    public void SomethingThatIsNotAVoxFileIsRefusedByName(string text)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(text);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => Read(bytes));

        Assert.Contains(".vox file cannot be read", error.Message);
    }

    [Fact]
    public void ATruncatedFileIsRefusedRatherThanHalfRead()
    {
        byte[] whole = VoxBuilder.File()
            .Size(4, 4, 4)
            .Voxels((0, 0, 0, 1), (1, 1, 1, 1))
            .Palette((1, 255, 255, 255, 255))
            .Build();

        for (var length = 20; length < whole.Length; length += 7)
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => Read(whole[..length]));

            Assert.Contains(".vox file cannot be read", error.Message);
        }
    }

    /// <summary>
    /// Both walks over a file are recursive, so a file that nests far enough
    /// would take the process down with a stack overflow rather than being
    /// refused — which is not a refusal, and is exactly what a file built to
    /// break a reader would do. Fifty thousand deep did it before the bounds
    /// went in.
    /// </summary>
    [Fact]
    public void AFileWhoseChunksNestTooDeepIsRefusedRatherThanCrashing()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Read(VoxBuilder.File()
                .Size(1, 1, 1)
                .Voxels((0, 0, 0, 1))
                .Nest(50_000)));

        Assert.Contains("nest", error.Message);
    }

    [Fact]
    public void AGraphThatNestsTooDeepIsRefusedRatherThanCrashing()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Read(VoxBuilder.File()
                .Size(1, 1, 1)
                .Voxels((0, 0, 0, 1))
                .NestedGraph(50_000)));

        Assert.Contains("nests more than", error.Message);
    }

    /// <summary>
    /// And the bound has to be far enough above anything real. MagicaVoxel's own
    /// files are four deep; its deepest fixture is six.
    /// </summary>
    [Fact]
    public void AnOrdinaryDepthOfNestingIsRead()
    {
        ImportedFile file = Read(VoxBuilder.File()
            .Size(1, 1, 1)
            .Voxels((0, 0, 0, 1))
            .Palette((1, 255, 255, 255, 255))
            .NestedGraph(16));

        Assert.Single(file.Materials);
    }

    /// <summary>
    /// A chunk may declare any 32-bit length it likes, and one near
    /// <see cref="int.MaxValue"/> added to an offset wraps negative — which slips
    /// past a bounds check written in <c>int</c> and leaves the walk reading from
    /// a negative index. Each of these is a named refusal rather than whatever the
    /// arithmetic happens to throw.
    /// </summary>
    [Theory]
    [InlineData(int.MaxValue, 0)]
    [InlineData(0, int.MaxValue)]
    [InlineData(int.MaxValue - 15, int.MaxValue - 15)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void AChunkDeclaringAnImpossibleLengthIsRefused(int contentBytes, int childrenBytes)
    {
        byte[] bytes = VoxBuilder.File()
            .Chunk("SIZE", System.Array.Empty<byte>())
            .Build();

        // Overwrite the SIZE chunk's declared lengths in place: past the file
        // header and MAIN's own twelve-byte header, then the four id bytes.
        System.Array.Copy(BitConverter.GetBytes(contentBytes), 0, bytes, 8 + 12 + 4, 4);
        System.Array.Copy(BitConverter.GetBytes(childrenBytes), 0, bytes, 8 + 12 + 8, 4);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Read(bytes));

        Assert.Contains(".vox file cannot be read", error.Message);
    }

    [Fact]
    public void AFileWithNoModelIsRefused()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Read(VoxBuilder.File().Palette((1, 255, 255, 255, 255))));

        Assert.Contains("no model", error.Message);
    }

    [Fact]
    public void AGraphThatNamesANodeItNeverDefinesIsRefused()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Read(VoxBuilder.File()
                .Size(1, 1, 1)
                .Voxels((0, 0, 0, 1))
                .Transform(0, 1, Vector3I.Zero)
                .Group(1, 99)));

        Assert.Contains("never defines", error.Message);
    }

    [Fact]
    public void AGraphThatRefersBackToItselfIsRefused()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Read(VoxBuilder.File()
                .Size(1, 1, 1)
                .Voxels((0, 0, 0, 1))
                .Transform(0, 1, Vector3I.Zero)
                .Group(1, 2)
                .Transform(2, 1, Vector3I.Zero)));

        Assert.Contains("refers back to itself", error.Message);
    }

    [Fact]
    public void AShapeNamingAModelTheFileDoesNotHoldIsRefused()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Read(VoxBuilder.File()
                .Size(1, 1, 1)
                .Voxels((0, 0, 0, 1))
                .Transform(0, 1, Vector3I.Zero)
                .Group(1, 2)
                .Transform(2, 3, Vector3I.Zero)
                .Shape(3, 5)));

        Assert.Contains("model 5", error.Message);
    }

    /// <summary>
    /// Colour index zero is the format's own word for an empty cell, so a voxel
    /// wearing it is not a voxel and must not become a material.
    /// </summary>
    [Fact]
    public void AVoxelWearingTheEmptyIndexIsNotACell()
    {
        ImportedFile file = Read(VoxBuilder.File()
            .Size(2, 1, 1)
            .Voxels((0, 0, 0, 0), (1, 0, 0, 1))
            .Palette((1, 255, 255, 255, 255)));

        Assert.Single(Assert.Single(file.Roots).Volume!.Cells);
        Assert.Single(file.Materials);
    }

    private static ImportedFile Read(VoxBuilder builder) => Read(builder.Build());

    private static ImportedFile Read(byte[] bytes) => new VoxImportFormat().Read(bytes, "part");
}
