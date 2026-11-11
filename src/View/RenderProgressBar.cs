using Godot;

namespace VoxelCAD.View;

/// <summary>
/// How far along a render is, as a line that fills. A thin accent bar on the
/// inset the rest of the card's fields stand on.
/// </summary>
/// <remarks>
/// Drawn rather than a themed <see cref="ProgressBar"/> so it stays two rectangles
/// and one colour lookup, and so it eases: the fraction arrives about ten times a
/// second in steps of whatever the last batch managed, and a bar that jumped in
/// those steps would read as the render stalling and catching up.
/// </remarks>
internal sealed partial class RenderProgressBar : Control
{
    private const float BarHeight = 3f;

    private readonly Smoothed _filled;

    public RenderProgressBar(MotionTiming timing)
    {
        Name = "RenderProgressBar";
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(0f, BarHeight);

        _filled = new Smoothed(MotionSpeed.Opacity, timing);
        _filled.SnapTo(0f);
    }

    /// <summary>
    /// How full, from nought to one. A render starting again goes back to nought
    /// outright rather than easing down — that is a different render, not this one
    /// losing ground.
    /// </summary>
    public void SetFraction(float fraction)
    {
        float wanted = Mathf.Clamp(fraction, 0f, 1f);

        if (wanted < _filled.Value)
            _filled.SnapTo(wanted);
        else
            _filled.Retarget(wanted);

        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _filled.Tick((float)delta);

        QueueRedraw();

        if (_filled.IsSettled)
            SetProcess(false);
    }

    public override void _Draw()
    {
        var track = new Rect2(0f, 0f, Size.X, BarHeight);

        DrawRect(track, GetThemeColor("inset_background", EditorThemeFactory.SemanticType));

        float width = Size.X * _filled.Value;

        if (width <= 0f)
            return;

        DrawRect(
            new Rect2(0f, 0f, width, BarHeight),
            GetThemeColor("accent", EditorThemeFactory.SemanticType));
    }
}
