using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The glass a surface is made of, and — since a shape like the panels' cannot
/// be a stylebox — the surface itself: this shader draws the fill, the hairline
/// border and the outline, all from one distance field.
/// </summary>
/// <remarks>
/// The engine's <see cref="StyleBox"/> draws rectangles, and a panel grown from
/// its own tab is not one: it is a rectangle with a notch bitten out below the
/// stub, so the tabs underneath show through. Two controls faked into looking
/// like one left a seam wherever their corners met, and no per-corner radius
/// setting can round a <em>concave</em> corner at all. A field can: the shape is
/// a rounded box minus a rounded box, and subtracting one leaves the fillet the
/// inner corner wants for free.
/// <para>
/// One screen read per surface stands (see <c>docs/panels.md</c>): the blur is a
/// mip lookup, everything drawn inside a surface tints the pane it already has.
/// </para>
/// </remarks>
internal static class GlassPanelMaterial
{
    /// <summary>Mip level of the blur — each step doubles the smear radius.</summary>
    private const float BlurLod = 3.4f;

    internal static readonly string FrostedCode =
        $$"""
        shader_type canvas_item;

        uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear_mipmap;
        uniform float blur_lod = 3.4;
        uniform vec4 tint : source_color = vec4(0.21, 0.24, 0.29, 0.52);
        uniform vec4 border_color : source_color = vec4(0.4, 0.45, 0.52, 1.0);

        {{PanelShapeField.Source}}

        void fragment() {
            // The item's own modulate, read before it is overwritten. A
            // canvas_item fragment that assigns COLOR discards what the vertex
            // stage seeded it with, and that is where a parent's modulate
            // arrives — so a pane whose card was being faded out simply did not
            // fade, and vanished whole on the frame the card was hidden.
            float fade = COLOR.a;

            vec2 coverage = panel_coverage(panel_distance(UV * rect_size));
            vec3 pane = mix(textureLod(screen_texture, SCREEN_UV, blur_lod).rgb, tint.rgb, tint.a);

            COLOR = vec4(
                mix(pane, border_color.rgb, coverage.y * border_color.a), coverage.x * fade);
        }
        """;

    /// <summary>
    /// The same pane with the screen taken out of it: one flat colour, the same
    /// shape, the same hairline.
    /// </summary>
    /// <remarks>
    /// A second shader rather than a branch inside the first, and that is the whole
    /// point of the setting: a canvas item whose shader <em>declares</em>
    /// <c>hint_screen_texture</c> makes the engine copy the back buffer for it,
    /// whether or not a branch went on to read the sampler. So a uniform switch
    /// would have turned the blur off and gone on paying for it — which is exactly
    /// the "genuinely not computed anywhere" this is for.
    /// </remarks>
    internal static readonly string SolidCode =
        $$"""
        shader_type canvas_item;

        uniform vec4 pane_color : source_color = vec4(0.21, 0.24, 0.29, 1.0);
        uniform vec4 border_color : source_color = vec4(0.4, 0.45, 0.52, 1.0);

        {{PanelShapeField.Source}}

        void fragment() {
            float fade = COLOR.a;
            vec2 coverage = panel_coverage(panel_distance(UV * rect_size));

            COLOR = vec4(
                mix(pane_color.rgb, border_color.rgb, coverage.y * border_color.a),
                coverage.x * fade);
        }
        """;

    private static ShaderMaterial? _shared;
    private static Shader? _frosted;
    private static Shader? _solid;
    private static EditorPalette _palette = EditorPalette.Dark();
    private static bool _frostedWanted = true;

    public static ShaderMaterial Shared()
    {
        if (_shared != null)
            return _shared;

        _frosted = new Shader { Code = FrostedCode };
        _shared = new ShaderMaterial { Shader = _frostedWanted ? _frosted : Solid() };
        _shared.SetShaderParameter("blur_lod", BlurLod);

        Retint(_palette);

        return _shared;
    }

    /// <summary>
    /// Says whether the panes read the screen behind them. Switched on the one
    /// material every pane shares — swapping the <em>shader</em> rather than the
    /// material, so no surface has to be re-pointed at anything and a pane built
    /// after the switch is born wearing the right one.
    /// </summary>
    public static void SetFrosted(bool frosted)
    {
        _frostedWanted = frosted;

        if (_shared == null)
            return;

        _shared.Shader = frosted ? _frosted : Solid();

        // A parameter a shader does not declare is dropped with it, so what the
        // new one reads is written again rather than assumed to have survived.
        _shared.SetShaderParameter("blur_lod", BlurLod);
        Retint(_palette);
    }

    private static Shader Solid() => _solid ??= new Shader { Code = SolidCode };

    /// <summary>
    /// Writes the current look into the one material every pane shares, which is
    /// how a theme change reaches glass: the shader is outside the theme, so
    /// nothing looks these colours up as it draws.
    /// </summary>
    public static void Retint(EditorPalette palette)
    {
        _palette = palette;

        if (_shared == null)
            return;

        // What the pane is made of is GlassTint's answer, so that whether a panel
        // stands off the scene behind it is a question a test can put.
        GlassTint glass = GlassTint.For(palette);

        _shared.SetShaderParameter("tint", new Color(glass.Tint, glass.Strength));

        // What the frosted pane averages to over the backdrop it usually stands on,
        // so switching the blur off changes how much of the scene shows through and
        // not what colour the chrome is.
        _shared.SetShaderParameter("pane_color", glass.Over(palette.VoidLow));

        // A hairline the eye can find against a bright scene as well as a dark
        // one: the pane's own edge, moved away from the surface rather than
        // tinted — up on a dark theme, down on a light one, where lifting a
        // light grey gives a line nobody can see.
        Color edge = palette.IsDark
            ? palette.BorderHover.Lightened(0.25f)
            : palette.BorderHover.Darkened(0.25f);

        _shared.SetShaderParameter("border_color", new Color(edge, 0.85f));
    }
}
