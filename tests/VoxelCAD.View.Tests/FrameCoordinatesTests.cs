using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The contract between a captured image and the space input is aimed in. Getting
/// this wrong is silent: every click lands somewhere plausible and wrong.
/// </summary>
public sealed class FrameCoordinatesTests
{
    private static readonly Vector2I Window = new(1489, 649);

    /// <summary>
    /// The whole frame at full size is one to one, which is the case the window
    /// capture is normally read in.
    /// </summary>
    [Fact]
    public void AFullFrameMapsOneToOne()
    {
        (Vector2 origin, Vector2 scale) = FrameCoordinates.Resolve(
            Window,
            Window,
            new Rect2I(Vector2I.Zero, Window),
            Window);

        Assert.Equal(Vector2.Zero, origin);
        Assert.Equal(1f, scale.X, 4);
        Assert.Equal(1f, scale.Y, 4);
    }

    [Fact]
    public void ADownscaleHalvesTheScaleAndLeavesTheOriginAlone()
    {
        (Vector2 origin, Vector2 scale) = FrameCoordinates.Resolve(
            Window,
            Window,
            new Rect2I(Vector2I.Zero, Window),
            new Vector2I(744, 324));

        Assert.Equal(Vector2.Zero, origin);
        Assert.Equal(0.5f, scale.X, 2);
        Assert.Equal(0.5f, scale.Y, 2);
    }

    [Fact]
    public void ACropMovesTheOriginToTheCropCorner()
    {
        (Vector2 origin, Vector2 scale) = FrameCoordinates.Resolve(
            Window,
            Window,
            new Rect2I(100, 40, 200, 120),
            new Vector2I(200, 120));

        Assert.Equal(new Vector2(100f, 40f), origin);
        Assert.Equal(1f, scale.X, 4);
    }

    /// <summary>
    /// A crop and a downscale together: the origin is where the crop started, the
    /// scale is what the shrink left, and the two compose so that
    /// <c>origin + pixel / scale</c> is still the point that was aimed at.
    /// </summary>
    [Fact]
    public void ACropAndADownscaleCompose()
    {
        (Vector2 origin, Vector2 scale) = FrameCoordinates.Resolve(
            Window,
            Window,
            new Rect2I(100, 40, 200, 120),
            new Vector2I(100, 60));

        Assert.Equal(new Vector2(100f, 40f), origin);
        Assert.Equal(0.5f, scale.X, 4);

        // The centre pixel of the returned image is the centre of the crop.
        Assert.Equal(200f, origin.X + 50f / scale.X, 3);
        Assert.Equal(100f, origin.Y + 30f / scale.Y, 3);
    }

    /// <summary>
    /// The case the whole struct exists for: a window whose pixels outnumber the
    /// canvas units a control's rect is measured in. Measured at 2.58 on this
    /// project, which is the difference between clicking a button and clicking the
    /// dock behind it.
    /// </summary>
    [Fact]
    public void PixelsPerInputUnitAboveOneIsCarriedRatherThanAssumedAway()
    {
        var pixels = new Vector2I(2972, 1783);
        var input = new Vector2I(2972, 1783);

        (Vector2 origin, Vector2 scale) = FrameCoordinates.Resolve(
            pixels,
            input,
            new Rect2I(Vector2I.Zero, pixels),
            new Vector2I(1486, 891));

        Assert.Equal(Vector2.Zero, origin);
        Assert.Equal(0.5f, scale.X, 3);
    }

    /// <summary>A degenerate frame answers rather than throwing; nobody can click in it anyway.</summary>
    [Fact]
    public void AZeroSizedWindowDoesNotDivideByZero()
    {
        (Vector2 origin, Vector2 scale) = FrameCoordinates.Resolve(
            Vector2I.Zero,
            Vector2I.Zero,
            new Rect2I(Vector2I.Zero, Vector2I.Zero),
            Vector2I.Zero);

        Assert.True(float.IsFinite(origin.X));
        Assert.True(float.IsFinite(scale.X));
    }
}
