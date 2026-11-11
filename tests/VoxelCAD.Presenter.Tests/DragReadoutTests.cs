using Godot;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The line a drag shows while it is in flight. It exists for one case — a
/// quantised channel where the element has not moved yet — so what is asserted
/// here is that the pending part appears, says which way, and goes away again.
/// </summary>
public sealed class DragReadoutTests
{
    private const int X = 1;
    private const int Y = 2;
    private const int Z = 4;

    /// <summary>
    /// The case the readout is for: the pointer is a third of a cell along, the model
    /// still says four, and the line says both so the user knows to keep pulling.
    /// </summary>
    [Fact]
    public void APendingOffsetIsShownBesideTheAppliedValue()
    {
        string line = DragReadoutHelper.Format(
            TransformChannel.Position,
            FieldMode.Integer,
            new Vector3(4f, 0f, 0f),
            new Vector3(4.3f, 0f, 0f),
            X);

        Assert.Equal("X 4 (+0.3)", line);
    }

    [Fact]
    public void PullingTheOtherWayShowsASignedOffset()
    {
        string line = DragReadoutHelper.Format(
            TransformChannel.Position,
            FieldMode.Integer,
            new Vector3(4f, 0f, 0f),
            new Vector3(3.7f, 0f, 0f),
            X);

        Assert.Equal("X 4 (-0.3)", line);
    }

    /// <summary>
    /// Sitting on the value is not pulling away from it, so there is nothing pending
    /// to report — the readout still says where the element is.
    /// </summary>
    [Fact]
    public void NothingPendingLeavesTheValueAlone()
    {
        string line = DragReadoutHelper.Format(
            TransformChannel.Position,
            FieldMode.Integer,
            new Vector3(4f, 0f, 0f),
            new Vector3(4f, 0f, 0f),
            X);

        Assert.Equal("X 4", line);
    }

    /// <summary>A plane drag reports both of its axes and stays quiet about the third.</summary>
    [Fact]
    public void APlaneDragReportsTwoAxes()
    {
        string line = DragReadoutHelper.Format(
            TransformChannel.Position,
            FieldMode.Integer,
            new Vector3(4f, 9f, 1f),
            new Vector3(4.3f, 9f, 1.6f),
            X | Z);

        Assert.Equal("X 4 (+0.3)   Z 1 (+0.6)", line);
    }

    /// <summary>Before anything has been pulled there is nothing to say, and an empty line hides it.</summary>
    [Fact]
    public void NoAxisMeansNoLine()
    {
        Assert.Equal(
            string.Empty,
            DragReadoutHelper.Format(
                TransformChannel.Position,
                FieldMode.Integer,
                Vector3.Zero,
                new Vector3(0.4f, 0f, 0f),
                axisMask: 0));
    }

    /// <summary>Degrees are marked as such, on both halves of the pair.</summary>
    [Fact]
    public void RotationIsShownInDegrees()
    {
        string line = DragReadoutHelper.Format(
            TransformChannel.Rotation,
            FieldMode.Snap90,
            new Vector3(0f, 90f, 0f),
            new Vector3(0f, 122f, 0f),
            Y);

        Assert.Equal("Y 90° (+32.0°)", line);
    }

    /// <summary>
    /// Under Float the model keeps what it is given, so there is never anything
    /// pending — and the value is shown at the precision the channel actually holds
    /// rather than rounded to a cell it does not snap to.
    /// </summary>
    [Fact]
    public void AFloatChannelShowsItsDecimals()
    {
        string line = DragReadoutHelper.Format(
            TransformChannel.Position,
            FieldMode.Float,
            new Vector3(4.35f, 0f, 0f),
            new Vector3(4.35f, 0f, 0f),
            X);

        Assert.Equal("X 4.35", line);
    }

    /// <summary>
    /// A quantised value is shown as the whole number it is: two decimals would
    /// suggest a precision the channel does not have.
    /// </summary>
    [Fact]
    public void AQuantisedValueShowsNoDecimals()
    {
        string line = DragReadoutHelper.Format(
            TransformChannel.Scale,
            FieldMode.Integer,
            new Vector3(0f, 0f, 2f),
            new Vector3(0f, 0f, 2f),
            Z);

        Assert.Equal("Z 2", line);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 0)]
    [InlineData(1f, 0f, 0f, X)]
    [InlineData(0f, 0f, -2f, Z)]
    [InlineData(1f, 0f, 3f, X | Z)]
    public void TheAxisMaskNamesTheAxesADeltaMoved(float x, float y, float z, int expected)
    {
        Assert.Equal(expected, DragReadoutHelper.GetAxisMask(new Vector3(x, y, z)));
    }
}
