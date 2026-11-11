using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// How hard each edge of a scrolling body dissolves. The rule the whole thing
/// rests on is that it follows what is actually cut, not how close a row happens
/// to be to the clip line — so a body at rest has no fade at its top however
/// small its own margin is.
/// </summary>
public sealed class ScrollFadeEdgesTests
{
    [Fact]
    public void ABodyThatDoesNotScrollFadesAtNeitherEnd() =>
        Assert.Equal(ScrollFadeStrength.None, ScrollFadeEdges.Solve(new ScrollExtent(0f, 0f)));

    /// <summary>
    /// The half of it the request was actually about: nothing is above the top
    /// yet, so nothing dissolves there, whatever the content's own inset is.
    /// </summary>
    [Fact]
    public void AtRestTheTopIsWhole()
    {
        ScrollFadeStrength strength = ScrollFadeEdges.Solve(new ScrollExtent(0f, 400f));

        Assert.Equal(0f, strength.Top);
        Assert.Equal(1f, strength.Bottom);
    }

    [Fact]
    public void ScrolledToTheEndTheBottomIsWhole()
    {
        ScrollFadeStrength strength = ScrollFadeEdges.Solve(new ScrollExtent(400f, 400f));

        Assert.Equal(1f, strength.Top);
        Assert.Equal(0f, strength.Bottom);
    }

    [Fact]
    public void InTheMiddleBothEndsDissolveFully()
    {
        ScrollFadeStrength strength = ScrollFadeEdges.Solve(new ScrollExtent(200f, 400f));

        Assert.Equal(1f, strength.Top);
        Assert.Equal(1f, strength.Bottom);
    }

    /// <summary>
    /// The ramp: a first pixel of scroll must not bring the dissolve in at full
    /// strength, which would be a hard edge arriving as another one left.
    /// </summary>
    [Fact]
    public void TheFirstUnitsOfScrollBringItInGradually()
    {
        ScrollFadeStrength strength =
            ScrollFadeEdges.Solve(new ScrollExtent(ScrollFadeEdges.Onset / 2f, 400f));

        Assert.Equal(0.5f, strength.Top, 3);
    }

    /// <summary>An offset outside the range is the range's own end, not a negative fade.</summary>
    [Fact]
    public void AnOverscrolledOffsetIsClamped()
    {
        Assert.Equal(0f, ScrollFadeEdges.Solve(new ScrollExtent(-50f, 400f)).Top);
        Assert.Equal(0f, ScrollFadeEdges.Solve(new ScrollExtent(900f, 400f)).Bottom);
    }
}
