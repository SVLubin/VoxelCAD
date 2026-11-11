using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// How fast this editor moves, and whether it moves at all. One object, held by
/// every motion in the chrome, so a setting changed while a panel is mid-flight
/// reaches that panel rather than the next one built.
/// </summary>
/// <remarks>
/// Handed out by the view's factory for the reason the injected input state is:
/// it belongs to the shell, which is told what the user picked, and to the
/// scene, whose camera turn runs at the same pace — and to neither alone. Passed
/// rather than ambient, so a test builds its own and nothing is shared between
/// them.
/// <para>
/// The numbers live here rather than with the preferences for the reason the
/// shell holds what a view-cube corner means: the file stores the position the
/// user picked, and how slow "slow" is is a look decision.
/// </para>
/// </remarks>
internal sealed class MotionTiming
{
    /// <summary>Whether transitions run at all. Off, everything lands at once.</summary>
    public bool Animate { get; private set; } = true;

    /// <summary>
    /// What a finished duration is multiplied by — <b>the clamps included</b>.
    /// </summary>
    /// <remarks>
    /// Never nought, whatever the setting: "off" is <see cref="Animate"/>,
    /// because a factor of nought would leave <see cref="Smoothed"/> with a
    /// duration of nought, <c>IsSettled</c> true from the first frame and the
    /// value standing where it was for ever. A freeze rather than a crash, and
    /// nothing logged.
    /// </remarks>
    public float DurationFactor { get; private set; } = 1f;

    public void Set(bool enabled, AnimationSpeed speed)
    {
        Animate = enabled;
        DurationFactor = FactorFor(speed);
    }

    /// <summary>
    /// A fixed duration at this pace, for a curve that is safe at nought — the
    /// view cube's turn is, by construction. <see cref="Smoothed"/> is not, and
    /// takes its answer from <see cref="Smoothed.DurationFor"/> and its own snap
    /// branch instead.
    /// </summary>
    public float ScaleFixed(float seconds) => Animate ? seconds * DurationFactor : 0f;

    /// <summary>
    /// Equal ratios rather than equal steps, because a duration is judged
    /// logarithmically: each position is half again as long as the one before.
    /// Anything the enum does not have is the shipped pace — a display setting
    /// must not be able to throw — and a test enumerates the enum, so a member
    /// added tomorrow cannot inherit Fast by silence.
    /// </summary>
    private static float FactorFor(AnimationSpeed speed) =>
        speed switch
        {
            AnimationSpeed.Medium => 1.5f,
            AnimationSpeed.Slow => 2.25f,
            _ => 1f
        };
}
