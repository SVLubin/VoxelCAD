using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The lattice a transform field can actually represent.
/// </summary>
/// <remarks>
/// Godot's Range does not merely step by <c>Step</c>: it rounds the value to
/// <c>MinValue + k * Step</c>. With a minimum of -100000 and a step of 90 that
/// lattice misses zero by 10, so an element whose rotation was zero displayed
/// -10 and one at 90 displayed 80 — the inspector disagreeing with the document
/// while the document was right. These tests reproduce the engine's rule rather
/// than the field, because a Control cannot be built without the engine.
/// </remarks>
public sealed class NumericFieldRangeTests
{
    /// <summary>Godot's own quantisation, from the Range.Step documentation.</summary>
    private static double Quantise(double value, FieldMode mode)
    {
        double step = NumericFieldRange.GetStep(mode);
        double minimum = -NumericFieldRange.GetLimit(mode);

        return Math.Round((value - minimum) / step) * step + minimum;
    }

    [Theory]
    [InlineData(FieldMode.Float)]
    [InlineData(FieldMode.Integer)]
    [InlineData(FieldMode.Snap90)]
    public void TheLimitIsAWholeNumberOfSteps(FieldMode mode)
    {
        double step = NumericFieldRange.GetStep(mode);
        double steps = NumericFieldRange.GetLimit(mode) / step;

        Assert.Equal(Math.Round(steps), steps, 6);
    }

    [Theory]
    [InlineData(FieldMode.Float)]
    [InlineData(FieldMode.Integer)]
    [InlineData(FieldMode.Snap90)]
    public void ZeroIsRepresentableInEveryMode(FieldMode mode) =>
        Assert.Equal(0d, Quantise(0d, mode), 6);

    [Theory]
    [InlineData(90d)]
    [InlineData(180d)]
    [InlineData(-90d)]
    [InlineData(-180d)]
    public void EveryRightAngleIsRepresentableUnderSnap90(double degrees) =>
        Assert.Equal(degrees, Quantise(degrees, FieldMode.Snap90), 6);

    [Theory]
    [InlineData(-16d)]
    [InlineData(7d)]
    [InlineData(-1d)]
    public void EveryWholeNumberIsRepresentableUnderInteger(double value) =>
        Assert.Equal(value, Quantise(value, FieldMode.Integer), 6);
}
