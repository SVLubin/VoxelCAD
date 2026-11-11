namespace VoxelCAD.View;

/// <summary>
/// How fast one kind of quantity moves, and how small a change of target is
/// beneath noticing.
/// </summary>
/// <remarks>
/// The two travel together because they are the same question asked at the two
/// ends of a scale — a length is measured in canvas units and an alpha in the
/// nought-to-one of a colour channel, and a speed or a threshold written for one
/// is off by three orders of magnitude for the other.
/// <para>
/// This exists because the mismatch has already shipped. The drop hint's whole
/// range is 0.14 and it was carried by a number whose "beneath noticing" was
/// half a unit, so every transition it ever made was an assignment: it did not
/// fade a little less, it did not fade at all, and nothing warned. A preset
/// makes the pairing unwritable rather than merely unlikely, and the constructor
/// argument is required rather than defaulted for the same reason — a default is
/// exactly how an alpha inherited a length's threshold.
/// </para>
/// </remarks>
internal readonly struct MotionSpeed
{
    /// <summary>
    /// Canvas units per second, for everything with an edge: panel rects, the
    /// notch, a column's own width.
    /// </summary>
    /// <remarks>
    /// Calibrated so the motion this chrome is judged by takes what it takes
    /// today. A panel opening out of its tab grows from the tab's square
    /// (<see cref="ChromePanel.CollapsedSide"/>, 30) to a surface that gives a
    /// notch away (<see cref="ChromePanel.SurfaceWidth"/> 322 plus
    /// <see cref="EdgeColumn.NotchReach"/> 36, so 358): 328 units, on either
    /// column, for every panel. At 2750 that is 119.3 ms against the 120.0 ms
    /// one fixed duration gave — a fifth of a frame, so nothing is restyled and
    /// only the relation between motions changes.
    /// </remarks>
    public static readonly MotionSpeed Length = new(2750f, 0.5f);

    /// <summary>
    /// Alpha per second, for anything that appears rather than moves.
    /// </summary>
    /// <remarks>
    /// Set by the one fade that spans the whole channel — the landing ghost's
    /// outline, nought to one — at 83.3 ms: five frames at 60 Hz, ten at 120.
    /// Long enough that the eye reads a fade rather than a switch, short enough
    /// to be up before the hand has travelled.
    /// <para>
    /// The drop hint is not governed by this number and cannot be. Its largest
    /// step is 0.14, which is 11.7 ms of travel — below <see cref="Smoothed"/>'s
    /// floor, so it is a fixed 50 ms fade whatever this constant says. That is
    /// the floor doing its job: below three frames there is no motion to have a
    /// speed. Wanting the hint speed-governed instead means an alpha speed under
    /// 2.8, which would make the ghost's own fade a third of a second.
    /// </para>
    /// </remarks>
    public static readonly MotionSpeed Opacity = new(12f, 1f / 255f);

    private MotionSpeed(float unitsPerSecond, float quiet)
    {
        UnitsPerSecond = unitsPerSecond;
        Quiet = quiet;
    }

    public float UnitsPerSecond { get; }

    /// <summary>
    /// A change of target this small or smaller is tracked without restarting
    /// the curve.
    /// </summary>
    /// <remarks>
    /// Two jobs in one number, and both are needed. It is what stops a target
    /// restated unchanged every frame — which the column does to every motion it
    /// owns — from holding the clock at zero for ever; and some targets are
    /// measured from live content and jitter by less than the output can show.
    /// Which is where the value comes from in both cases: half a unit is what
    /// <see cref="RectMotion.Snap"/> rounds away, and 1/255 is what an eight-bit
    /// alpha channel rounds away.
    /// </remarks>
    public float Quiet { get; }
}
