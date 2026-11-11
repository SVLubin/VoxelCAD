using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The maps a facture wears. Albedo and normal come from one height field for a
/// drawn finish; everything is null for <see cref="MaterialFinish.Smooth"/>, which
/// is the whole of what "no facture" has to mean.
/// </summary>
/// <param name="Roughness">
/// Null for every drawn finish: a height field has no opinion about
/// gloss, so a drawn facture cannot say that mortar is matte where the brick beside
/// it is not. A scanned set can, and this is where it arrives.
/// </param>
/// <param name="Occlusion">
/// NOTE: The shading the surface's own relief owes itself, which the mesher's
/// per-corner occlusion cannot see — it measures cells, and a mortar joint is not a
/// cell. Ambient only, so nothing it does can contradict a real light.
/// </param>
/// <param name="Metallic">
/// NOTE: Which parts of the surface are metal. Present and black for every
/// stone in this set, which is the correct answer rather than an unused map.
/// </param>
internal readonly record struct FinishTexturePair(
    Texture2D? Albedo,
    Texture2D? Normal,
    Texture2D? Roughness = null,
    Texture2D? Occlusion = null,

    // No Height. The pack ships a displacement map and this used to carry it —
    // never assigned by anything that built a pair, and never read. What is done
    // with the displacement is done at the pack: it is differentiated into
    // Normal, because Godot's own parallax works on UV1 in a tangent frame and
    // triplanar has neither, so the scalar itself could not have been used here
    // even if something had filled it in.
    Texture2D? Metallic = null)
{
    public bool HasTexture => Albedo is not null;

    /// <summary>
    /// Whether the albedo map carries the surface's own colour rather than a grey
    /// modulation of the material's. A drawn facture's albedo is a shade to be
    /// multiplied; a photograph's is the colour itself, and multiplying a brick
    /// photograph by a brick-red material would make it twice as red.
    /// </summary>
    public bool CarriesColor => Roughness is not null;
}
