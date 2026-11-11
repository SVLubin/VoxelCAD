namespace VoxelCAD.View;

/// <summary>
/// The one shader every shipped material is drawn with: it picks a layer from the
/// arrays by a number the vertex carries, and writes the channels the engine's own
/// lighting reads.
/// </summary>
/// <remarks>
/// A shader of ours, and that is allowed where faking light is not: this sets
/// ALBEDO, NORMAL_MAP, ROUGHNESS, METALLIC and AO and then lets Godot light them,
/// which is the same PBR every other surface in this editor answers to. Nothing
/// here shades anything.
/// <para>
/// The projection is chosen rather than blended, which is the one liberty voxels
/// buy: every face is axis-aligned, so exactly one of the three planes is facing
/// the viewer and mixing in the other two would only soften what is already
/// exact. That also makes it cheap — one sample per map instead of three.
/// </para>
/// </remarks>
internal static class StandardMaterialShader
{
    public const string Source = @"
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2DArray surface_maps : source_color, filter_nearest_mipmap, repeat_enable;
uniform sampler2DArray relief_maps : filter_nearest_mipmap, repeat_enable;
uniform float relief_scale = 1.0;

// The viewport's optional effects, as multipliers rather than branches: a shader
// that took an `if` per fragment for a setting nobody changes twice a session
// would be paying for the switch on every pixel to save nothing.
uniform float occlusion = 1.0;
uniform float relief = 1.0;

varying flat float layer;
varying vec3 placed;
varying vec3 facing;

void vertex()
{
    // The layer the whole triangle reads, and the cells one tile of its material
    // spans. Both flat, because every vertex of a face wears one material and
    // interpolating either would ask for the value between two of them — and both
    // per vertex rather than as uniforms, because this surface is shared by every
    // shipped material in the chunk.
    layer = CUSTOM0.x;

    // Godot's own triplanar convention, so a shipped material runs the same way
    // here as a drawn one does through StandardMaterial3D.
    placed = VERTEX / CUSTOM0.y * vec3(1.0, -1.0, 1.0);
    facing = abs(NORMAL);
}

void fragment()
{
    // One plane, not a blend: a voxel face is axis-aligned, so one of the three
    // is exactly facing and the other two contribute nothing but softness.
    vec2 uv;

    if (facing.x >= facing.y && facing.x >= facing.z)
        uv = placed.zy;
    else if (facing.y >= facing.z)
        uv = placed.xz;
    else
        uv = placed.xy;

    vec4 surface = texture(surface_maps, vec3(uv, layer));
    vec4 maps = texture(relief_maps, vec3(uv, layer));

    // The vertex colour is the mesher's ambient occlusion and nothing else, so
    // ignoring it is exactly a surface that keeps its colour into its creases.
    ALBEDO = surface.rgb * mix(vec3(1.0), COLOR.rgb, occlusion);
    ROUGHNESS = surface.a;

    // Two channels of the normal; the third is what is left of a unit vector, so
    // the map carries two numbers where it would otherwise carry three.
    vec2 tilt = maps.rg * 2.0 - 1.0;
    NORMAL_MAP = vec3(tilt * relief_scale * relief, sqrt(max(1.0 - dot(tilt, tilt), 0.0))) * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 1.0;

    // The map's own occlusion goes with the baked one: both are the same fact
    // about the same surface, measured at two different sizes.
    AO = mix(1.0, maps.b, occlusion);
    AO_LIGHT_AFFECT = 0.0;
    METALLIC = maps.a;
}
";
}
