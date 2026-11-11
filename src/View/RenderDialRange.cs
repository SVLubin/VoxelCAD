using System;

namespace VoxelCAD.View;

/// <summary>
/// What one render dial offers: the bounds it is held to and how finely it moves.
/// </summary>
/// <remarks>
/// <para>
/// It exists because a <see cref="Godot.SpinBox"/> does not merely step by its
/// step — Godot's <c>Range</c> rounds every value to <c>Minimum + k · Step</c>,
/// counting from the minimum rather than from zero. So a minimum that is not a
/// whole number of steps shifts the <em>entire</em> lattice, and a step that does
/// not divide what somebody wants makes it unsayable. Both shipped: exposure ran
/// from 0.01 by 0.05, so 1.0 became 1.01 and no round number was reachable at
/// all; width and height stepped by 16, so 1080 became 1088 and the card could
/// not render Full HD.
/// </para>
/// <para>
/// Neither was visible. The card writes what the box holds straight back to the
/// preferences file, so the stored exposure was quietly walked off its own
/// default and stayed there.
/// </para>
/// <para>
/// The rule that removes both: <b>the lattice runs from zero</b> — the minimum is
/// a whole number of steps — <b>and the step divides what the model accepts</b>,
/// which for a dial holding whole numbers means a step of one. It is arithmetic
/// rather than layout, so it lives out here where a test can reach it, the same
/// way <see cref="NumericFieldRange"/> does for the transform fields.
/// </para>
/// </remarks>
internal readonly record struct RenderDialRange(double Minimum, double Maximum, double Step)
{
    /// <summary>
    /// How near a value has to be to the lattice to count as on it. Relative to
    /// the step, because these are doubles carrying a float's worth of noise by
    /// the time they have been through a preferences file.
    /// </summary>
    private const double Tolerance = 1.0e-6;

    /// <summary>
    /// What Godot would make of this value: rounded to the lattice, then held
    /// inside the bounds. Restated here so a test can ask the same question the
    /// engine answers, without an engine.
    /// </summary>
    public double Snap(double value)
    {
        if (Step <= 0d)
            return Math.Clamp(value, Minimum, Maximum);

        double snapped = Minimum + Math.Round((value - Minimum) / Step) * Step;

        return Math.Clamp(snapped, Minimum, Maximum);
    }

    /// <summary>Whether the field can express this value exactly.</summary>
    public bool Holds(double value) =>
        value >= Minimum - Step * Tolerance
        && value <= Maximum + Step * Tolerance
        && Math.Abs(Snap(value) - value) <= Step * Tolerance;

    /// <summary>
    /// Whether the lattice runs from zero rather than from wherever the minimum
    /// happens to be. The property that makes round numbers reachable, and the one
    /// the exposure dial did not have.
    /// </summary>
    public bool IsAnchored =>
        Step <= 0d || Math.Abs(Minimum / Step - Math.Round(Minimum / Step)) <= Tolerance;
}
