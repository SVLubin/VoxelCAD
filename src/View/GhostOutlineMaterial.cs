using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The landing outline: the panels' own shape, drawn in the accent instead of in
/// glass — an accent hairline round the whole of it, notch included, over a faint
/// wash of the same colour that stops at the shape's own boundary.
/// </summary>
/// <remarks>
/// A second material rather than a flag on <see cref="GlassPanelMaterial"/>, and
/// the shape shared as source. The decision it keeps is the canon's
/// (<c>docs/panels.md</c>): a ghost is a mark <em>about</em> where a surface will
/// be, not a surface, so it is never glass — the screen sampler is what costs a
/// backbuffer copy, and a bypassed one would still take it, once per frame for
/// the length of every drag. What is shared is the field; what is not is the
/// paint.
/// </remarks>
internal static class GhostOutlineMaterial
{
    /// <summary>
    /// How much of the accent the outline washes its hole with — enough to say
    /// <em>how much room</em>, which is the whole content of the message, and
    /// little enough that what it stands on stays readable through it.
    /// </summary>
    private const float FillAlpha = 0.1f;

    private static readonly string ShaderCode =
        $$"""
        shader_type canvas_item;

        {{PanelShapeField.Source}}

        uniform vec4 fill : source_color = vec4(0.44, 0.73, 0.98, 0.1);
        uniform vec4 line : source_color = vec4(0.44, 0.73, 0.98, 1.0);

        void fragment() {
            // The item's own modulate, read before it is overwritten. A
            // canvas_item fragment that assigns COLOR discards what the vertex
            // stage seeded it with, which is where SelfModulate arrives — so
            // read here, the fade goes on being SelfModulate's job and needs no
            // channel of its own.
            float fade = COLOR.a;

            vec2 coverage = panel_coverage(panel_distance(UV * rect_size));

            // Coverage in the *alpha* channel, because a ghost is translucent
            // where the glass is opaque: the glass composites the screen itself
            // and can spend alpha on the shape alone. Written that way here it
            // would be a solid accent plate. And the border is multiplied
            // through the coverage, never added to it — see panel_coverage.
            COLOR = vec4(
                mix(fill.rgb, line.rgb, coverage.y),
                coverage.x * mix(fill.a, line.a, coverage.y) * fade);
        }
        """;

    private static ShaderMaterial? _shared;

    public static ShaderMaterial Shared()
    {
        if (_shared != null)
            return _shared;

        _shared = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };

        Retint(EditorPalette.Dark());

        return _shared;
    }

    /// <summary>
    /// The accent, in the one material every ghost shares — the same push a
    /// glass pane needs, and for the same reason: a shader looks nothing up.
    /// </summary>
    public static void Retint(EditorPalette palette)
    {
        if (_shared == null)
            return;

        _shared.SetShaderParameter("fill", new Color(palette.Accent, FillAlpha));
        _shared.SetShaderParameter("line", palette.Accent);
    }
}
