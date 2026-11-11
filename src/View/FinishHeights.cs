using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// A facture's height field and the two numbers that say how to read it, for
/// anything outside this layer that has to draw the same surface.
/// </summary>
/// <remarks>
/// Public for one reason and no other, the way <see cref="ViewCubeGeometry"/> is:
/// the renderer draws the very factures the viewport draws, and a second table of
/// patterns would be two surfaces that look alike until somebody changes one. The
/// tracer cannot reach a <see cref="Godot.Texture2D"/> — it marches on a device of
/// its own — so what crosses is the field, not the picture made from it.
/// <para>
/// The heights are recomputed on each call rather than cached: this is asked once
/// per render, and the cache that exists (<see cref="FinishTextures"/>) holds
/// textures, which is a different thing to hold.
/// </para>
/// </remarks>
public static class FinishHeights
{
    /// <summary>
    /// The tile for one finish, in row-major order and in the range zero to one,
    /// or an empty array for a finish that has no facture.
    /// </summary>
    public static FinishHeightField Get(string name, MaterialFinish finish)
    {
        FinishRecipe recipe = FinishRecipes.GetRecipe(finish);

        // NOTE: a material wears the scan named after it, on the same terms as
        // FinishTextures.Get and through the same seam, so the tracer and the
        // viewport cannot come to wear two different things for one material.
        if (finish == MaterialFinish.Standard && StandardMaterialPack.Has(name))
            return StandardMaterialPack.LoadField(name);

        // Cached per finish, and the point of the cache is the array *instance*:
        // every material wearing a drawn facture is handed the very same heights,
        // so the atlas the tracer uploads can hold one copy of it however many
        // materials asked.
        if (DrawnByFinishDictionary.TryGetValue(finish, out FinishHeightField drawn))
            return drawn;

        FinishHeightField field = recipe.HasTexture
            ? new FinishHeightField(
                DrawnHeightField.Create(recipe),
                recipe.Size,
                recipe.AlbedoFloor,
                recipe.NormalScale,
                recipe.BumpStrength,
                recipe.UnitsPerTile)
            : FinishHeightField.None;

        DrawnByFinishDictionary[finish] = field;

        return field;
    }

    private static readonly Dictionary<MaterialFinish, FinishHeightField> DrawnByFinishDictionary = new();


}
