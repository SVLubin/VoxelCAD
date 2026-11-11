using VoxelCAD.CommonTypes;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Which dials throw a render away. Getting this wrong in one direction loses a
/// finished picture for nothing; in the other it shows two different renders
/// averaged together, which looks like a tracer bug and is not one.
/// </summary>
public sealed class RenderRestartTests
{
    private static readonly RenderDials Any = new(
        Width: 1280,
        Height: 720,
        Samples: 256,
        Exposure: 1f,
        SunElevationDegrees: 70f,
        SunAzimuthDegrees: 120f,
        SunSizeDegrees: 2f,
        Glare: 0.06f,
        Backdrop: RenderBackdrop.Environment,
        Ground: true);

    [Fact]
    public void NothingChangedNeedsNothing()
    {
        Assert.False(RenderRestart.IsNeeded(Any, Any));
    }

    /// <summary>
    /// Exposure is applied when the picture is read, so turning it re-grades what
    /// is already there — which is what lets it be turned while looking at it.
    /// </summary>
    [Fact]
    public void ExposureOnlyRegrades()
    {
        Assert.False(RenderRestart.IsNeeded(Any, Any with { Exposure = 2.5f }));
    }

    /// <summary>Asking for more is asking the same render to carry on.</summary>
    [Fact]
    public void MoreSamplesCarryOn()
    {
        Assert.False(RenderRestart.IsNeeded(Any, Any with { Samples = 4096 }));
    }

    [Fact]
    public void FewerSamplesCarryOnToo()
    {
        Assert.False(RenderRestart.IsNeeded(Any, Any with { Samples = 8 }));
    }

    [Fact]
    public void BothTogetherStillCarryOn()
    {
        Assert.False(RenderRestart.IsNeeded(Any, Any with { Samples = 1024, Exposure = 0.5f }));
    }

    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("elevation")]
    [InlineData("azimuth")]
    [InlineData("size")]
    [InlineData("glare")]
    [InlineData("backdrop")]
    [InlineData("ground")]
    public void EverythingThatChangesThePictureStartsAgain(string dial)
    {
        RenderDials moved = dial switch
        {
            "width" => Any with { Width = 1920 },
            "height" => Any with { Height = 1080 },
            "elevation" => Any with { SunElevationDegrees = 30f },
            "azimuth" => Any with { SunAzimuthDegrees = 200f },
            "size" => Any with { SunSizeDegrees = 12f },
            "glare" => Any with { Glare = 0.2f },
            "backdrop" => Any with { Backdrop = RenderBackdrop.Transparent },
            _ => Any with { Ground = false }
        };

        Assert.True(RenderRestart.IsNeeded(Any, moved));
    }
}
