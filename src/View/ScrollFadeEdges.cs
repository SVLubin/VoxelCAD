using Godot;

namespace VoxelCAD.View;

/// <summary>How strongly each edge of a scrolling body dissolves, 0 to 1.</summary>
internal readonly record struct ScrollFadeStrength(float Top, float Bottom)
{
    public static readonly ScrollFadeStrength None = new(0f, 0f);
}

/// <summary>
/// How far a scrolling body has run past each of its edges, as the strength the
/// dissolve there should be drawn at.
/// </summary>
/// <remarks>
/// Measured from <b>what is actually cut</b> rather than from how close a row
/// happens to be to the clip line, and that is the whole of the rule: at rest
/// nothing has gone past the top, so the top does not fade, however small the
/// content's own margin is. A body scrolled to its end fades at the top and not
/// at the bottom, for the same reason and by the same arithmetic.
/// <para>
/// The ramp exists so the dissolve does not appear at full strength on the first
/// pixel of a scroll — which would be a hard edge arriving instead of a hard
/// edge leaving.
/// </para>
/// </remarks>
internal static class ScrollFadeEdges
{
    /// <summary>
    /// How far a body must have scrolled before its edge is dissolving as much
    /// as it ever will. Short: past this the answer stops changing, and the
    /// point is only to get there smoothly rather than in one frame.
    /// </summary>
    public const float Onset = 10f;

    public static ScrollFadeStrength Solve(ScrollExtent extent)
    {
        if (extent.Maximum <= 0f)
            return ScrollFadeStrength.None;

        float scrolled = Mathf.Clamp(extent.Offset, 0f, extent.Maximum);

        return new ScrollFadeStrength(
            Mathf.Clamp(scrolled / Onset, 0f, 1f),
            Mathf.Clamp((extent.Maximum - scrolled) / Onset, 0f, 1f));
    }
}
