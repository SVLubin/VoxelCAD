using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The dimmer's one decision: solid to the machine everywhere, open to the
/// agent in one place and only while a gesture is being dispatched. Solid is
/// what keeps hover and the cursor shape off the suspended panels; the hole is
/// what lets an injected event land, because the injector parks the pointer
/// wherever it is about to act. Neither is what refuses the user's clicks —
/// that is decided by origin, in <see cref="InputSuspension"/>.
/// </summary>
public sealed class DimmerHitTestTests
{
    private static readonly Vector2 Pointer = new(640, 360);

    [Fact]
    public void WithNoAgentPointerTheDimmerIsSolid()
    {
        Assert.True(DimmerHitTest.Contains(new Vector2(100, 100), null, gestureInFlight: true));
        Assert.True(DimmerHitTest.Contains(Vector2.Zero, null, gestureInFlight: true));
    }

    [Fact]
    public void DuringAGestureTheDimmerOpensAtTheAgentsPointer()
    {
        Assert.False(DimmerHitTest.Contains(Pointer, Pointer, gestureInFlight: true));
        Assert.False(DimmerHitTest.Contains(Pointer + new Vector2(1, 1), Pointer, gestureInFlight: true));
    }

    [Fact]
    public void AwayFromThePointerTheDimmerStaysSolid()
    {
        Assert.True(DimmerHitTest.Contains(
            Pointer + new Vector2(DimmerHitTest.HoleRadius + 1, 0), Pointer, gestureInFlight: true));
        Assert.True(DimmerHitTest.Contains(new Vector2(10, 10), Pointer, gestureInFlight: true));
    }

    /// <summary>
    /// Between gestures there is no hole at all, pointer or not: an agent that
    /// has stopped injecting — thinking, or working over the semantic surface —
    /// leaves solid glass, even though its pointer stays put for the brush's
    /// preview to read.
    /// </summary>
    [Fact]
    public void BetweenGesturesTheDimmerIsSolidEvenAtThePointer()
    {
        Assert.True(DimmerHitTest.Contains(Pointer, Pointer, gestureInFlight: false));
    }
}
