using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The soft edge a scrolling panel body is cut with: a mask that is solid
/// through the middle and falls away to nothing over the last few units at
/// whichever edge has content running past it.
/// </summary>
/// <remarks>
/// It masks by <b>alpha</b> — <see cref="CanvasItem.ClipChildrenMode.Only"/>,
/// where what this control draws is not shown but is used as the stencil for
/// everything under it. That is the one mechanism that works here, and the
/// obvious cheaper one does not: laying a gradient of the background colour over
/// the content would be a visible haze band, because a panel is glass and what
/// is behind the rows is a blur rather than a fill. A mask takes a buffer of its
/// own, which is a real cost — but it is a stencil rather than a second screen
/// read, so it does not blur the blur, which is the thing the panels' own rule
/// is actually about.
/// <para>
/// Drawn as vertex colours rather than through a shader or a gradient texture:
/// the shape is two ramps and a rectangle, which is exactly what a polygon with
/// per-corner alpha already is.
/// </para>
/// </remarks>
internal sealed partial class ScrollFade : Control
{
    /// <summary>
    /// How far the dissolve reaches in from an edge. Small on purpose: what it
    /// is for is to take the knife off the cut, not to vignette the panel.
    /// </summary>
    public const float Band = 16f;

    private ScrollFadeStrength _strength = ScrollFadeStrength.None;

    public ScrollFade()
    {
        Name = "ScrollFade";

        // Its drawing is the mask and is never itself seen.
        ClipChildren = ClipChildrenMode.Only;

        // A wrapper, so it takes no mouse of its own — the rows under it still
        // get theirs, an ignored control being skipped rather than sealed.
        MouseFilter = MouseFilterEnum.Ignore;

        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void SetStrength(ScrollFadeStrength strength)
    {
        if (_strength == strength)
            return;

        _strength = strength;

        QueueRedraw();
    }

    public override void _Draw()
    {
        float width = Size.X;
        float height = Size.Y;

        if (width <= 0f || height <= 0f)
            return;

        // Never more than half the body each, or in a short panel the two ramps
        // would overlap and the middle would be masked from both sides at once.
        float band = Mathf.Min(Band, height * 0.5f);

        // Three pieces that tile the rect exactly, never one laid over another:
        // the mask is an alpha, and a translucent ramp drawn *over* a solid one
        // blends back to solid instead of replacing it.
        DrawRamp(width, 0f, band, 1f - _strength.Top, 1f);
        DrawRamp(width, band, height - band, 1f, 1f);
        DrawRamp(width, height - band, height, 1f, 1f - _strength.Bottom);
    }

    /// <summary>
    /// One horizontal strip of the mask, as opaque at its top as
    /// <paramref name="top"/> asks and as opaque at its bottom as
    /// <paramref name="bottom"/> does.
    /// </summary>
    private void DrawRamp(float width, float from, float to, float top, float bottom)
    {
        if (to <= from)
            return;

        var points = new[]
        {
            new Vector2(0f, from),
            new Vector2(width, from),
            new Vector2(width, to),
            new Vector2(0f, to)
        };

        var high = new Color(1f, 1f, 1f, top);
        var low = new Color(1f, 1f, 1f, bottom);

        DrawPolygon(points, new[] { high, high, low, low });
    }
}
