using System;
using System.Collections.Generic;
using VoxelCAD.Render;
using Xunit;

namespace VoxelCAD.Render.Tests;

/// <summary>
/// How measured noise steers the samples. The measurement itself happens on the
/// device and cannot be reached from here; what is held here is what is done with
/// it, which is the half that decides whether a render spends its time well.
/// </summary>
public sealed class NoiseWeightTests
{
    /// <summary>
    /// The claim the whole thing rests on: a tile twice as noisy as the average of
    /// the picture is sampled about twice as often. Measured over a run rather
    /// than asserted about one pick, because the guarantee is a ratio.
    /// </summary>
    [Fact]
    public void SamplesFollowTheNoise()
    {
        var grid = new RenderTileGrid(128, 128);
        var noise = new float[grid.Count];

        // Four tiles: one settled, two average, one twice the average.
        noise[0] = 0.25f;
        noise[1] = 1f;
        noise[2] = 1f;
        noise[3] = 1.75f;

        var scheduler = new TileScheduler(grid.Count, 100000);
        scheduler.SetWeights(TilePriority.Weigh(grid, default, noise));

        for (int i = 0; i < 4000; i++)
        {
            Assert.True(scheduler.TryNext(out int tile));
            scheduler.Record(tile);
        }

        // The average is one, so the shares are the noises themselves.
        Assert.InRange(scheduler.TakenAt(0), 200, 300);
        Assert.InRange(scheduler.TakenAt(3), 1600, 1800);
        Assert.True(scheduler.TakenAt(3) > scheduler.TakenAt(1) * 1.5);
    }

    /// <summary>
    /// Where somebody is looking and how far from settled a tile is are different
    /// questions, and both are answered: a quiet tile under the eye still outranks
    /// a noisy one off screen.
    /// </summary>
    [Fact]
    public void LookingAtSomethingStillCountsBesideTheNoise()
    {
        var grid = new RenderTileGrid(128, 128);
        var region = new RenderTile(0, 0, 64, 64);

        // Tile 0 is the one on screen and is the quietest of them.
        var noise = new float[grid.Count];
        Array.Fill(noise, 2f);
        noise[0] = 0.5f;

        float[] weights = TilePriority.Weigh(grid, region, noise);

        Assert.True(weights[0] > weights[1]);
    }

    /// <summary>
    /// Bounded either way: one stubborn tile may not take the whole render, and a
    /// quiet one may not be abandoned to it. Without the floor a tile converging
    /// on flat colour would stop being sampled at all, and the promise that every
    /// tile finishes would go with it.
    /// </summary>
    [Fact]
    public void NoTileTakesTheRenderAndNoneIsAbandoned()
    {
        var grid = new RenderTileGrid(128, 128);
        var noise = new float[grid.Count];

        Array.Fill(noise, 0.0001f);
        noise[0] = 1000f;

        float[] weights = TilePriority.Weigh(grid, default, noise);

        Assert.Equal(TilePriority.MostNoiseWeight, weights[0], 3);
        Assert.Equal(TilePriority.LeastNoiseWeight, weights[1], 3);
    }

    /// <summary>
    /// A render nobody has measured yet weighs as it always did — the noise half
    /// is a multiplier of one until there is something to say.
    /// </summary>
    [Fact]
    public void NoMeasurementLeavesTheWeightsAlone()
    {
        var grid = new RenderTileGrid(128, 64);
        var flat = new float[grid.Count];

        Array.Fill(flat, 1f);

        float[] plain = TilePriority.Weigh(grid, default);
        float[] measured = TilePriority.Weigh(grid, default, flat);

        for (int i = 0; i < plain.Length; i++)
            Assert.Equal(plain[i], measured[i], 4);
    }

    /// <summary>
    /// A measurement of the wrong length is not applied at all rather than half
    /// applied, which is what a grid that has just changed size would offer.
    /// </summary>
    [Fact]
    public void AMeasurementOfTheWrongLengthIsIgnored()
    {
        var grid = new RenderTileGrid(128, 128);

        float[] weights = TilePriority.Weigh(grid, default, new float[] { 5f, 5f });

        Assert.All(weights, weight => Assert.Equal(1f, weight, 4));
    }

    /// <summary>
    /// Every tile still finishes, however lopsided the noise was: the weighting
    /// changes the order and never the total.
    /// </summary>
    [Fact]
    public void EveryTileStillFinishes()
    {
        var grid = new RenderTileGrid(128, 128);
        var noise = new float[grid.Count];

        Array.Fill(noise, 0.001f);
        noise[0] = 50f;

        var scheduler = new TileScheduler(grid.Count, 16);
        scheduler.SetWeights(TilePriority.Weigh(grid, default, noise));

        int steps = 0;

        while (scheduler.TryNext(out int tile))
        {
            scheduler.Record(tile);

            Assert.True(++steps < 10000, "The scheduler did not finish.");
        }

        for (int i = 0; i < grid.Count; i++)
            Assert.Equal(16, scheduler.TakenAt(i));
    }
}
