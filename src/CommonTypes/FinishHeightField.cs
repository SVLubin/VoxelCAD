using System;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// One facture's tile as numbers rather than as a picture: the heights, and the
/// three values that say how to read them.
/// </summary>
/// <remarks>
/// Here rather than in the view for the reason <c>PreferenceValues</c> is: it
/// crosses the view seam. What crosses out of the view for the renderer. A <c>Texture2D</c> could not:
/// the tracer marches on a rendering device of its own and has no sampler to hand
/// one to. The heights are what both maps the viewport uses are differentiated
/// and shaded from, so a render made from these describes the same surface.
/// </remarks>
/// <param name="Heights">Row-major, nought to one, <paramref name="Size"/> square.</param>
/// <param name="AlbedoFloor">What the lowest point of the field dims the colour to.</param>
/// <param name="NormalScale">How much of the relief the material applies.</param>
/// <param name="BumpStrength">
/// How steep the field's own slopes are taken to be when it is differentiated.
/// </param>
/// <param name="Colors">
/// NOTE: The tile's own colour, linear, three floats a texel in the same
/// row-major order as <paramref name="Heights"/> — or empty, which is what every
/// drawn facture supplies. A drawn facture's albedo is a monochrome scale of its
/// height, so the tracer forms it from the height and needs nothing more; a
/// scanned one *is* a colour, and that colour is the whole reason for having it.
/// Where it is supplied it replaces the material's own rather than multiplying it,
/// which is what the viewport does too — a brick photograph times a brick-red
/// material is twice as red.
/// </param>
/// <param name="Roughness">
/// NOTE: Gloss per texel, one float a texel, or empty. The one channel a
/// height field has no opinion about at all: mortar is matte where the brick
/// beside it is not.
/// </param>
/// <param name="Occlusion">
/// NOTE: How much of the sky one texel can see, or empty. It is the shading
/// the surface's own relief owes itself *below the texel*, which is the one part
/// of the occlusion a tracer cannot compute for itself — everything larger it
/// works out by following the light. So it attenuates what arrives from the whole
/// hemisphere and never the sun, whose direction is known and which a map of one
/// number per texel cannot have an opinion about.
/// </param>
/// <param name="Metal">
/// Which parts of the surface are metal, or empty. It has to cross with the rest:
/// a material whose own metallic dial is open so that the map may govern would
/// otherwise be a mirror everywhere the map is not read.
/// </param>
/// <param name="UnitsPerTile">
/// How many of the pattern's own units span one tile edge. The material's scale is
/// measured in cells per <b>unit</b>, so this is what turns it into the cells the
/// tile covers — and it crosses the seam because the tracer has to reach the same
/// product the viewport does, or a render would tile a facture differently from
/// the screen it was framed on.
/// </param>
public readonly record struct FinishHeightField(
    float[] Heights,
    int Size,
    float AlbedoFloor,
    float NormalScale,
    float BumpStrength,
    float UnitsPerTile,
    float[]? Colors = null,
    float[]? Roughness = null,
    float[]? Occlusion = null,
    float[]? Metal = null)
{
    public static FinishHeightField None =>
        new(Array.Empty<float>(), 0, 1f, 0f, 0f, 1f);

    public bool HasField => Size > 0;

    public bool HasColor => Colors is { Length: > 0 };

    public bool HasRoughness => Roughness is { Length: > 0 };

    public bool HasOcclusion => Occlusion is { Length: > 0 };

    public bool HasMetal => Metal is { Length: > 0 };

    /// <summary>
    /// How many floats one texel takes in the uploaded atlas: the height, then the
    /// colour, then the gloss. Carried rather than assumed so a field that has only
    /// heights costs only heights — a drawn tile is 256 square, and giving every
    /// one of them three colour channels it does not use would quadruple what is
    /// uploaded for nothing.
    /// <para>
    /// The layout is fixed rather than a set of flags — height, then colour, then
    /// gloss, then occlusion — so the shader can index a channel by a constant
    /// instead of carrying an offset per channel. A field with a later channel and
    /// no earlier one is therefore not expressible, which is what every scanned set
    /// supplies and what no drawn one does.
    /// </para>
    /// </summary>
    public int Stride => HasColor
        ? HasRoughness ? HasOcclusion ? HasMetal ? 7 : 6 : 5 : 4
        : 1;
}
