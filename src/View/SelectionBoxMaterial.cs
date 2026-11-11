using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The dash-dot line the selection border is drawn with, and the solid line the
/// drag ghost gets instead.
/// </summary>
/// <remarks>
/// The two used to be one colour at two opacities, and during a drag that put two
/// nearly identical boxes on screen at once — the one the element is in and the one
/// the hand is pulling towards — differing only in how bright they were. They now
/// differ in <b>colour and line style both</b>, and the split follows what each is
/// saying:
/// <list type="bullet">
/// <item>the border is an annotation on something the user is looking at, so it is
/// white and dash-dot — the draughtsman's centre line, which reads as a mark
/// <em>about</em> geometry rather than as geometry;</item>
/// <item>the ghost is where the gesture is going, so it stays the accent colour and
/// solid, and is now the more emphatic of the two because it is the one the eye
/// should follow.</item>
/// </list>
/// <para>
/// The pattern runs in the element's own cells rather than in screen space, so it
/// does not swim as the camera moves and its period is the same ruler the walls'
/// grid uses. The cost is that a non-uniform scale stretches the dashes along the
/// scaled axis, which is honest — they still count cells.
/// </para>
/// </remarks>
internal static class SelectionBoxMaterial
{
    /// <summary>One dash-plus-dot in cells.</summary>
    private const float Period = 2f;

    /// <summary>
    /// What fills the gaps. Not transparency: a white line disappears into a pale
    /// model, which is exactly where the border matters most, so the pattern is two
    /// tones rather than one tone and nothing. Kept well under opaque so it reads as
    /// the shadow of a mark rather than as a second line.
    /// </summary>
    private static readonly Color GapColor = new(0f, 0f, 0f, 0.5f);

    /// <summary>
    /// The pattern within one period, as fractions of it: a long dash, a gap, a
    /// short dot, a gap. Dash-dot rather than plain dash because a plain dash on a
    /// box edge is hard to tell from a line broken by whatever is in front of it.
    /// </summary>
    private const float DashEnd = 0.55f;
    private const float DotStart = 0.72f;
    private const float DotEnd = 0.84f;

    /// <summary>
    /// Chrome, so it is drawn over the model rather than into it: without this the
    /// border of a box hugging its own voxels would be half-swallowed by them.
    /// </summary>
    private const int ChromePriority = 126;

    public static ShaderMaterial CreateDashDot(Color color)
    {
        var shader = new Shader();
        shader.Code =
            """
            shader_type spatial;
            render_mode unshaded, depth_test_disabled, depth_draw_never, cull_disabled;

            uniform vec4 line_color : source_color = vec4(1.0);
            // What the gaps are filled with instead of nothing. A voxel model is
            // usually pale, and a white line vanishing into it is what the plain
            // dash did: the border was legible where it crossed the room and gone
            // where it crossed the model. Two tones survive either background.
            uniform vec4 gap_color : source_color = vec4(0.0, 0.0, 0.0, 0.55);
            // One dash-plus-dot, in the units UV.x is measured in — the element's
            // own cells. The mesh puts the distance along each edge there, because
            // a line primitive gives a fragment nothing else to run a pattern on.
            uniform float period = 2.0;
            uniform float dash_end = 0.55;
            uniform float dot_start = 0.72;
            uniform float dot_end = 0.84;

            void fragment() {
                float u = UV.x / period;
                float phase = fract(u);

                // Softened by the derivative of u rather than of phase: fwidth of a
                // fract is a spike at the wrap, and it would put a bright speck at
                // every period boundary. As the pattern falls below a pixel this
                // widens until the smoothsteps flatten, so a dense edge settles
                // towards the blend of the two tones instead of shimmering.
                float soft = max(fwidth(u), 1e-4) * 1.5;

                float dash = smoothstep(-soft, soft, dash_end - phase);
                float dot = smoothstep(-soft, soft, phase - dot_start)
                    * smoothstep(-soft, soft, dot_end - phase);
                float mark = clamp(dash + dot, 0.0, 1.0);

                ALBEDO = mix(gap_color.rgb, line_color.rgb, mark);
                ALPHA = mix(gap_color.a, line_color.a, mark);
            }
            """;

        var material = new ShaderMaterial { Shader = shader };

        material.SetShaderParameter("line_color", color);
        material.SetShaderParameter("gap_color", GapColor);
        material.SetShaderParameter("period", Period);
        material.SetShaderParameter("dash_end", DashEnd);
        material.SetShaderParameter("dot_start", DotStart);
        material.SetShaderParameter("dot_end", DotEnd);
        material.RenderPriority = ChromePriority;

        return material;
    }

    /// <summary>
    /// The ghost's line: the same geometry, unbroken and in colour. It ignores the
    /// arc length the mesh carries, which is what makes one mesh serve both.
    /// </summary>
    public static StandardMaterial3D CreateSolid(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = true,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        RenderPriority = ChromePriority
    };
}
