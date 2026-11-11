using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Builds a facture's two maps and hands out the same pair to every material that
/// asks for it. A tile is identical for everyone using the finish, so generating
/// one per material would be the same picture computed many times.
/// </summary>
/// <remarks>
/// One path for every finish, noise included. Godot's <c>NoiseTexture2D</c> was
/// what generated the stochastic ones, and it was dropped for a specific reason:
/// its <c>Seamless</c> mode blends a skirt across the join, which <b>hides</b> a
/// seam rather than removing one — and, being outside
/// <see cref="DrawnHeightField.Sample"/>, those five finishes were the ones the
/// tiling test could not see. Generating them here from a lattice that wraps
/// makes the guarantee whole and the test cover everything.
/// </remarks>
internal static class FinishTextures
{
    private static readonly Dictionary<string, FinishTexturePair> PairsByKeyDictionary = new();

    /// <summary>
    /// The albedo modulation and the normal map for a finish, both null for
    /// <see cref="MaterialFinish.Smooth"/>.
    /// </summary>
    public static FinishTexturePair Get(string name, MaterialFinish finish)
    {
        // A scanned set belongs to the material that named it; a drawn one belongs
        // to the finish and is shared by every material wearing it.
        bool shipped = finish == MaterialFinish.Standard && StandardMaterialPack.Has(name);
        string key = shipped ? $"standard:{name}" : $"drawn:{finish}";

        if (PairsByKeyDictionary.TryGetValue(key, out FinishTexturePair cached))
            return cached;

        FinishTexturePair pair = shipped
            ? StandardMaterialPack.Load(name)
            : Create(FinishRecipes.GetRecipe(finish));

        PairsByKeyDictionary[key] = pair;

        return pair;
    }

    private static FinishTexturePair Create(FinishRecipe recipe) =>
        recipe.HasTexture
            ? HeightFieldTextures.Create(DrawnHeightField.Create(recipe), recipe)
            : default;

}
