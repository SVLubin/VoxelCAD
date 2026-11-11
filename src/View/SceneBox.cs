using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Goxel-style scene box: SEE_BACK (cull exterior / front faces).
/// BoxMesh outward normals + cull_front; engine flips shading on backfaces
/// (do NOT manually negate NORMAL — that made the floor dark).
/// Grid is drawn in world space on integer voxel planes.
/// </summary>
internal sealed partial class SceneBox : Node3D
{
    private const float GridStrength = 0.5f;

    /// <summary>
    /// The dash pattern's period in cells while the walls cannot be placed
    /// against; zero leaves the grid solid. One cell puts a single dash on every
    /// cell edge, centred on the corner — so the two directions meet and every
    /// intersection is a cross.
    /// </summary>
    private const float UnpaintableDashPeriod = 1f;

    /// <summary>
    /// How much of each period is drawn. The crosses mark the lattice, which is
    /// what the grid is for; the rest of the edge can be gap, and the more of it
    /// is, the more plainly the surface reads as out of use.
    /// </summary>
    private const float UnpaintableDashDuty = 0.4f;

    /// <summary>
    /// How much of the grid's weight the dashed state keeps. The lines are the
    /// same colour in the same places; they are just carrying less of it.
    /// </summary>
    private const float UnpaintableGridFade = 0.45f;

    private readonly MeshInstance3D _meshInstance;
    private readonly ShaderMaterial _material = CreateSeeBackMaterial();

    private bool _paintable = true;

    public SceneBox()
    {
        _meshInstance = new MeshInstance3D
        {
            Name = "BoxMesh",
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Layers = RenderLayers.SceneBox
        };
        AddChild(_meshInstance);
    }

    /// <summary>
    /// The buildable region is the document's, so it changes when one is opened.
    /// </summary>
    /// <remarks>
    /// The box sits exactly on the [0, Size] bounds — no geometric fudge. It is a
    /// backdrop: its material never writes depth (see CreateSeeBackMaterial), so
    /// content coplanar with a wall or the floor always wins without an epsilon
    /// offset.
    /// </remarks>
    public void SetSize(Vector3 size)
    {
        _meshInstance.Mesh = new BoxMesh { Size = size };
        _meshInstance.Position = size * 0.5f;
    }

    /// <summary>
    /// Whether the walls are a surface the brush can place against — which they
    /// are not while the element being painted sits off the scene's own cell grid.
    /// </summary>
    /// <remarks>
    /// What changes is the grid's line style — solid becomes dashed — and how much
    /// of its weight it carries. The walls keep their colour, the grid keeps its
    /// own, and the lines stay where they were, so the room looks like the same
    /// room and the ruler still measures: a dashed line is the ordinary way of
    /// drawing something that is there but not in force.
    /// </remarks>
    public void SetPaintable(bool paintable)
    {
        if (_paintable == paintable)
            return;

        _paintable = paintable;

        _material.SetShaderParameter("dash_period", paintable ? 0f : UnpaintableDashPeriod);
        _material.SetShaderParameter("dash_duty", UnpaintableDashDuty);
        _material.SetShaderParameter(
            "grid_strength",
            paintable ? GridStrength : GridStrength * UnpaintableGridFade);
    }

    /// <summary>
    /// What the room is painted. Its colour and nothing else — the grid, the
    /// dashes and the shading are the room's own and stay where they are.
    /// Scenery rather than lighting: the room casts no shadow and receives none,
    /// so this reaches nothing but itself, which is why it may follow the theme
    /// when the lights may not.
    /// </summary>
    public void SetRoomColor(Color color) => _material.SetShaderParameter("albedo", color);

    private static ShaderMaterial CreateSeeBackMaterial()
    {
        var shader = new Shader();
        shader.Code =
            """
            shader_type spatial;
            // Goxel EFFECT_SEE_BACK: hide exteriors, keep interiors.
            // depth_draw_never: this is a backdrop — it must never occupy the depth
            // buffer, so content flush with a wall/floor is never depth-fought or
            // (in Forward+) discarded at equal depth. Removes all epsilon offsets.
            render_mode cull_front, shadows_disabled, diffuse_lambert, specular_disabled, depth_draw_never;

            uniform vec4 albedo : source_color = vec4(0.8, 0.8, 1.0, 1.0);
            uniform float grid_strength = 0.5;
            // Length of one dash-plus-gap in cells. Zero draws the grid solid.
            uniform float dash_period = 0.0;
            // Fraction of that period which is drawn.
            uniform float dash_duty = 0.4;

            void fragment() {
                // Do not flip NORMAL here — with cull_front Godot already
                // shades backfaces correctly (manual flip inverted floor/ceil).

                vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
                // Use geometric (pre-shade) normal direction for choosing axes.
                vec3 nAbs = abs(normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz));

                vec2 uv;
                if (nAbs.y >= nAbs.x && nAbs.y >= nAbs.z) {
                    uv = world.xz;
                } else if (nAbs.x >= nAbs.z) {
                    uv = world.yz;
                } else {
                    uv = world.xy;
                }

                // Voxel grid: lines on integer world coordinates.
                vec2 fw = max(fwidth(uv), vec2(1e-4));
                vec2 dist = abs(uv - round(uv));
                vec2 line = 1.0 - smoothstep(vec2(0.0), fw * 1.25, dist);

                // Dashes run *along* each line, so the line at an integer u is
                // broken by v and the other way round — hence the swizzle. The
                // dash is centred on the period boundary, which at a period of one
                // cell is the corner: both directions are drawn there, so every
                // intersection is a cross and the lattice stays readable however
                // much of the edge between them is gap. As the pattern shrinks
                // below a pixel the antialiasing width swallows it and the line
                // fades towards its duty cycle instead of shimmering, which is the
                // right way to lose it.
                vec2 dash = vec2(1.0);
                if (dash_period > 0.0) {
                    vec2 along = uv.yx / dash_period;
                    vec2 phase = abs(fract(along) - 0.5);
                    vec2 soft = max(fwidth(along), vec2(1e-4));
                    vec2 onset = vec2(0.5 - dash_duty * 0.5);
                    dash = smoothstep(onset - soft, onset + soft, phase);
                }

                float grid = max(line.x * dash.x, line.y * dash.y);

                vec3 gridColor = albedo.rgb * 0.62;
                ALBEDO = mix(albedo.rgb, gridColor, clamp(grid * grid_strength, 0.0, 1.0));
                ROUGHNESS = 0.9;
                METALLIC = 0.0;
            }
            """;

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("grid_strength", GridStrength);
        material.SetShaderParameter("dash_period", 0f);
        material.SetShaderParameter("dash_duty", UnpaintableDashDuty);
        // Backdrop: draw before everything else.
        material.RenderPriority = -1;
        return material;
    }
}
