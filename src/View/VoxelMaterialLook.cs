using VoxelCAD.CommonTypes;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Every decision <see cref="VoxelMaterialFactory"/> takes about a colour and an
/// appearance, worked out without an engine. The factory then does nothing but
/// write these onto a <see cref="StandardMaterial3D"/>.
/// </summary>
/// <remarks>
/// Split out because a <see cref="StandardMaterial3D"/> cannot be constructed in a
/// test host — measured: the host crashes on the first one — so as long as the
/// arithmetic lived inside the factory it could only ever be verified by taking a
/// photograph. That is not hypothetical. <see cref="EmissionEnergy"/> shipped
/// without the opacity in it, and a self-lit material at alpha 0.4 came back a flat
/// opaque slab; it was found by eye and, until this, held by nothing. The rule is
/// the one the panels already follow: keep the decisions out of the part that needs
/// an engine.
/// </remarks>
/// <param name="Blends">
/// Whether the surface goes through the alpha pipeline. The material declares
/// that it is see-through and its alpha then says how much; an alpha on a
/// material that has not declared it means nothing at all.
/// </param>
/// <param name="Shines">
/// Whether the specular term is worth computing. A perfectly rough dielectric has
/// no highlight, and switching the term off there is what keeps a matte voxel
/// looking exactly as it did before any of this existed.
/// </param>
/// <param name="EmissionEnergy">
/// How brightly it is self-lit; nought is not self-lit at all.
/// </param>
/// <param name="FinishPeriod">
/// How many cells one repeat of the facture spans, or nought for no facture — in
/// which case the texture, the normal map and the triplanar mapping are all left
/// off rather than applied at strength nought.
/// </param>
/// <param name="NormalScale">How deep the facture's relief is driven.</param>
/// <param name="Occluded">
/// Whether the ambient occlusion the mesher baked into the vertex colours is read
/// at all. It is a multiplier on the albedo, so ignoring it is exactly a surface
/// that keeps its colour into its creases.
/// </param>
internal readonly record struct VoxelMaterialLook(
    bool Blends,
    bool Shines,
    float EmissionEnergy,
    float FinishPeriod,
    float NormalScale,
    bool Occluded)
{
    /// <summary>
    /// What an emission of one is worth as a Godot energy multiplier. A number
    /// chosen by looking at a lamp beside a lit wall, which is the only way there
    /// is to choose it.
    /// </summary>
    public const float EmissionGain = 1.6f;

    /// <summary>Whether this facture is worth applying at all.</summary>
    public bool WearsFinish => FinishPeriod > 0f;

    /// <summary>
    /// The material as the document describes it, with every optional effect
    /// running — what a preview shows.
    /// </summary>
    public static VoxelMaterialLook From(Color color, MaterialAppearance appearance) =>
        From(color, appearance, ViewportEffects.Full);

    /// <summary>
    /// The material as a viewport running <paramref name="effects"/> draws it.
    /// </summary>
    /// <param name="scanned">
    /// Whether the maps are the shipped pack's rather than drawn from a recipe.
    /// A scan has no recipe, so without this it answers a period of nought and a
    /// shipped material comes out blank — which is exactly what it did.
    /// </param>
    public static VoxelMaterialLook From(
        Color color,
        MaterialAppearance appearance,
        ViewportEffects effects,
        bool scanned = false)
    {
        // A facture switched off is a period of nought, which is already the whole
        // of "no facture" here: the texture, the normal map and the triplanar
        // mapping are left off rather than applied at strength nought.
        float period = effects.Factures
            ? View.FinishPeriod.GetCells(appearance, scanned)
            : 0f;

        return new VoxelMaterialLook(
            Blends: appearance.OpacityOf(color.A) < 1f,
            Shines: appearance.Roughness < 1f || appearance.Metallic > 0f,

            // Scaled by the opacity, because emission is added *after* the blend and
            // so does not fade with the surface carrying it. A glowing pane does add
            // its own light whatever its opacity — that is the physics — but a colour
            // the document has already been told to see through must not come back
            // solid, and between the two readings the one the editor owes its user is
            // the one they asked for.
            //
            // The *declared* opacity, never the raw alpha: an opaque material may be
            // carrying an alpha nobody chose, and reading it here would dim a lamp
            // that was never asked to be see-through. Same deduction the flag was
            // added to remove, surviving in one more reader.
            EmissionEnergy: appearance.Emission * EmissionGain * appearance.OpacityOf(color.A),

            FinishPeriod: period,
            // A scan's normal map is authored at full strength, so the dial is the
            // whole of it; a drawn one is scaled by the recipe that drew it.
            NormalScale: period <= 0f
                ? 0f
                : scanned
                    ? appearance.FinishRelief
                    : FinishRecipes.GetRecipe(appearance.Finish).NormalScale * appearance.FinishRelief,

            Occluded: effects.Occlusion);
    }
}
