using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Render;

/// <summary>
/// One material as the tracer needs it: what a path tracer actually asks a
/// surface for, with the document's own <see cref="MaterialAppearance"/>
/// and colour folded into them.
/// </summary>
/// <remarks>
/// The facture crosses as its <em>identity</em> and how it tiles; the field
/// itself is supplied to the tracer separately, because it comes from the view
/// and the compile has no business holding a picture.
/// </remarks>
/// <param name="Name">
/// What the facture this material wears is filed under. The material's own name
/// rather than its finish, because a downloaded set belongs to the material that
/// named it and a finish is an enum with room for a handful.
/// </param>
/// <param name="Albedo">Base colour, linear.</param>
/// <param name="Opacity">
/// One is opaque; below one the surface refracts. Taken from the material's own
/// declaration rather than from its alpha — a colour may carry an alpha the
/// material has said means nothing.
/// </param>
/// <param name="Roughness">Zero is a mirror, one is chalk.</param>
/// <param name="Metallic">One tints the reflection instead of the diffuse.</param>
/// <param name="Emission">Self-lit radiance multiplier; zero is not self-lit.</param>
/// <param name="Finish">Which facture is laid over it.</param>
/// <param name="Scale">
/// How many cells one repeat spans, or nought for the finish's own answer —
/// which lives with the field and not with the material.
/// </param>
/// <param name="Relief">How much of the facture's own relief is applied.</param>
internal readonly record struct RenderMaterial(
    string Name,
    Color Albedo,
    float Opacity,
    float Roughness,
    float Metallic,
    float Emission,
    MaterialFinish Finish,
    float Scale,
    float Relief)
{
    /// <summary>How many floats one material occupies in the GPU buffer.</summary>
    public const int Floats = 16;

    /// <summary>
    /// The document's colour is written in sRGB — it is what a picker offered and
    /// what the viewport's own materials are given — and light is linear. Without
    /// this the render is the same scene under a different gamma, which reads as
    /// the wrong colours rather than as a mistake anybody could name.
    /// </summary>
    public static RenderMaterial From(
        string name,
        Color color,
        MaterialAppearance appearance) =>
        new(
            Name: name,
            Albedo: color.SrgbToLinear(),
            Opacity: appearance.OpacityOf(color.A),
            Roughness: appearance.Roughness,
            Metallic: appearance.Metallic,
            Emission: appearance.Emission,
            Finish: appearance.Finish,
            Scale: appearance.FinishScale,
            Relief: appearance.FinishRelief);

    /// <summary>
    /// Writes this material into a float buffer at <paramref name="offset"/>, with
    /// the facture resolved to where its tile actually sits in the uploaded atlas.
    /// A field of no size is a material with no facture, which is most of them.
    /// </summary>
    public void Write(float[] buffer, int offset, FinishHeightField field, int atlasOffset)
    {
        buffer[offset + 0] = Albedo.R;
        buffer[offset + 1] = Albedo.G;
        buffer[offset + 2] = Albedo.B;
        buffer[offset + 3] = Opacity;

        buffer[offset + 4] = Roughness;
        buffer[offset + 5] = Metallic;
        buffer[offset + 6] = Emission;
        // Cells per unit times units to the tile — the very product
        // FinishPeriod.GetCells forms for the viewport's own materials, restated
        // here because the two must tile a facture identically.
        buffer[offset + 7] = Math.Max(Scale * field.UnitsPerTile, 0.01f);

        buffer[offset + 8] = field.NormalScale * Relief;
        buffer[offset + 9] = field.AlbedoFloor;
        buffer[offset + 10] = field.BumpStrength;

        // How much of the hemisphere this surface can see, before any facture has
        // had its say. One for everything without an occlusion map, which is every
        // material the shader will not ask about — the value has to be here rather
        // than defaulted in the shader, because a facture overwrites it and a
        // material without one never reaches that code at all.
        buffer[offset + 11] = 1f;

        bool wears = field.HasField && Relief > 0f;

        buffer[offset + 12] = System.BitConverter.Int32BitsToSingle(wears ? atlasOffset : 0);
        buffer[offset + 13] = System.BitConverter.Int32BitsToSingle(wears ? field.Size : 0);

        // Spare, kept so the struct stays four vec4s wide.
        buffer[offset + 14] = System.BitConverter.Int32BitsToSingle(0);

        // How many floats a texel takes, so the shader can index a tile that
        // carries a colour and a gloss beside its height without a second buffer
        // and a second binding — and without a drawn tile paying for channels it
        // does not have.
        buffer[offset + 15] = System.BitConverter.Int32BitsToSingle(wears ? field.Stride : 1);
    }
}
