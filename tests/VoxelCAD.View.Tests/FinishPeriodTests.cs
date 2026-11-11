using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// How many cells one repeat of a facture covers, and how big a preview block has
/// to be to hold it. The rule the shipped preview broke: a fixed three cells showed
/// three quarters of a four-cell plank pattern, so the placed model wore the quarter
/// the preview had cropped.
/// </summary>
public class FinishPeriodTests
{
    private static MaterialAppearance With(MaterialFinish finish, float scale) =>
        MaterialAppearance.Default with { Finish = finish, FinishScale = scale };

    /// <summary>A material with no facture has no period to speak of.</summary>
    [Fact]
    public void ASmoothMaterialHasNoPeriod()
    {
        Assert.Equal(0f, FinishPeriod.GetCells(With(MaterialFinish.Smooth, 4f)));
    }

    /// <summary>Nor has one whose relief has been turned all the way down.</summary>
    [Fact]
    public void NoReliefIsNoFacture()
    {
        MaterialAppearance flat = With(MaterialFinish.Laminate, 4f) with { FinishRelief = 0f };

        Assert.Equal(0f, FinishPeriod.GetCells(flat));
    }

    /// <summary>
    /// Otherwise the period is the dial times the units the tile holds — the dial
    /// measures a <b>unit</b> of the pattern, a board or a panel or a knurl, and
    /// the tile is however many of them the pattern draws.
    /// </summary>
    [Fact]
    public void ThePeriodIsTheDialTimesTheUnitsInTheTile()
    {
        // Four boards to a laminate tile, one panel to a tiled one.
        Assert.Equal(16f, FinishPeriod.GetCells(With(MaterialFinish.Laminate, 4f)));
        Assert.Equal(4f, FinishPeriod.GetCells(With(MaterialFinish.Tiles, 4f)));
    }

    /// <summary>
    /// Which is the whole reason the dial was moved onto the unit: a number that
    /// meant the tile made two materials set alike come out sizes apart, because a
    /// tile holds one panel and six hammer marks. Set alike now, a unit is a unit.
    /// </summary>
    [Fact]
    public void TheSameDialGivesEveryFactureTheSameSizedUnit()
    {
        MaterialFinish[] textured =
        {
            MaterialFinish.Laminate, MaterialFinish.Brick, MaterialFinish.Parquet,
            MaterialFinish.Tiles, MaterialFinish.Mosaic, MaterialFinish.Knurled,
            MaterialFinish.Tread, MaterialFinish.Ribbed, MaterialFinish.Hammered,
            MaterialFinish.GlassWave
        };

        foreach (MaterialFinish finish in textured)
        {
            float period = FinishPeriod.GetCells(With(finish, 2f));
            float units = FinishRecipes.GetRecipe(finish).UnitsPerTile;

            Assert.Equal(2f, period / units, 3);
        }
    }

    /// <summary>
    /// A preview block holds the whole period and one cell over, so the pattern is
    /// seen to start again inside the face rather than at its edge.
    /// </summary>
    [Fact]
    public void APreviewBlockHoldsTheWholePatternAndTheStartOfTheNext()
    {
        // One panel to a tile, so the dial and the period are the same number here.
        Assert.Equal(5, FinishPeriod.GetPreviewCells(With(MaterialFinish.Tiles, 4f)));
        Assert.Equal(9, FinishPeriod.GetPreviewCells(With(MaterialFinish.Tiles, 8f)));

        // Four boards to a tile, so a board of two cells is a tile of eight.
        Assert.Equal(9, FinishPeriod.GetPreviewCells(With(MaterialFinish.Laminate, 2f)));
    }

    /// <summary>
    /// And it stops growing before it becomes more voxels than a preview is worth:
    /// the dial reaches sixteen cells to a unit and a tile can hold six, which is a
    /// block of ninety-six a side and nearly a million cells.
    /// </summary>
    [Fact]
    public void APreviewBlockStopsGrowingAtTheCap()
    {
        Assert.Equal(
            FinishPeriod.MaximumPreviewCells,
            FinishPeriod.GetPreviewCells(With(MaterialFinish.Hammered, 16f)));
    }

    /// <summary>
    /// A fractional period rounds up, or the block would hold less than the pattern
    /// it exists to show.
    /// </summary>
    [Fact]
    public void AFractionalPeriodRoundsUp()
    {
        Assert.Equal(6, FinishPeriod.GetPreviewCells(With(MaterialFinish.Tiles, 4.5f)));
    }

    /// <summary>
    /// Three at the bottom, whatever the facture: a cube smaller than that has no
    /// room to show its own corners, and the occlusion in them is half the point.
    /// The default appearance lands exactly there, so nothing anybody has looked at
    /// moves.
    /// </summary>
    [Fact]
    public void ThreeIsTheFloorAndTheDefaultLandsOnIt()
    {
        Assert.Equal(3, FinishPeriod.GetPreviewCells(With(MaterialFinish.Tiles, 1f)));
        Assert.Equal(3, FinishPeriod.GetPreviewCells(With(MaterialFinish.Rough, 2f)));
        Assert.Equal(3, FinishPeriod.GetPreviewCells(With(MaterialFinish.Knurled, 0.25f)));
        Assert.Equal(3, FinishPeriod.GetPreviewCells(MaterialAppearance.Default));
    }
}
