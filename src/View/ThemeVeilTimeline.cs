using Godot;

namespace VoxelCAD.View;

/// <summary>
/// One step of the veil: how far out of focus the window is, which picture is on
/// show, whether the frame is to be frozen and the look swapped now, and whether
/// the run is over.
/// </summary>
/// <param name="Blur">Nought sharp, one fully out of focus.</param>
/// <param name="Blend">Nought the frame that was, one the window as it is now.</param>
/// <param name="Breath">
/// How far into its one extra swell of softness the held stretch is, nought to
/// one and back — the frame breathing, so the fully blurred picture reads as
/// alive rather than hung. Always additive: the base blur is the floor it
/// returns to, never dips below.
/// </param>
internal readonly record struct ThemeVeilStep(
    float Blur,
    float Blend,
    float Breath,
    bool Swap,
    bool Finished);

/// <summary>
/// The clock a theme change is hidden behind: throw the window out of focus,
/// freeze it there and swap the look behind the still, let it settle, dissolve
/// to the new one while both are equally soft, and bring it back into focus.
/// </summary>
/// <remarks>
/// Three earlier shapes were tried and each one's failure forced part of this,
/// so they are worth keeping. A wash to the <b>incoming panel colour</b> was a
/// flashbang — dark to light is a whole screen going from about 0.12 to 0.87 in
/// a fifth of a second. A wash to the <b>midpoint</b> of the two themes fixed the
/// brightness and was still a flat field, which is the editor going away rather
/// than changing. And a <b>sharp</b> frozen frame read as lag, quite rightly: a
/// sharp still picture that does not answer the pointer is what a hung
/// application looks like.
/// <para>
/// So the first thing that happens is motion, on the frame of the click — the
/// window goes soft, and that <em>is</em> the feedback. Nothing on screen is
/// ever anything but the editor: first the old one, then the new one, both of
/// them out of focus in the middle where the change is made.
/// </para>
/// <para>
/// <b>Where the brightness step goes is the whole design.</b> Dark to light is a
/// real change of luminance and nothing can remove it; what can be chosen is
/// where it lands. It lands in the dissolve — between two pictures that are both
/// fully out of focus, so what crosses is a field of colour and not a page of
/// text — and the refocus afterwards makes it read as the window reforming
/// rather than as a flash. That is also why the dissolve is its own phase rather
/// than overlapping the refocus: two half-blurred pictures crossing show doubled
/// text, which is the ghosting every naive cross-fade has.
/// </para>
/// <para>
/// <b>No phase here may go under about five frames</b> — a twelfth of a second
/// at sixty. Under it the eye stops seeing a change and starts seeing two
/// different pictures: cut to 0.045 s, under three frames, the defocus was
/// reported as the window becoming blurred <em>instantly</em>, which is exactly
/// what three frames are. Speed here is therefore not a dial that runs to zero;
/// past the floor it stops being motion at all, and
/// <c>ThemeVeilTimelineTests.EveryPhaseIsLongEnoughToBeSeen</c> is what keeps
/// the next person from finding that out the way this one did.
/// </para>
/// <para>
/// A plain class rather than <see cref="Smoothed"/>, because this must run at
/// its own pace whatever <see cref="MotionTiming"/> says. It is not a transition
/// <em>of</em> the interface — it is a cover <em>over</em> a repaint, and
/// switching it off would not remove motion, it would show the seam the cover
/// exists to hide.
/// </para>
/// </remarks>
internal sealed class ThemeVeilTimeline
{
    /// <summary>Going soft. The window's answer to the click, so it starts at once.</summary>
    public const float DefocusSeconds = 0.13f;

    /// <summary>
    /// Held on the still while the repaint lands — the beat in which the
    /// containers Godot defers have their sort, out of sight — and the stage
    /// the breath plays on. It was 0.09 s when it only had the first job, and
    /// a breath needs to be *watched*: at anything under a second the cycle is
    /// squeezed between the defocus and the dissolve, both of which outshout
    /// it. Long enough for one whole cycle and the start of the next; the
    /// price is a slower theme switch, paid deliberately.
    /// </summary>
    public const float SettleSeconds = 0.9f;

    /// <summary>
    /// Where the brightness changes, and the reason it is its own phase: both
    /// pictures are fully out of focus here, so a light theme arriving over a
    /// dark one is a field changing colour rather than two pages of text
    /// crossing through each other.
    /// </summary>
    public const float DissolveSeconds = 0.15f;

    /// <summary>
    /// Coming back. Longer than the defocus on purpose: a picture resolving
    /// wants more time than a picture dissolving, and this is the half the eye
    /// actually reads.
    /// </summary>
    public const float RefocusSeconds = 0.16f;

    /// <summary>
    /// Where the swell lives: from the moment the defocus arrives at full to the
    /// moment the refocus leaves it — the whole stretch the still and the
    /// dissolve occupy, which is the part of the run that would otherwise stand
    /// still.
    /// </summary>
    private const float HeldStart = DefocusSeconds;

    private const float HeldSeconds = SettleSeconds + DissolveSeconds;

    /// <summary>
    /// One breath arch. Its own clock, deliberately not the held stretch's:
    /// welded to the stretch the breath was one swell however long the hold —
    /// and "breathing" is cycles, which is what was asked for in so many
    /// words. Brisk for a breath, because the stage is short.
    /// </summary>
    public const float BreathArchSeconds = 0.7f;

    /// <summary>
    /// How long the breathing takes to leave at the stretch's far end. There
    /// is deliberately no ramp at the near end: the breath's whole point is to
    /// entertain while the change is made, so it starts the instant the
    /// defocus lands, at the arch's own full attack — a ramp there was
    /// read as a dead delay before anything breathed.
    /// </summary>
    private const float BreathRampSeconds = 0.22f;

    /// <summary>
    /// When the frame is frozen and the look swapped: at the first breath's
    /// trough, not at the defocus's end. The swap frame's own work — the
    /// readback, the mip chain, the repaint — freezes rendering for a tenth of
    /// a second whatever the clock does, and parked at the start it read as a
    /// dead pause before the breathing began. In the trough the picture would
    /// be standing still anyway: the hitch hides inside the exhale.
    /// </summary>
    public const float SwapSeconds = HeldStart + BreathArchSeconds;

    /// <summary>
    /// The most one tick may advance the clock. A phase that is skipped is a
    /// phase nobody saw — the five-frame rule under hitches — and the run has
    /// a hitch built into it: the swap frame's own work (the readback, the mip
    /// chain, the whole repaint) took a seventh of a second and swallowed the
    /// breath's entire rise, so the still arrived already swollen and only
    /// ever relaxed. Traced per rendered frame: breath 0.00 on the swap frame,
    /// 0.84 on the next. Capped, a hitch costs wall time instead of
    /// choreography — everything after it still gets its frames on screen.
    /// </summary>
    private const float MaximumStepSeconds = 1f / 30f;

    private float _elapsed;
    private bool _swapped;

    public bool IsRunning { get; private set; }

    public float Blur { get; private set; }

    public float Blend { get; private set; }

    public static float Duration =>
        DefocusSeconds + SettleSeconds + DissolveSeconds + RefocusSeconds;

    public void Start()
    {
        IsRunning = true;
        _elapsed = 0f;
        _swapped = false;
        Blur = 0f;
        Blend = 0f;
    }

    /// <summary>
    /// Advances by <paramref name="delta"/> seconds, capped per tick — a hitch
    /// pauses the choreography rather than deleting the frames it covered, so
    /// however long a step, the freeze and the swap happen exactly once, out
    /// of sight, and in the first breath's trough — where the picture stands
    /// still anyway, so the repaint's own hitch reads as the exhale's pause.
    /// </summary>
    public ThemeVeilStep Advance(float delta)
    {
        if (!IsRunning)
            return new ThemeVeilStep(0f, 0f, 0f, Swap: false, Finished: false);

        _elapsed += Mathf.Min(delta, MaximumStepSeconds);
        Blur = BlurAt(_elapsed);
        Blend = BlendAt(_elapsed);

        bool swap = !_swapped && _elapsed >= SwapSeconds;
        _swapped |= swap;

        bool finished = _elapsed >= Duration;

        if (finished)
        {
            IsRunning = false;
            Blur = 0f;
            Blend = 1f;
        }

        return new ThemeVeilStep(Blur, Blend, BreathAt(_elapsed), swap, finished);
    }

    /// <summary>
    /// Out of focus and back. Whole from the moment the defocus ends until the
    /// refocus starts, which is the stretch the freeze, the swap and the
    /// dissolve all have to live in.
    /// </summary>
    public static float BlurAt(float elapsed)
    {
        if (elapsed <= 0f)
            return 0f;

        if (elapsed < DefocusSeconds)
            return Ease(elapsed / DefocusSeconds);

        float refocusing = elapsed - (DefocusSeconds + SettleSeconds + DissolveSeconds);

        if (refocusing <= 0f)
            return 1f;

        return refocusing >= RefocusSeconds ? 0f : Ease(1f - refocusing / RefocusSeconds);
    }

    /// <summary>
    /// Which picture is on show. The still until the new look has been applied
    /// and has settled, then over to the window as it now is — and it stays
    /// there, because the refocus has nothing to say about which picture it is
    /// sharpening.
    /// </summary>
    public static float BlendAt(float elapsed)
    {
        float dissolving = elapsed - (DefocusSeconds + SettleSeconds);

        if (dissolving <= 0f)
            return 0f;

        return dissolving >= DissolveSeconds ? 1f : Ease(dissolving / DissolveSeconds);
    }

    /// <summary>
    /// The breathing: sine arches on their own clock, from the instant the
    /// defocus lands. Strictly non-negative by construction — the base is a
    /// floor and breathing only ever adds — touching the floor between arches,
    /// which is what makes it read as breaths rather than as one long swell.
    /// An arch rather than a raised cosine on purpose: a raised cosine leaves
    /// the floor with no velocity, and stacked on the swap's ramp-in and the
    /// repaint hitch that read as a dead delay before anything breathed. The
    /// first inhale starts at full attack instead; only the far end is eased,
    /// so the breathing melts into the refocus rather than being cut by it.
    /// </summary>
    public static float BreathAt(float elapsed)
    {
        float held = elapsed - HeldStart;

        if (held <= 0f || held >= HeldSeconds)
            return 0f;

        float wave = Mathf.Abs(Mathf.Sin(Mathf.Pi * held / BreathArchSeconds));

        return Envelope(held) * wave;
    }

    /// <summary>
    /// One at full breath, easing to nought over the ramp at the stretch's far
    /// end alone — however the window cuts an arch, the breathing leaves with
    /// no velocity. The near end is deliberately not eased; see
    /// <see cref="BreathAt"/>.
    /// </summary>
    private static float Envelope(float held)
    {
        float edge = HeldSeconds - held;

        if (edge >= BreathRampSeconds)
            return 1f;

        float ramp = Mathf.Sin(Mathf.Pi * 0.5f * edge / BreathRampSeconds);

        return ramp * ramp;
    }

    private static float Ease(float t)
    {
        float clamped = Mathf.Clamp(t, 0f, 1f);

        return clamped * clamped * (3f - 2f * clamped);
    }
}
