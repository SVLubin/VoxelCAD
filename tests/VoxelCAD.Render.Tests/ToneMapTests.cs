using Godot;
using VoxelCAD.Render;
using Xunit;

namespace VoxelCAD.Render.Tests;

/// <summary>
/// The curve between accumulated radiance and a pixel. What matters about it is
/// not its shape but that it never inverts and never overflows: a render that
/// clipped to white or went backwards somewhere would read as a bug in the
/// tracer, which is a long way from where it would be.
/// </summary>
public sealed class ToneMapTests
{
    [Fact]
    public void BlackStaysBlack()
    {
        Color mapped = ToneMap.Apply(new Color(0f, 0f, 0f));

        Assert.Equal(0f, mapped.R, 5);
        Assert.Equal(0f, mapped.G, 5);
        Assert.Equal(0f, mapped.B, 5);
    }

    [Fact]
    public void MoreLightIsNeverLessPixel()
    {
        float previous = -1f;

        for (float radiance = 0f; radiance < 40f; radiance += 0.05f)
        {
            float value = ToneMap.Apply(new Color(radiance, radiance, radiance)).R;

            Assert.True(value >= previous, $"{radiance} went backwards.");

            previous = value;
        }
    }

    [Fact]
    public void NothingEverLeavesTheRange()
    {
        for (float radiance = 0f; radiance < 1000f; radiance += 1.3f)
        {
            Color mapped = ToneMap.Apply(new Color(radiance, radiance * 0.5f, radiance * 2f));

            Assert.InRange(mapped.R, 0f, 1f);
            Assert.InRange(mapped.G, 0f, 1f);
            Assert.InRange(mapped.B, 0f, 1f);
        }
    }

    /// <summary>
    /// A highlight rolls off rather than clipping: past the point where a linear
    /// mapping would already be white, more light must still make a difference.
    /// </summary>
    [Fact]
    public void HighlightsRollOffInsteadOfClipping()
    {
        float atOne = ToneMap.Apply(new Color(1f, 1f, 1f)).R;
        float atFour = ToneMap.Apply(new Color(4f, 4f, 4f)).R;

        Assert.True(atOne < 1f, "One unit of radiance should not already be white.");
        Assert.True(atFour > atOne, "Four units should be brighter than one.");
    }

    /// <summary>
    /// A grey stays a grey: the curve is per channel and identical between them,
    /// so nothing acquires a tint on the way to the screen.
    /// </summary>
    [Fact]
    public void AGreyDoesNotAcquireATint()
    {
        Color mapped = ToneMap.Apply(new Color(0.7f, 0.7f, 0.7f));

        Assert.Equal(mapped.R, mapped.G, 6);
        Assert.Equal(mapped.G, mapped.B, 6);
    }
}
