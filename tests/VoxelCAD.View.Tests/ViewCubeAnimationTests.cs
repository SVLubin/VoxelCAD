using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The eased turn a view-cube press starts. Stepped with a number rather than a
/// clock, which is the whole reason it can be checked at all.
/// </summary>
public sealed class ViewCubeAnimationTests
{
    private const float Duration = 1f;

    private static ViewCubeAnimation Create(float startYaw, float targetYaw) =>
        new(startYaw, 0f, targetYaw, 0f, Duration);

    [Fact]
    public void ATurnStartsWhereItWasAskedTo()
    {
        var animation = new ViewCubeAnimation(30f, -20f, 90f, 0f, Duration);

        Assert.Equal(30f, animation.YawDegrees, 3);
        Assert.Equal(-20f, animation.PitchDegrees, 3);
        Assert.False(animation.IsFinished);
    }

    [Fact]
    public void ATurnEndsExactlyOnItsTarget()
    {
        var animation = new ViewCubeAnimation(30f, -20f, 90f, 45f, Duration);

        animation.Advance(Duration);

        Assert.Equal(90f, animation.YawDegrees, 3);
        Assert.Equal(45f, animation.PitchDegrees, 3);
        Assert.True(animation.IsFinished);
    }

    /// <summary>
    /// Half way through in time is half way round in angle — smoothstep is
    /// symmetric — and it is not finished until the whole duration has passed.
    /// </summary>
    [Fact]
    public void HalfWayThroughIsHalfWayRound()
    {
        ViewCubeAnimation animation = Create(0f, 100f);

        animation.Advance(Duration * 0.5);

        Assert.Equal(50f, animation.YawDegrees, 3);
        Assert.False(animation.IsFinished);
    }

    /// <summary>
    /// Eased at both ends: the first tenth of the time covers less than a tenth of
    /// the angle, and so does the last. That is what stops the turn jerking into
    /// motion and stopping dead.
    /// </summary>
    [Fact]
    public void TheTurnLeavesAndArrivesSlowly()
    {
        ViewCubeAnimation early = Create(0f, 100f);
        early.Advance(Duration * 0.1);

        Assert.True(early.YawDegrees < 10f, $"{early.YawDegrees} should be under a tenth");
        Assert.True(early.YawDegrees > 0f);

        ViewCubeAnimation late = Create(0f, 100f);
        late.Advance(Duration * 0.9);

        Assert.True(late.YawDegrees > 90f, $"{late.YawDegrees} should be past nine tenths");
        Assert.True(late.YawDegrees < 100f);
    }

    [Fact]
    public void AdvancingPastTheEndDoesNotOvershoot()
    {
        ViewCubeAnimation animation = Create(0f, 90f);

        animation.Advance(Duration * 5);

        Assert.Equal(90f, animation.YawDegrees, 3);
        Assert.True(animation.IsFinished);
    }

    /// <summary>
    /// A cancelled turn is one nobody advances again: the last angles it wrote stay
    /// where they are, which is "leave the camera where the animation had reached"
    /// rather than a snap to either end.
    /// </summary>
    [Fact]
    public void GivingUpPartWayLeavesThePartWayValue()
    {
        ViewCubeAnimation animation = Create(0f, 100f);

        animation.Advance(Duration * 0.5);
        float abandoned = animation.YawDegrees;

        Assert.NotEqual(0f, abandoned, 3);
        Assert.NotEqual(100f, abandoned, 3);
        Assert.Equal(abandoned, animation.YawDegrees, 3);
    }

    /// <summary>The short way round: forward 20°, not back 340°.</summary>
    [Fact]
    public void YawTakesTheShortWayRound()
    {
        ViewCubeAnimation animation = Create(350f, 10f);

        animation.Advance(Duration);

        Assert.Equal(370f, animation.YawDegrees, 3);
    }

    [Fact]
    public void TheShortWayIsTakenInBothDirections()
    {
        ViewCubeAnimation animation = Create(10f, 350f);

        animation.Advance(Duration);

        Assert.Equal(-10f, animation.YawDegrees, 3);
    }

    /// <summary>
    /// A half turn is the same distance either way, so the tie is broken forwards —
    /// and broken the same way whichever end it is asked from, rather than flipping
    /// on a rounding error.
    /// </summary>
    [Fact]
    public void AHalfTurnAlwaysGoesForwards()
    {
        ViewCubeAnimation forwards = Create(10f, 190f);
        forwards.Advance(Duration);

        ViewCubeAnimation backwards = Create(190f, 10f);
        backwards.Advance(Duration);

        Assert.Equal(190f, forwards.YawDegrees, 3);
        Assert.Equal(370f, backwards.YawDegrees, 3);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(20f, 20f)]
    [InlineData(-340f, 20f)]
    [InlineData(340f, -20f)]
    [InlineData(180f, 180f)]
    [InlineData(-180f, 180f)]
    [InlineData(540f, 180f)]
    public void WrappingBringsAnAngleIntoOneTurn(float degrees, float expected)
    {
        Assert.Equal(expected, ViewCubeAnimation.WrapDegrees(degrees), 3);
    }

    /// <summary>A zero duration is finished at once rather than dividing by nothing.</summary>
    [Fact]
    public void AZeroDurationTurnIsAlreadyOver()
    {
        var animation = new ViewCubeAnimation(0f, 0f, 90f, 30f, 0f);

        animation.Advance(0.0);

        Assert.True(animation.IsFinished);
        Assert.Equal(90f, animation.YawDegrees, 3);
        Assert.Equal(30f, animation.PitchDegrees, 3);
    }
}
