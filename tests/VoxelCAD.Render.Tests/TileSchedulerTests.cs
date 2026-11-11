using System;
using VoxelCAD.Render;
using Xunit;

namespace VoxelCAD.Render.Tests;

/// <summary>
/// Where the next sample goes. Every claim the priority scheme makes is here,
/// because none of them can be read off a picture: a render that served the wrong
/// tiles still finishes, and finishes looking exactly the same.
/// </summary>
public sealed class TileSchedulerTests
{
    /// <summary>
    /// The whole picture is covered once before any of it is covered twice. This
    /// is what keeps a prioritised render from growing out of the middle of the
    /// frame — the first sweep is complete however lopsided the weights are.
    /// </summary>
    [Fact]
    public void TheFirstSweepIsCompleteWhateverTheWeights()
    {
        var scheduler = new TileScheduler(8, 100);
        scheduler.SetWeights(new[] { 8f, 8f, 1f, 1f, 1f, 1f, 1f, 1f });

        var seen = new bool[8];

        for (int i = 0; i < 8; i++)
        {
            Assert.True(scheduler.TryNext(out int tile));
            Assert.False(seen[tile]);

            seen[tile] = true;
            scheduler.Record(tile);
        }

        Assert.Equal(1, scheduler.Minimum);
        Assert.Equal(1, scheduler.Peak);
    }

    /// <summary>
    /// The point of the whole thing: a tile the viewer is showing is served in
    /// proportion to its weight. Measured over a long run rather than asserted
    /// about one pick, because the guarantee is a ratio and not an order.
    /// </summary>
    [Fact]
    public void SamplesArriveInProportionToTheWeights()
    {
        var scheduler = new TileScheduler(4, 100000);
        scheduler.SetWeights(new[] { 8f, 1f, 1f, 1f });

        for (int i = 0; i < 4400; i++)
        {
            Assert.True(scheduler.TryNext(out int tile));
            scheduler.Record(tile);
        }

        // Eight parts of eleven to the boosted tile, one to each of the others.
        Assert.InRange(scheduler.TakenAt(0), 3150, 3250);
        Assert.InRange(scheduler.TakenAt(1), 380, 420);
        Assert.InRange(scheduler.TakenAt(2), 380, 420);
        Assert.InRange(scheduler.TakenAt(3), 380, 420);
    }

    /// <summary>
    /// Nothing off screen is abandoned. A render is a picture somebody will save,
    /// so the part nobody is looking at has to finish too — it simply finishes
    /// later.
    /// </summary>
    [Fact]
    public void NothingIsStarved()
    {
        var scheduler = new TileScheduler(6, 20);
        scheduler.SetWeights(new[] { 8f, 8f, 1f, 1f, 1f, 1f });

        int steps = 0;

        while (scheduler.TryNext(out int tile))
        {
            scheduler.Record(tile);

            Assert.True(++steps < 1000, "The scheduler did not finish.");
        }

        Assert.True(scheduler.IsFinished);

        for (int i = 0; i < 6; i++)
            Assert.Equal(20, scheduler.TakenAt(i));
    }

    /// <summary>
    /// Priority decides the order and never the total, which is the claim that
    /// makes it safe: two renders of the same scene at the same settings are the
    /// same picture whatever was being looked at while they ran.
    /// </summary>
    [Fact]
    public void EveryTileEndsWithWhatWasAskedForHoweverItWasWeighted()
    {
        var even = new TileScheduler(5, 12);
        var lopsided = new TileScheduler(5, 12);
        lopsided.SetWeights(new[] { 1f, 40f, 1f, 1f, 0.001f });

        Drain(even);
        Drain(lopsided);

        for (int i = 0; i < 5; i++)
            Assert.Equal(even.TakenAt(i), lopsided.TakenAt(i));

        Assert.Equal(even.Total, lopsided.Total);
    }

    /// <summary>
    /// Moving the view mid-render keeps every sample already traced. A weighting is
    /// about what to do next; throwing work away for it would make looking around
    /// during a render cost more than it bought.
    /// </summary>
    [Fact]
    public void ReWeighingKeepsWhatHasBeenTraced()
    {
        var scheduler = new TileScheduler(4, 50);

        for (int i = 0; i < 40; i++)
        {
            scheduler.TryNext(out int tile);
            scheduler.Record(tile);
        }

        long before = scheduler.Total;

        scheduler.SetWeights(new[] { 1f, 1f, 8f, 8f });

        Assert.Equal(before, scheduler.Total);
        Assert.Equal(10, scheduler.Minimum);
    }

    /// <summary>
    /// The whole render's progress, which is what a bar is drawn from — every
    /// sample counted, not the least converged tile, or the bar would stand still
    /// while three quarters of the picture finished.
    /// </summary>
    [Fact]
    public void ProgressCountsEverySampleRatherThanTheWorstTile()
    {
        var scheduler = new TileScheduler(4, 10);
        scheduler.SetWeights(new[] { 8f, 1f, 1f, 1f });

        for (int i = 0; i < 20; i++)
        {
            scheduler.TryNext(out int tile);
            scheduler.Record(tile);
        }

        Assert.Equal(0.5f, scheduler.Fraction, 3);
        Assert.True(scheduler.Minimum < scheduler.Peak);
    }

    /// <summary>
    /// Asking for fewer than a tile already has finishes it rather than undoing
    /// anything — a render is never un-traced.
    /// </summary>
    [Fact]
    public void LoweringWhatIsWantedBelowWhatIsDoneFinishesIt()
    {
        var scheduler = new TileScheduler(2, 30);

        for (int i = 0; i < 20; i++)
        {
            scheduler.TryNext(out int tile);
            scheduler.Record(tile);
        }

        scheduler.SetWanted(4);

        Assert.True(scheduler.IsFinished);
        Assert.False(scheduler.TryNext(out _));
        Assert.Equal(10, scheduler.TakenAt(0));
    }

    [Fact]
    public void AWeightPerTileAndNoMore()
    {
        var scheduler = new TileScheduler(3, 10);

        Assert.Throws<ArgumentException>(() => scheduler.SetWeights(new[] { 1f, 1f }));
    }

    /// <summary>
    /// A region that covers the picture, or none at all, is plain round robin —
    /// which is what a render nobody is looking at particularly should do.
    /// </summary>
    [Fact]
    public void NoRegionWeighsEveryTileTheSame()
    {
        var grid = new RenderTileGrid(256, 128);
        float[] weights = TilePriority.Weigh(grid, default);

        Assert.All(weights, weight => Assert.Equal(1f, weight, 3));
    }

    [Fact]
    public void ARegionLiftsWhatIsInsideItAndNothingElse()
    {
        var grid = new RenderTileGrid(256, 128);
        float[] weights = TilePriority.Weigh(grid, new RenderTile(0, 0, 64, 64));

        Assert.Equal(TilePriority.Boost, weights[0], 3);
        Assert.Equal(1f, weights[1], 3);
        Assert.Equal(1f, weights[^1], 3);
    }

    /// <summary>
    /// A pan of one pixel must not re-sort the order for nothing; a real move must.
    /// </summary>
    [Fact]
    public void OnlyARealMoveCountsAsADifferentWeighting()
    {
        var grid = new RenderTileGrid(256, 256);

        float[] was = TilePriority.Weigh(grid, new RenderTile(0, 0, 128, 128));

        Assert.False(TilePriority.Differ(was, TilePriority.Weigh(grid, new RenderTile(0, 0, 128, 128))));
        Assert.True(TilePriority.Differ(was, TilePriority.Weigh(grid, new RenderTile(64, 64, 128, 128))));
    }

    private static void Drain(TileScheduler scheduler)
    {
        while (scheduler.TryNext(out int tile))
            scheduler.Record(tile);
    }
}
