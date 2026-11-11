using System;
using System.Globalization;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What the line under a render says. Pure, because wording is a decision and a
/// decision inside a <see cref="Godot.Control"/> is one no test can reach — the
/// same argument every other pure piece in this layer rests on.
/// </summary>
internal static class RenderProgressLine
{
    /// <summary>
    /// The whole line: how far along, how long it has taken, and roughly how much
    /// longer.
    /// </summary>
    /// <remarks>
    /// The part being looked at is named separately when it is ahead, and only
    /// then. A render nobody has zoomed into converges evenly and there is nothing
    /// to say; one somebody is aiming at has two honest answers — what the whole
    /// picture is worth and what the part on screen is worth — and showing only
    /// the first would make a visibly clean corner read as noisy.
    /// </remarks>
    public static string Describe(RenderProgress progress)
    {
        if (progress.SamplesWanted <= 0)
            return string.Empty;

        string elapsed = Duration(progress.Seconds);

        if (progress.IsFinished)
            return $"{progress.SamplesTaken} samples · {elapsed}";

        string line = $"{progress.SamplesTaken} of {progress.SamplesWanted} samples";

        if (progress.SamplesPeak > progress.SamplesTaken)
            line += $" · {progress.SamplesPeak} in view";

        line += $" · {elapsed}";

        double remaining = progress.RemainingSeconds;

        if (remaining >= 0d)
            line += $" · about {Duration(remaining)} left";

        return line;
    }

    /// <summary>
    /// A span of time, at the precision it is worth reading at: tenths while it is
    /// a few seconds, whole seconds past ten, minutes past a minute and hours past
    /// an hour. A render reported to a tenth of a second after four minutes is four
    /// digits nobody uses, and the same argument one step up is what stops a slow
    /// render being estimated at "1666 m 40 s".
    /// </summary>
    public static string Duration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0d)
            return "0.0 s";

        // Decided by what would be <em>printed</em> rather than by the raw value:
        // 9.96 rounds to 10.0, and "10.0 s" is the one reading this is meant to
        // stop happening.
        if (Math.Round(seconds, 1) < 10d)
            return seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

        double whole = Math.Round(seconds, MidpointRounding.AwayFromZero);

        if (whole < 60d)
            return whole.ToString("0", CultureInfo.InvariantCulture) + " s";

        Split(seconds, 60d, out int minutes, out int restSeconds);

        // Decided by what would be printed, exactly as the tenths above are: a span
        // a hair under the hour whose seconds round up is an hour, not "60 m".
        if (minutes < 60)
            return restSeconds == 0 ? $"{minutes} m" : $"{minutes} m {restSeconds} s";

        Split(seconds / 60d, 60d, out int hours, out int restMinutes);

        return restMinutes == 0 ? $"{hours} h" : $"{hours} h {restMinutes} m";
    }

    /// <summary>
    /// A span as whole units and a rounded remainder, with the remainder rolled up
    /// when it rounds to a whole unit of its own — or a span a hair under the hour
    /// prints as "59 m 60 s".
    /// </summary>
    private static void Split(double span, double per, out int whole, out int rest)
    {
        whole = (int)(span / per);

        // Away from zero, never the default: .NET rounds halves to even, so a span
        // of an hour and ten and a half minutes would read "1 h 10 m". The same
        // surprise Godot's own Vector3.Round holds, and the ledger says to assume
        // this class of it is still out there.
        rest = (int)Math.Round(span - whole * per, MidpointRounding.AwayFromZero);

        if (rest < (int)per)
            return;

        whole++;
        rest = 0;
    }
}
