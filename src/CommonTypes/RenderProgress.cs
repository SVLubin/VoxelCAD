namespace VoxelCAD.CommonTypes;

/// <summary>
/// How far along a render is, as one value read at one moment.
/// </summary>
/// <remarks>
/// One struct rather than a handful of properties because the render runs on a
/// thread of its own: read one at a time they could disagree — a fraction from
/// after the sample the count was read before — and a bar that jumped backwards
/// would be that, not a bug in the arithmetic.
/// </remarks>
/// <param name="SamplesTaken">
/// What the least converged part of the picture has had. The whole render is
/// worth what its worst corner is worth.
/// </param>
/// <param name="SamplesPeak">What the best served part has had.</param>
/// <param name="SamplesWanted">What was asked for.</param>
/// <param name="Fraction">Every sample counted, from nought to one.</param>
/// <param name="Seconds">How long it has been running.</param>
/// <param name="SamplesPerSecond">
/// Whole-picture samples a second, measured over the recent past rather than
/// averaged over the whole run — what is left is estimated from how fast it is
/// going now, not from how fast it started.
/// </param>
/// <param name="Noise">
/// How far from settled the worst part of the picture is, as a relative error —
/// a tenth means the average pixel there is about ten per cent out. Nought before
/// anything has been measured. The worst rather than the average, because a render
/// is done when its worst part is quiet.
/// </param>
/// <param name="IsFinished">Whether every part has had what was asked for.</param>
public readonly record struct RenderProgress(
    int SamplesTaken,
    int SamplesPeak,
    int SamplesWanted,
    float Fraction,
    double Seconds,
    double SamplesPerSecond,
    float Noise,
    bool IsFinished)
{
    /// <summary>Nothing has been traced and nothing is running.</summary>
    public static RenderProgress None => new(0, 0, 0, 0f, 0d, 0d, 0f, false);

    /// <summary>
    /// Roughly how much longer, or a negative number when there is nothing to
    /// estimate from — a render that has not taken a sample yet has no rate, and
    /// guessing one would put a number on screen that is about to be wrong.
    /// </summary>
    public double RemainingSeconds =>
        IsFinished || SamplesPerSecond <= 0d || SamplesWanted <= 0
            ? -1d
            : (1d - Fraction) * SamplesWanted / SamplesPerSecond;
}
