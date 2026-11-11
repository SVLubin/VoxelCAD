using System;
using System.Collections.Generic;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// What a caller writes when it wants a number out of a render rather than a
/// picture. Held here because a rectangle nobody can read is a measurement
/// somewhere other than where the check meant to take it — and because this is the
/// half of that command a test can reach at all, the other half needing a device.
/// </summary>
public sealed class RenderSampleRectTests
{
    [Fact]
    public void OneRectangleIsFourNumbers()
    {
        IReadOnlyList<RenderSampleRect> rects = RenderSampleRect.ParseAll("10,20,30,40");

        Assert.Equal(new RenderSampleRect(10, 20, 30, 40), Assert.Single(rects));
    }

    [Fact]
    public void SeveralAreSeparatedBySemicolons()
    {
        IReadOnlyList<RenderSampleRect> rects = RenderSampleRect.ParseAll("0,0,4,4; 8,8,2,2 ;16,0,1,1");

        Assert.Equal(3, rects.Count);
        Assert.Equal(new RenderSampleRect(8, 8, 2, 2), rects[1]);
    }

    /// <summary>
    /// A trailing separator is how a list gets written by a loop, so it is not an
    /// error — but an empty list is, because it means the caller asked for a
    /// measurement and named nothing to measure.
    /// </summary>
    [Fact]
    public void ATrailingSeparatorIsAllowedAndAnEmptyListIsNot()
    {
        Assert.Single(RenderSampleRect.ParseAll("1,2,3,4;"));
        Assert.Throws<ArgumentException>(() => RenderSampleRect.ParseAll(" ; "));
    }

    [Theory]
    [InlineData("1,2,3")]
    [InlineData("1,2,3,4,5")]
    [InlineData("1,2,3,four")]
    [InlineData("1,2,0,4")]
    [InlineData("1,2,3,-1")]
    public void AnythingElseIsRefusedRatherThanGuessedAt(string text)
    {
        Assert.Throws<ArgumentException>(() => RenderSampleRect.ParseAll(text));
    }

    /// <summary>
    /// It reads back the way the report prints it, so a rectangle in an answer can
    /// be pasted straight into the next question.
    /// </summary>
    [Fact]
    public void ItPrintsTheWayTheReportsDo()
    {
        Assert.Equal("10,20 30x40", new RenderSampleRect(10, 20, 30, 40).ToString());
    }
}
