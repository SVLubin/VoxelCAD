using System;

namespace VoxelCAD.View;

/// <summary>
/// A number that moves towards whatever it is last told, easing as it arrives,
/// at a speed rather than over a duration. Every animated quantity in the chrome
/// is one of these — a panel's edges, a column's width, how far the notch
/// reaches, how solid a hint is — so nothing in the layout has to know how
/// motion works.
/// </summary>
/// <remarks>
/// A sine ease-out, over a duration derived from how far there is to go. The
/// three parts of that are chosen for different reasons.
/// <list type="bullet">
/// <item><b>Timed, because an exponential has no end.</b> The approach this
/// replaced closed a fraction of the remaining gap per frame, so it never
/// arrived and had to be cut off at a threshold. The last few units crept, and
/// the eye caught on them: the last thing a panel uncovers is the first letter
/// of its own title, and that letter faded in over several frames after the
/// motion was visually over. A curve that reaches its target <em>at</em> a time
/// lands cleanly and stops.</item>
/// <item><b>Ease-out rather than ease-in-out, because the start must stay
/// sharp.</b> The exponential's one virtue was that it left instantly; a
/// symmetric curve creeps away from the tab and reads as sluggish. Sine-out
/// keeps most of the speed at the front and still arrives with its velocity at
/// zero — 70.7% of the ground is covered in the first half of the time.</item>
/// <item><b>Distance over speed rather than one duration, because a duration
/// makes the launch a function of how far the thing happens to be going.</b>
/// Under a fixed 0.12 s a panel opening covered 328 units and the tabs below it
/// reordered by 36, side by side during the same drag, at a nine-fold difference
/// in pace. And a reversal cost a whole transition for whatever little was left:
/// one frame into an open, coming home took eight more frames to travel the
/// ground one frame had covered. With the duration derived from the distance
/// both leave at the same speed and simply stop at different times, and a
/// reversal costs what is left because what is left <em>is</em> the distance.
/// Sine-out gains from this rather than losing: its launch speed is π/2 times
/// the constant for every distance instead of being proportional to it.</item>
/// </list>
/// Retargeting is still the normal case and still costs nothing: a new target
/// restarts from wherever the value <em>stands</em>, so the position never
/// jumps, and the curve is monotonic between the two, so there is no overshoot
/// however quickly a hand flips it. What is new is that it also restarts with a
/// clock sized for the ground that is actually left.
/// </remarks>
internal sealed class Smoothed
{
    /// <summary>
    /// The shortest a transition may be, and not a matter of taste: below it a
    /// motion stops being one.
    /// </summary>
    /// <remarks>
    /// At the length speed a frame's worth of travel is 45.8 units at 60 Hz and
    /// 23.5 at the 117 Hz measured on this machine, so a tab reordering by one
    /// place (<see cref="EdgeColumn.NotchReach"/>, 36 units) would complete
    /// inside a single frame on one machine and animate on another — the
    /// frame-rate independence this class exists to keep, failing at the other
    /// end. Three frames at 60 Hz is the fewest that can read as movement; six
    /// at 120. It is also what makes the duration provably non-zero on every
    /// path, so the division in <see cref="Tick"/> needs no guard of its own.
    /// </remarks>
    private const float MinimumDuration = 0.05f;

    /// <summary>And the longest.</summary>
    /// <remarks>
    /// The reason is not the tail — a timed curve always lands — but that a
    /// canvas unit is not a fixed physical size: the canvas-to-window factor is
    /// derived from the window's own width, measured at 1.002 and at 2.58 on one
    /// machine, so distance alone would make the chrome animate more slowly the
    /// bigger the monitor. Twelve frames at 60 Hz. It bites past 550 units,
    /// which in a 691-unit column is the full-height slide and nothing else.
    /// </remarks>
    private const float MaximumDuration = 0.2f;

    private readonly MotionSpeed _speed;
    private readonly MotionTiming _timing;

    private bool _started;
    private float _from;
    private float _elapsed;

    /// <summary>
    /// Never below <see cref="MinimumDuration"/>, on every path a snap included
    /// — the invariant <see cref="Tick"/>'s division rests on.
    /// </summary>
    private float _duration = MinimumDuration;

    public Smoothed(MotionSpeed speed, MotionTiming timing)
    {
        _speed = speed;
        _timing = timing;
    }

    public float Value { get; private set; }

    public float Target { get; private set; }

    /// <summary>
    /// Whether this number has ever been aimed at anything. The one honest
    /// answer to "is this the first time" — a caller that instead asks which way
    /// something was aimed <em>last</em> gets a yes on every reversal.
    /// </summary>
    public bool IsStarted => _started;

    public bool IsSettled => _started && _elapsed >= _duration;

    /// <summary>
    /// How long that much ground takes, at one quantity's own speed and the
    /// editor's current pace.
    /// </summary>
    /// <remarks>
    /// The pace multiplies the <b>finished</b> duration, clamps included.
    /// Applied to the distance-over-speed term alone it would do nothing at all
    /// to a motion either clamp is already holding, and both hold a great deal
    /// of this chrome: at Slow the 36-unit tab reorder and the 653-unit slide
    /// would be exactly what they are today, and the 328-unit panel open would
    /// land on the very same ceiling as the slide. Multiplying the clamped
    /// answer stretches the whole curve uniformly, so every relation between two
    /// motions survives at every setting and the knees stay where they are.
    /// </remarks>
    public static float DurationFor(float distance, MotionSpeed speed, MotionTiming timing) =>
        timing.DurationFactor
        * Math.Clamp(distance / speed.UnitsPerSecond, MinimumDuration, MaximumDuration);

    /// <summary>
    /// How far this number would actually travel if it were aimed there — zero
    /// when the target is unchanged enough to be tracked rather than restarted.
    /// </summary>
    /// <remarks>
    /// For a caller moving several numbers as one shape, which has to know which
    /// of them are about to set off. Asked here rather than restated there, so
    /// the quiet rule lives in one place.
    /// </remarks>
    public float RestartTravel(float value) =>
        _started && Math.Abs(value - Target) > _speed.Quiet
            ? Math.Abs(value - Value)
            : 0f;

    /// <summary>
    /// Puts the value where it belongs with no motion at all — for a thing
    /// arriving on screen, which has nowhere to have come from.
    /// </summary>
    public void SnapTo(float value)
    {
        Value = value;
        Target = value;
        _from = value;
        _duration = MinimumDuration;
        _elapsed = MinimumDuration;
        _started = true;
    }

    /// <summary>
    /// Aims at a new value, over however long the ground between here and there
    /// takes. The first one ever given snaps: a panel appearing for the first
    /// time should not fly in from the origin.
    /// </summary>
    public void Retarget(float value) =>
        RetargetOver(value, DurationFor(Math.Abs(value - Value), _speed, _timing));

    /// <summary>
    /// The same, over a clock somebody else set — for a caller moving several
    /// numbers that together are one shape, which must give them one clock or
    /// the shape deforms on the way. See <see cref="RectMotion.Retarget"/>.
    /// </summary>
    public void RetargetOver(float value, float duration)
    {
        // Above every other branch, and above the quiet rule in particular: the
        // column restates every target unchanged on every frame, which takes
        // that branch and returns — so anything below this would not answer a
        // setting switched off while a panel was in flight.
        if (!_timing.Animate)
        {
            SnapTo(value);

            return;
        }

        if (!_started)
        {
            SnapTo(value);

            return;
        }

        if (Math.Abs(value - Target) <= _speed.Quiet)
        {
            // Tracked, not restarted — and this branch is load-bearing rather
            // than an optimisation. The column restates every motion it owns at
            // its unchanged target on every frame, so a restart here would hold
            // IsSettled false for ever; and a panel that is never still can
            // never be picked up, IsStill being what TryGetDraggable asks.
            // Below Quiet nothing the output can show has changed anyway.
            Target = value;

            if (IsSettled)
                Value = value;

            return;
        }

        // From wherever it stands, which is what keeps the position continuous
        // through a target changed mid-flight — and for however long *that* much
        // ground takes. Measuring from Value rather than from the old target is
        // the whole of the reversal rule: coming back from one frame out costs
        // one frame's worth of ground, not a whole transition. Clamped here as
        // well as in DurationFor, so no caller can hand this a zero.
        _from = Value;
        _elapsed = 0f;

        // Scaled bounds, or a clock the caller computed through DurationFor is
        // cut straight back to the shipped ceiling here and every pace above
        // Fast silently does nothing past 550 units. Both callers that matter
        // compute one — every rect and every notch in the chrome.
        _duration = Math.Clamp(
            duration,
            MinimumDuration * _timing.DurationFactor,
            MaximumDuration * _timing.DurationFactor);

        Target = value;
    }

    public void Tick(float deltaSeconds)
    {
        if (!_started || IsSettled || deltaSeconds <= 0f)
            return;

        // Switched off while this was in flight. Here as well as in
        // RetargetOver, so that "instantly" is a property of this class rather
        // than of a caller that happens to restate its targets every frame.
        if (!_timing.Animate)
        {
            SnapTo(Target);

            return;
        }

        _elapsed = Math.Min(_duration, _elapsed + deltaSeconds);
        Value = _from + ((Target - _from) * Ease(_elapsed / _duration));
    }

    /// <summary>
    /// Sine ease-out: <c>sin(t·π/2)</c>. Fast away, decelerating, and exactly
    /// one at the end — spelled out rather than left to the sine, so the value
    /// lands on its target to the last bit rather than a rounding away from it.
    /// </summary>
    private static float Ease(float t) =>
        t >= 1f ? 1f : MathF.Sin(t * MathF.PI * 0.5f);
}
