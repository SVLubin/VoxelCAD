using System.Collections.Generic;
using System.Globalization;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Export;

/// <summary>
/// One MagicaVoxel palette entry read as a material of this document: the colour
/// from the <c>RGBA</c> chunk, and whatever its <c>MATL</c> dictionary meant that
/// this document can hold.
/// </summary>
/// <remarks>
/// <b>Every key is gated on <c>_type</c>, and that is the format's chief trap.</b>
/// MagicaVoxel writes a <c>MATL</c> for all 256 entries whether or not anybody
/// touched one, and it writes keys that do not apply to the type it also wrote —
/// <c>teardown.vox</c> carries <c>_alpha 0.5</c> on all of its plain diffuse
/// materials. Reading a key without asking whether its type admits it turns an
/// ordinary file half transparent.
/// <para>
/// <b>A diffuse material's <c>_rough</c> is not read at all.</b> MagicaVoxel's own
/// material panel offers no roughness for a diffuse material — the 0.1 every
/// untouched entry carries is a default nobody chose — while ours is a single
/// GGX dial on which 0.1 is very nearly a mirror. Read literally, every stock
/// MagicaVoxel file would arrive glossier than this editor's Steel preset.
/// </para>
/// </remarks>
internal static class VoxMaterialReader
{
    /// <summary>
    /// MagicaVoxel's roughness slider at its top does not look like our chalk, so
    /// the range is halved on the way in. A borrowed visual calibration rather
    /// than a measurement of ours — see <c>docs/import.md</c>.
    /// </summary>
    private const float RoughnessScale = 0.5f;

    /// <summary>
    /// A fully transparent material reads as a broken import rather than as glass,
    /// so the very last of the opacity is kept. MagicaVoxel would draw it
    /// invisible; a user looking for their model would not thank us for agreeing.
    /// </summary>
    private const float MinimumOpacity = 0.02f;

    /// <summary>What an entry the file described no material for is read as.</summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyProperties =
        new Dictionary<string, string>();

    public static ImportedMaterial Read(
        int colorIndex,
        (byte R, byte G, byte B, byte A) rgba,
        IReadOnlyDictionary<string, string>? properties,
        bool fileHasMaterials)
    {
        var color = new Color(rgba.R / 255f, rgba.G / 255f, rgba.B / 255f, 1f);
        var name = $"Palette {colorIndex:D3}";

        // The question is whether the *file* describes its materials, not whether
        // this entry has one. Where it does, MagicaVoxel always writes an alpha of
        // 255 and means nothing by it, so the alpha is ignored for every entry —
        // including one the file left out, which is an ordinary diffuse material
        // rather than a reason to start reading the alpha again.
        if (!fileHasMaterials)
            return Untyped(name, color, rgba.A);

        properties ??= EmptyProperties;

        string type = properties.TryGetValue("_type", out string? declared) ? declared : "_diffuse";
        bool metallic = type is "_metal" or "_blend";
        bool transmissive = type is "_glass" or "_blend";
        bool emissive = type == "_emit";

        // Which keys a type admits is MagicaVoxel's own panel, read from the copy
        // it ships as text: the Surface group — roughness and index of refraction —
        // is offered for Metal, Glass and Blend, and for nothing else. A diffuse
        // material has no roughness there, and neither has an emitter or a cloud.
        bool surfaced = metallic || transmissive;

        // MagicaVoxel up to 0.99.5 wrote the strength of whichever effect the type
        // named as _weight; later versions gave each its own key. It is a fallback
        // for those and never a value in its own right — see Strength.
        float? legacy = Optional(properties, "_weight");

        MaterialAppearance appearance = MaterialAppearance.Default with
        {
            Roughness = surfaced
                ? Number(properties, "_rough", 0.1f) * RoughnessScale
                : MaterialAppearance.Default.Roughness,
            Metallic = metallic ? Strength(properties, "_metal", type == "_metal", legacy) : 0f,
            Emission = emissive ? Strength(properties, "_emit", statement: true, legacy) : 0f
        };

        if (!transmissive)
            return new ImportedMaterial(name, color, appearance);

        // _trans and _alpha are the same dial under two names, and the dial is
        // labelled Transparency in MagicaVoxel's own panel — so it is the
        // complement of the opacity this document stores. Absent, a material is
        // opaque: transparency is the one of the three whose off position is a
        // number rather than the absence of the type.
        float transparency = Optional(properties, "_trans")
            ?? Optional(properties, "_alpha")
            ?? (type == "_glass" ? legacy ?? 0f : 0f);

        if (transparency <= 0f)
            return new ImportedMaterial(name, color, appearance);

        return new ImportedMaterial(
            name,
            color with { A = Mathf.Max(1f - transparency, MinimumOpacity) },
            appearance with { Transparent = true });
    }

    /// <summary>
    /// What a file with no <c>MATL</c> chunk anywhere means. There the colour's
    /// own alpha is the only thing that could carry an opacity, and something has
    /// to read it: this editor's own <c>.vox</c> writer puts one there, so
    /// ignoring it would break a round trip through our own format.
    /// </summary>
    /// <remarks>
    /// The reverse rule holds too, and matters more: where a file <em>does</em>
    /// carry materials, MagicaVoxel always writes an alpha of 255 and means
    /// nothing by it, so the alpha is ignored outright and <c>MATL</c> decides.
    /// </remarks>
    private static ImportedMaterial Untyped(string name, Color color, byte alpha) =>
        alpha == byte.MaxValue
            ? new ImportedMaterial(name, color, MaterialAppearance.Default)
            : new ImportedMaterial(
                name,
                color with { A = alpha / 255f },
                MaterialAppearance.Default with { Transparent = true });

    /// <summary>
    /// How strongly a type's own effect applies: its own key, then the one key
    /// older files carried, and then — only where the type is itself the
    /// statement — fully.
    /// </summary>
    /// <remarks>
    /// <paramref name="statement"/> is what separates <c>_metal</c> from
    /// <c>_blend</c>, and getting it wrong is expensive: <c>_blend</c> is the
    /// modern general type whose dials say everything, so a <c>_blend</c> with no
    /// <c>_metal</c> key means <em>not metal</em>. Reading the absent key as full
    /// strength made every <c>_blend</c> material a mirror — measured on
    /// MagicaVoxel's own material showcase, where all three plainly dielectric
    /// samples and all four glass ones came in fully metallic. <c>_metal</c> is
    /// the other way round: an old file that names the type and no strength means
    /// metal, because in that spelling the type was the only thing to say.
    /// </remarks>
    private static float Strength(
        IReadOnlyDictionary<string, string> properties,
        string key,
        bool statement,
        float? legacy)
    {
        float? own = Optional(properties, key);

        if (own.HasValue)
            return own.Value;

        return statement ? legacy ?? 1f : 0f;
    }

    private static float Number(
        IReadOnlyDictionary<string, string> properties,
        string key,
        float fallback) => Optional(properties, key) ?? fallback;

    /// <summary>The number a key holds, or nothing where it is absent or unreadable.</summary>
    /// <remarks>
    /// "NaN" and "Infinity" parse, and nothing downstream removes them: the model
    /// clamps an appearance on the way in, and clamping a NaN answers NaN. One
    /// would then sit in the document and be saved with it. A property that is not
    /// a finite number is not a number the file meant.
    /// </remarks>
    private static float? Optional(IReadOnlyDictionary<string, string> properties, string key) =>
        properties.TryGetValue(key, out string? text)
        && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
        && float.IsFinite(value)
            ? value
            : null;
}
