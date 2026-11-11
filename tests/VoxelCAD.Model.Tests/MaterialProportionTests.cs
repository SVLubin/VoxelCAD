using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// The ready-made materials are sized against each other the way the real things
/// are. The scale each one carries is measured in cells to one <b>unit</b> of its
/// pattern — a course, a board, a panel, a mosaic tile — so the numbers are
/// directly comparable, and these say what the comparison must come out as.
/// </summary>
/// <remarks>
/// Written because they were not. While the dial meant the whole tile the numbers
/// were not comparable at all: a tile holds one panel and six hammer marks, so the
/// same dial gave sizes six times apart and the presets had drifted into whatever
/// each of them happened to need. What a family of materials owes its user is that
/// a pool's mosaic is smaller than a brick course, which is smaller than a floor
/// board, which is smaller than a ceramic tile — because that is how they are in a
/// yard, and a model made of them should read that way without anybody measuring.
/// </remarks>
public sealed class MaterialProportionTests
{
    private static float Unit(MaterialPreset preset) =>
        MaterialPresetTable.Get(preset).Appearance.FinishScale;

    /// <summary>
    /// The architectural chain, smallest first. Every step is a real proportion:
    /// a 25 mm pool tile, a 75 mm brick course, a 75 mm parquet block, a 150 mm
    /// board, a 190 mm glass block, a 300 mm ceramic tile.
    /// </summary>
    [Fact]
    public void TheArchitecturalMaterialsRunSmallestToLargest()
    {
        Assert.True(Unit(MaterialPreset.Mosaic) < Unit(MaterialPreset.Brick));
        Assert.True(Unit(MaterialPreset.Brick) <= Unit(MaterialPreset.Parquet));
        Assert.True(Unit(MaterialPreset.Parquet) < Unit(MaterialPreset.PlankFloor));
        Assert.True(Unit(MaterialPreset.PlankFloor) < Unit(MaterialPreset.GlassBlockWave));
        Assert.True(Unit(MaterialPreset.GlassBlockWave) < Unit(MaterialPreset.Tile));
    }

    /// <summary>
    /// The one the proportions were asked for by name: a pool's mosaic repeats in
    /// the space half a brick course takes.
    /// </summary>
    [Fact]
    public void APoolsMosaicRepeatsWithinHalfABrickCourse()
    {
        Assert.True(Unit(MaterialPreset.Mosaic) <= Unit(MaterialPreset.Brick) / 2f);
    }

    /// <summary>
    /// And a mark worked into a part is finer than anything a wall is built from:
    /// a knurl is a millimetre or two and a hammer mark is fifteen, against a pool
    /// tile's twenty-five.
    /// </summary>
    /// <remarks>
    /// A tread plate's lentil is deliberately not in this list, and the first cut
    /// of the test had it there and failed: a lentil is about thirty millimetres,
    /// which is a pool tile's size and not a knurl's. The chequer plate is a
    /// walked-on surface rather than a gripped one, and the numbers say so.
    /// </remarks>
    [Fact]
    public void AWorkedMarkIsFinerThanAnythingBuilt()
    {
        float smallestBuilt = Unit(MaterialPreset.Mosaic);

        Assert.True(Unit(MaterialPreset.KnurledGrip) < smallestBuilt);
        Assert.True(Unit(MaterialPreset.HammeredMetal) < smallestBuilt);

        // The lentil sits with the mosaic rather than under it, within a third.
        Assert.InRange(Unit(MaterialPreset.TreadPlate) / smallestBuilt, 0.7f, 1.4f);
    }

    /// <summary>
    /// Nothing lands on one cell to a unit, which is the size that puts a joint of
    /// the pattern against every edge and corner of every model — measured at three
    /// times the flat material's contrast across a concave corner, and the defect
    /// this family was retuned to clear.
    /// </summary>
    [Fact]
    public void NoBuiltSurfaceHasAUnitOfExactlyOneCell()
    {
        MaterialPreset[] built =
        {
            MaterialPreset.Brick, MaterialPreset.Tile, MaterialPreset.Mosaic,
            MaterialPreset.PlankFloor, MaterialPreset.Parquet,
            MaterialPreset.GlassBlockWave, MaterialPreset.GlassBlockCross,
            MaterialPreset.GlassBlockBubble
        };

        foreach (MaterialPreset preset in built)
            Assert.NotEqual(1f, Unit(preset), 3);
    }
}
