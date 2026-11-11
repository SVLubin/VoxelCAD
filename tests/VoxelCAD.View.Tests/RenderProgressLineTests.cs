using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What the line under a render says. Wording, which is a decision — and one that
/// has to hold two honest answers at once when part of the picture is being served
/// first.
/// </summary>
public sealed class RenderProgressLineTests
{
    [Fact]
    public void ARunningRenderSaysHowFarAlongAndHowMuchLonger()
    {
        var progress = new RenderProgress(120, 120, 512, 0.234f, 6.1d, 20d, 0f, false);

        Assert.Equal("120 of 512 samples · 6.1 s · about 20 s left", RenderProgressLine.Describe(progress));
    }

    /// <summary>
    /// The part being looked at is named only when it is ahead. A render nobody has
    /// zoomed into converges evenly and there is nothing to say; one being aimed at
    /// has two honest answers, and showing only the lower would make a visibly
    /// clean corner read as noisy.
    /// </summary>
    [Fact]
    public void ThePartInViewIsNamedOnlyWhenItIsAhead()
    {
        var even = new RenderProgress(120, 120, 512, 0.234f, 6.1d, 20d, 0f, false);
        var aimed = new RenderProgress(120, 960, 512, 0.4f, 6.1d, 20d, 0f, false);

        Assert.DoesNotContain("in view", RenderProgressLine.Describe(even));
        Assert.Contains("960 in view", RenderProgressLine.Describe(aimed));
    }

    [Fact]
    public void AFinishedRenderSaysWhatItCostAndNothingAboutWhatIsLeft()
    {
        var progress = new RenderProgress(512, 512, 512, 1f, 41.2d, 12d, 0f, true);

        Assert.Equal("512 samples · 41 s", RenderProgressLine.Describe(progress));
    }

    /// <summary>
    /// A render that has not taken a sample yet has no rate, and putting a number
    /// on screen that is about to be wrong is worse than leaving it out.
    /// </summary>
    [Fact]
    public void NothingIsEstimatedBeforeThereIsARate()
    {
        var progress = new RenderProgress(0, 0, 512, 0f, 0.2d, 0d, 0f, false);

        Assert.Equal("0 of 512 samples · 0.2 s", RenderProgressLine.Describe(progress));
    }

    [Fact]
    public void ARenderNobodyHasAskedForSaysNothing()
    {
        Assert.Equal(string.Empty, RenderProgressLine.Describe(RenderProgress.None));
    }

    /// <summary>
    /// Tenths while it is seconds, whole seconds past ten, minutes past a minute and
    /// hours past an hour — a render reported to a tenth of a second after four
    /// minutes is digits nobody reads, and one estimated at "1666 m 40 s" is the
    /// same complaint one unit up.
    /// </summary>
    /// <remarks>
    /// Every boundary is decided by what would be <em>printed</em> rather than by
    /// the raw value: 9.96 rounds to 10.0, and a span a hair under the hour whose
    /// seconds round up is an hour rather than "60 m".
    /// </remarks>
    [Theory]
    [InlineData(0d, "0.0 s")]
    [InlineData(3.14d, "3.1 s")]
    [InlineData(9.96d, "10 s")]
    [InlineData(41.4d, "41 s")]
    [InlineData(60d, "1 m")]
    [InlineData(95d, "1 m 35 s")]
    [InlineData(3599.6d, "1 h")]
    [InlineData(3600d, "1 h")]
    [InlineData(4230d, "1 h 11 m")]
    [InlineData(3599.4d, "59 m 59 s")]
    [InlineData(200000d, "55 h 33 m")]
    public void TimeIsSaidAtThePrecisionItIsWorthReadingAt(double seconds, string expected)
    {
        Assert.Equal(expected, RenderProgressLine.Duration(seconds));
    }

    /// <summary>
    /// What is left comes off the recent rate, so that a render which sped up is
    /// not still being estimated from the seconds it spent starting.
    /// </summary>
    [Fact]
    public void WhatIsLeftComesOffTheRateRatherThanTheAverage()
    {
        var slowStart = new RenderProgress(100, 100, 1000, 0.1f, 100d, 10d, 0f, false);

        // Nine hundred samples to go at ten a second, whatever the first hundred
        // cost.
        Assert.Equal(90d, slowStart.RemainingSeconds, 3);
    }

    [Fact]
    public void AFinishedRenderHasNothingLeft()
    {
        Assert.True(new RenderProgress(512, 512, 512, 1f, 10d, 50d, 0f, true).RemainingSeconds < 0d);
    }
}
