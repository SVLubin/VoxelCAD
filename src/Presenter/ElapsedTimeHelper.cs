using System;

namespace VoxelCAD.Presenter;

/// <summary>
/// Formats how long an agent has held control, so the banner reads at a glance:
/// bare seconds while that is short enough to grasp, then minutes and seconds.
/// </summary>
/// <remarks>
/// Wording lives in the presenter, not the view — which is why this returns a
/// string rather than the view choosing between two of them. The clock counts
/// time completed, so it floors: "5s" appears at five seconds and holds until six.
/// </remarks>
internal static class ElapsedTimeHelper
{
    private const int SecondsPerMinute = 60;

    public static string Format(double seconds)
    {
        var total = (int)Math.Max(0.0, seconds);

        if (total < SecondsPerMinute)
            return $"{total}s";

        int minutes = total / SecondsPerMinute;
        int remainder = total % SecondsPerMinute;

        return $"{minutes}:{remainder:00}";
    }
}
