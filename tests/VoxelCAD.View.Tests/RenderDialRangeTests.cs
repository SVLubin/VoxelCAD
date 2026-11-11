using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What the render card's dials can express. Held here because neither of the two
/// ways they could not was visible: a value the field cannot say is silently
/// rounded to one it can, and the card then writes that back to the preferences
/// file — so the defect stores itself and stays.
/// </summary>
public sealed class RenderDialRangeTests
{
    /// <summary>
    /// The general statement, and the one that catches a dial added tomorrow: a
    /// field that cannot hold the value it is given on a fresh install is a field
    /// that has quietly changed the picture before anybody touched it.
    /// </summary>
    [Fact]
    public void EveryDialCanExpressItsOwnDefault()
    {
        foreach ((string name, RenderDialRange range, double value) in RenderDialRanges.All)
        {
            Assert.True(
                range.Holds(value),
                $"The {name} dial cannot express its default of {value} — "
                    + $"it becomes {range.Snap(value)} on a lattice of {range.Step} "
                    + $"from {range.Minimum}.");
        }
    }

    /// <summary>
    /// Why it could not, stated as the rule rather than as the two symptoms: Godot
    /// rounds to <c>Minimum + k · Step</c>, so a minimum off the step's own lattice
    /// shifts every value the dial can hold.
    /// </summary>
    [Fact]
    public void EveryLatticeRunsFromZero()
    {
        foreach ((string name, RenderDialRange range, double _) in RenderDialRanges.All)
        {
            Assert.True(
                range.IsAnchored,
                $"The {name} dial's minimum of {range.Minimum} is not a whole number "
                    + $"of its {range.Step} step, so its whole lattice is shifted.");
        }
    }

    /// <summary>
    /// The exposure defect itself. It ran from 0.01 by 0.05, so <em>no</em> round
    /// number was reachable — a stored 1.0 came back as 1.01, and `ui` on the
    /// running editor read 1.0099999997764826 off the live card.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    public void ExposureCanBeARoundNumber(double wanted)
    {
        Assert.True(
            RenderDialRanges.Exposure.Holds(wanted),
            $"An exposure of {wanted} becomes {RenderDialRanges.Exposure.Snap(wanted)}.");
    }

    /// <summary>
    /// And the size defect: a step of 16 meant every size had to be a multiple of
    /// it, so the card could not render Full HD — 1080 became 1088.
    /// </summary>
    [Theory]
    [InlineData(1080)]
    [InlineData(1920)]
    [InlineData(1200)]
    [InlineData(1000)]
    [InlineData(900)]
    [InlineData(675)]
    public void AnyWholeSizeTheRendererAcceptsCanBeAskedFor(int size)
    {
        Assert.True(RenderDialRanges.Width.Holds(size), $"width {size}");
        Assert.True(RenderDialRanges.Height.Holds(size), $"height {size}");
    }

    /// <summary>
    /// A dial must reach as far as the renderer does, or the form refuses what the
    /// command allows and a value set over the wire is clamped back on the next
    /// open. Three of them did not: exposure stopped at 8 of 100, focus at 999,
    /// and the azimuth's turn was written down rather than named.
    /// </summary>
    [Fact]
    public void TheDialsReachAsFarAsTheRendererDoes()
    {
        Assert.Equal(RenderLimits.MinimumSize, RenderDialRanges.Width.Minimum);
        Assert.Equal(RenderLimits.MaximumSize, RenderDialRanges.Width.Maximum);
        Assert.Equal(RenderLimits.MinimumExposure, RenderDialRanges.Exposure.Minimum);
        Assert.Equal(RenderLimits.MaximumExposure, RenderDialRanges.Exposure.Maximum);
        Assert.Equal(RenderLimits.MaximumAzimuth, RenderDialRanges.SunAzimuth.Maximum);
        Assert.Equal(RenderLimits.MaximumGlare, RenderDialRanges.Glare.Maximum);
    }

    /// <summary>
    /// The engine's rounding restated, checked against the arithmetic that
    /// diagnosed the live card: on the shipped exposure range every value was a
    /// hundredth over, because the lattice began at the minimum.
    /// </summary>
    [Fact]
    public void SnapIsGodotsOwnRoundingAndCatchesTheShippedRange()
    {
        var shipped = new RenderDialRange(0.01, 8.0, 0.05);

        Assert.Equal(1.01, shipped.Snap(1.0), 6);
        Assert.Equal(0.51, shipped.Snap(0.5), 6);
        Assert.False(shipped.IsAnchored);

        var shippedSize = new RenderDialRange(64, 4096, 16);

        Assert.Equal(1088, shippedSize.Snap(1080), 6);

        // Anchored — the size lattice really did run from zero — which is why the
        // minimum alone was not the whole rule: a step of 16 cannot say 1080
        // however well it is anchored.
        Assert.True(shippedSize.IsAnchored);
        Assert.False(shippedSize.Holds(1080));
    }

    /// <summary>A value outside the bounds is not held, whatever the lattice says.</summary>
    [Fact]
    public void NothingOutsideTheBoundsIsHeld()
    {
        Assert.False(RenderDialRanges.Width.Holds(32));
        Assert.False(RenderDialRanges.Width.Holds(8192));
        Assert.False(RenderDialRanges.Glare.Holds(0.5));
    }
}
