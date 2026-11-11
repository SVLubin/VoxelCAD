using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The step and the limits a transform field offers for one field mode.
/// </summary>
/// <remarks>
/// This exists because Godot's <c>Range</c> does not merely step by
/// <c>Step</c> — it rounds the value to <c>MinValue + k * Step</c>, counting
/// from the minimum rather than from zero. A minimum that is not a whole number
/// of steps therefore shifts the entire lattice: at step 90 against a minimum of
/// -100000 the representable values are ..., -100, -10, 80, ..., so an element
/// whose rotation really is zero showed -10 in the inspector, and 90 showed 80.
/// Keeping the limit a whole number of steps is the fix, and it is arithmetic
/// rather than layout, so it lives out here where a test can reach it.
/// </remarks>
internal static class NumericFieldRange
{
    /// <summary>How far a field reaches before the step is taken into account.</summary>
    private const double NominalLimit = 100000.0;

    public static double GetStep(FieldMode mode) => mode switch
    {
        FieldMode.Integer => 1.0,
        FieldMode.Snap90 => 90.0,
        _ => 0.01
    };

    /// <summary>
    /// The largest whole number of steps within <see cref="NominalLimit"/>, so
    /// that the field's own minimum is a multiple of its step and zero is always
    /// representable.
    /// </summary>
    public static double GetLimit(FieldMode mode)
    {
        double step = GetStep(mode);

        return Math.Floor(NominalLimit / step) * step;
    }
}
