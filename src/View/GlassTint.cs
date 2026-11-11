using Godot;

namespace VoxelCAD.View;

/// <summary>
/// What a pane of glass is made of, and what it comes out as over a given
/// background — the arithmetic <see cref="GlassPanelMaterial"/> hands its shader,
/// said once and outside it.
/// </summary>
/// <remarks>
/// It lives here rather than inside the material because "glass" is a
/// <em>relation</em> between the pane and what is behind it, and the thing
/// behind it — the scene — is chosen elsewhere. So whether a panel stands off
/// this editor's backdrop is a question with an answer, and while the answer
/// existed only inside a shader nothing could ask it: the light theme shipped
/// with a pane composing to within a hundredth of the void it stood on, which
/// no test could have caught and no screenshot of a panel over the room would
/// have shown.
/// </remarks>
internal readonly struct GlassTint
{
    /// <summary>
    /// How much of the pane is its own colour rather than the scene behind it.
    /// </summary>
    /// <remarks>
    /// Two numbers, because the scene is not themed. Over this editor's grey
    /// room a dark tint at about half strength already reads as a dark pane; a
    /// light one at the same strength composes to mid-grey — measured 0.46 where
    /// the panel colour is 0.87 — and the whole light theme arrives as a wash. A
    /// light pane has to be mostly opaque before it is light at all.
    /// </remarks>
    private const float DarkStrength = 0.52f;

    private const float LightStrength = 0.82f;

    private GlassTint(Color tint, float strength)
    {
        Tint = tint;
        Strength = strength;
    }

    /// <summary>The pane's own colour, before anything behind it is let through.</summary>
    public Color Tint { get; }

    /// <summary>How far <see cref="Tint"/> wins over what is behind the pane.</summary>
    public float Strength { get; }

    /// <summary>
    /// The glass a palette is made of: the tint moved <b>away from the ink</b>,
    /// like the hairline — a dark pane sits a shade under its panel colour, a
    /// light one a shade over it. Darkening both would make the light theme's
    /// glass the one grey surface in it.
    /// </summary>
    public static GlassTint For(EditorPalette palette) =>
        new(
            palette.IsDark
                ? palette.PanelBackground.Darkened(0.2f)
                : palette.PanelBackground.Lightened(0.2f),
            palette.IsDark ? DarkStrength : LightStrength);

    /// <summary>What the pane comes out as over <paramref name="behind"/>.</summary>
    public Color Over(Color behind) => behind.Lerp(Tint, Strength);
}
