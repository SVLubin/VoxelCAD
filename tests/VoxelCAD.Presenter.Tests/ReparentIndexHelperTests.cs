using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Where a dropped row lands, once it has left the place it was in. One case
/// differs from the identity, and only one.
/// </summary>
public sealed class ReparentIndexHelperTests
{
    [Fact]
    public void IntoAnotherParentTheDropIndexIsTheAnswer()
    {
        Assert.Equal(0, ReparentIndexHelper.Calculate(0, sameParent: false, currentIndex: 2));
        Assert.Equal(3, ReparentIndexHelper.Calculate(3, sameParent: false, currentIndex: 0));
    }

    /// <summary>
    /// Moving down inside its own parent, the row takes a place out from under the
    /// target as it goes — [A,B,C] with A dropped below C asks for 3 and means 2.
    /// </summary>
    [Fact]
    public void MovingDownInsideOneParentCountsTheGapItLeaves()
    {
        Assert.Equal(2, ReparentIndexHelper.Calculate(3, sameParent: true, currentIndex: 0));
        Assert.Equal(1, ReparentIndexHelper.Calculate(2, sameParent: true, currentIndex: 0));
    }

    [Fact]
    public void MovingUpInsideOneParentTakesNothingOutFromUnderTheTarget()
    {
        Assert.Equal(0, ReparentIndexHelper.Calculate(0, sameParent: true, currentIndex: 2));
        Assert.Equal(1, ReparentIndexHelper.Calculate(1, sameParent: true, currentIndex: 2));
    }

    /// <summary>Dropped exactly where it already is: nothing to adjust either way.</summary>
    [Fact]
    public void StayingPutIsAnIdentity()
    {
        Assert.Equal(1, ReparentIndexHelper.Calculate(1, sameParent: true, currentIndex: 1));
    }
}
