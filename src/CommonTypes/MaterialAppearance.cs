namespace VoxelCAD.CommonTypes;

/// <summary>
/// How a material takes light and what facture is laid over it. Six values, all
/// of them tunable — a preset is a shortcut that writes several at once, not a
/// mode the document remembers.
/// </summary>
/// <remarks>
/// There is deliberately no transparency here and no "kind". Transparency is the
/// colour's alpha, which is the only opacity this document has ever had, and the
/// kinds a first cut had — matte, plastic, metal, glass, frosted, emissive — are
/// points in this space rather than a dimension of their own. An enum beside
/// these numbers would be a second way to say the same thing, and the one that
/// could not be tuned.
/// </remarks>
/// <param name="Roughness">Zero is a mirror, one is chalk.</param>
/// <param name="Metallic">
/// One tints the reflection instead of the diffuse. Not a switch: at one there is
/// no diffuse term at all, so a face turned from every light goes black, which is
/// why the metal presets stop short of it.
/// </param>
/// <param name="Emission">Self-lit strength; zero is not self-lit at all.</param>
/// <param name="Finish">Which facture, <see cref="MaterialFinish.Smooth"/> for none.</param>
/// <param name="FinishScale">How many voxels one repeat of the facture spans.</param>
/// <param name="FinishRelief">How much of the facture's own relief is applied.</param>
/// <param name="Transparent">
/// Whether the material is see-through at all. It switches the whole of the
/// transparency machinery on — the alpha pass, the sorted instance, the refracted
/// path in the tracer, the seam rule that decides which of two touching cells
/// shows its face — and only then does the colour's alpha say <em>how</em>
/// see-through. Off, the material is opaque whatever its alpha happens to be.
/// <para>
/// Declared rather than inferred, and that is the point. "Below one" made an alpha
/// of 0.999 nobody chose put a material into the sorted pass and change which
/// faces of a model exist, silently and for the rest of the document's life; and a
/// mesher, an exporter and a tracer each had to make the same inference and agree
/// about it. One fact, asked in one place.
/// </para>
/// </param>
public readonly record struct MaterialAppearance(
    float Roughness,
    float Metallic,
    float Emission,
    MaterialFinish Finish,
    float FinishScale,
    float FinishRelief,
    bool Transparent = false)
{
    /// <summary>What every material made before this existed looked like.</summary>
    public static MaterialAppearance Default => new(
        Roughness: 1f,
        Metallic: 0f,
        Emission: 0f,
        Finish: MaterialFinish.Smooth,
        FinishScale: 2f,
        FinishRelief: 1f,
        Transparent: false);

    /// <summary>
    /// How opaque a surface of this material actually is: one unless it declares
    /// itself see-through, and then whatever the colour's alpha says.
    /// </summary>
    /// <remarks>
    /// One answer with four readers — the viewport's mesher, the exporter's, the
    /// material the viewport builds and the tracer's compile — because a model
    /// whose faces the screen and the file disagree about is the bug this exists
    /// to prevent. Here rather than in <c>Geometry</c> because that layer
    /// references nothing, and the flag this reads lives here.
    /// </remarks>
    public float OpacityOf(float alpha) => Transparent ? alpha : 1f;
}
