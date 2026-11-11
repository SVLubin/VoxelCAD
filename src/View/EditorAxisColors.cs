using Godot;

namespace VoxelCAD.View;

/// <summary>
/// What X, Y and Z look like across the editor. One vocabulary rather than a set
/// of literals per widget: the transform handles and the view cube have to agree
/// about which colour means which axis, or the two teach the user opposite things.
/// </summary>
internal static class EditorAxisColors
{
    public static readonly Color X = new(0.93f, 0.28f, 0.30f);

    public static readonly Color Y = new(0.40f, 0.85f, 0.36f);

    public static readonly Color Z = new(0.32f, 0.53f, 0.96f);

    /// <summary>
    /// One colour for whatever is under the pointer, rather than three brighter
    /// shades: what it says is "this is the one you would grab", which is the same
    /// statement whatever axis it is.
    /// </summary>
    public static readonly Color Hover = new(1f, 0.86f, 0.32f);

    /// <summary>The colour of axis 0, 1 or 2, in the order a <c>Basis</c> holds them.</summary>
    public static Color Get(int axis) => axis switch
    {
        0 => X,
        1 => Y,
        _ => Z
    };

    /// <summary>The theme entry a panel reads its axis colour from.</summary>
    public static string NameOf(int axis) => axis switch
    {
        0 => "axis_x",
        1 => "axis_y",
        _ => "axis_z"
    };

    /// <summary>
    /// The same three colours, written on a surface rather than floating in the
    /// scene. The gizmo's red, green and blue sit on a dark room whatever the
    /// chrome is doing; a panel's do not, and the green in particular is a pale
    /// smear on a light card. Pulled toward the ink until it can be read.
    /// </summary>
    /// <remarks>
    /// A darkening rather than a second set of literals: the two must stay the
    /// same three colours, because a panel and a handle naming the same axis
    /// differently is exactly what this class exists to prevent.
    /// </remarks>
    public static Color OnSurface(int axis, EditorPalette palette) =>
        palette.IsDark ? Get(axis) : Get(axis).Darkened(SurfaceDarkening);

    /// <summary>
    /// How far a light theme takes them down. Enough that the green reads as
    /// green against a pale card, not so far that the three stop being the
    /// gizmo's own colours.
    /// </summary>
    private const float SurfaceDarkening = 0.35f;
}
