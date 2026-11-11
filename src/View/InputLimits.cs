using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// What a synthesized gesture may not exceed, and the path a drag takes. Bounded
/// because a gesture holds the frame queue while it runs: everything behind it
/// waits, so an unbounded one is an editor that has stopped answering.
/// </summary>
/// <remarks>
/// Out of range is clamped rather than refused, the same bargain the model makes
/// with a transform and the preferences make with a threshold — a caller asking
/// for a thousand steps wants a slow drag, not an exception.
/// </remarks>
internal static class InputLimits
{
    /// <summary>
    /// Steps in one drag. At sixty frames a second this is two seconds of
    /// pointer travel, which is longer than any gesture a hand makes.
    /// </summary>
    public const int MaximumSteps = 120;

    public const int MinimumSteps = 1;

    /// <summary>Enough for a double click, and one spare.</summary>
    public const int MaximumClicks = 3;

    public const int MaximumNotches = 32;

    /// <summary>
    /// Characters in one typed string. A field nobody can see the end of is not
    /// something to fill from here.
    /// </summary>
    public const int MaximumTextLength = 256;

    public static int ClampSteps(int steps) => Math.Clamp(steps, MinimumSteps, MaximumSteps);

    public static int ClampClicks(int clicks) => Math.Clamp(clicks, 1, MaximumClicks);

    public static int ClampNotches(int notches) =>
        Math.Clamp(notches, -MaximumNotches, MaximumNotches);

    /// <summary>
    /// A point brought inside the window. Clamped rather than refused for the
    /// reason above, and because the edge is a legitimate place to aim — a drag
    /// that runs into the side of the window should stop there, not throw.
    /// </summary>
    public static Vector2 ClampPoint(Vector2 point, Vector2I windowSize) =>
        new(
            Math.Clamp(point.X, 0f, Math.Max(0, windowSize.X - 1)),
            Math.Clamp(point.Y, 0f, Math.Max(0, windowSize.Y - 1)));

    /// <summary>
    /// Where the pointer is on each frame of a drag: the start is where the press
    /// already put it, so the path is the <paramref name="steps"/> positions after
    /// it, ending exactly on <paramref name="to"/> rather than near it.
    /// </summary>
    public static IReadOnlyList<Vector2> ComposePath(Vector2 from, Vector2 to, int steps)
    {
        int count = ClampSteps(steps);
        var pathList = new List<Vector2>(count);

        for (var step = 1; step <= count; step++)
            pathList.Add(from.Lerp(to, (float)step / count));

        return pathList;
    }
}
