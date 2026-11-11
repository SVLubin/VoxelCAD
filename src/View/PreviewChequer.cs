using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The ground a transparent material is judged against: two quiet shades in a
/// chequer, so how much shows through is read directly rather than inferred.
/// </summary>
/// <remarks>
/// A flat ground destroys exactly the reading a preview of a transparent material
/// exists for — a pane at 0.3 and one at 0.6 over the same grey differ only in how
/// dark they are, and nothing on screen says which is which. A ground of two
/// colours turns opacity into something visible: the strength of the pattern
/// showing through <b>is</b> the transparency.
/// <para>
/// Drawn rather than tiled from an image, because two rectangles in a loop is less
/// than the texture would cost to make, and because the shades come from the theme
/// and have to follow it when it changes.
/// </para>
/// </remarks>
internal sealed partial class PreviewChequer : Control
{
    /// <summary>Canvas units. Small enough to read as a ground, large enough not to shimmer.</summary>
    private const float SquareSide = 8f;

    /// <summary>
    /// How far the two shades sit either side of the ground they replace. Quiet on
    /// purpose: this is inside a dock, and an image editor's black and white would
    /// be the loudest thing in the window.
    /// </summary>
    private const float Contrast = 0.035f;

    public PreviewChequer() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Notification(int what)
    {
        if (what == NotificationThemeChanged)
            QueueRedraw();
    }

    public override void _Draw()
    {
        Color ground = GetThemeColor("inset_background", EditorThemeFactory.SemanticType);

        Color pale = ground.Lightened(Contrast);
        Color deep = ground.Darkened(Contrast);

        Vector2 size = Size;

        DrawRect(new Rect2(Vector2.Zero, size), deep);

        var row = 0;

        for (float y = 0f; y < size.Y; y += SquareSide)
        {
            var column = 0;

            for (float x = 0f; x < size.X; x += SquareSide)
            {
                if ((row + column) % 2 == 0)
                {
                    DrawRect(
                        new Rect2(
                            new Vector2(x, y),
                            new Vector2(
                                Mathf.Min(SquareSide, size.X - x),
                                Mathf.Min(SquareSide, size.Y - y))),
                        pale);
                }

                column++;
            }

            row++;
        }
    }
}
