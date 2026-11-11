using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// How many cells one repeat of a facture covers, given a material's scale. One
/// answer with several readers: the material maps its tile over that many cells,
/// the preview sizes its block from it, and the tracer restates the same product.
/// </summary>
/// <remarks>
/// Shared rather than restated, because the two are only meaningful together. The
/// preview used to be three cells to a side whatever the material said, and its own
/// comment promised that was "enough for a facture to repeat" — true at a period of
/// two, and false for Brick, Timber, PlankFloor and Parquet, which are four, and
/// for anything the scale dial takes past three. So the preview showed three
/// quarters of a plank pattern and a placed model showed the rest, which is exactly
/// the mismatch a preview exists to prevent.
/// </remarks>
internal static class FinishPeriod
{
    /// <summary>
    /// The tile's period in cells: the dial, which is measured in cells per
    /// <b>unit</b> of the pattern, times how many units the tile holds. Zero when
    /// the material wears no facture at all.
    /// </summary>
    /// <remarks>
    /// The multiplication is the whole point. The dial used to be the tile itself,
    /// and a tile holds one panel or six hammer marks depending on the finish — so
    /// two materials set to the same number came out with features six times apart,
    /// and a tiling number that does not make things the same size is not a tiling
    /// number. Measured in units, a scale of two means a board two cells wide, a
    /// brick course two cells tall and a panel two cells across, whichever finish
    /// is wearing it.
    /// </remarks>
    /// <param name="scanned">
    /// Whether the material's maps come from the shipped pack rather than being
    /// drawn. A scan has no recipe — there is nothing to generate — so it would
    /// otherwise answer nought and the maps would be loaded and thrown away,
    /// which is what left every shipped material previewing as a blank cube. Its
    /// tile is one unit by definition: the pack's 64 texels are the pattern.
    /// </param>
    public static float GetCells(MaterialAppearance appearance, bool scanned = false)
    {
        if (appearance.FinishRelief <= 0f)
            return 0f;

        if (scanned)
            return appearance.FinishScale;

        FinishRecipe recipe = FinishRecipes.GetRecipe(appearance.Finish);

        if (!recipe.HasTexture)
            return 0f;

        return appearance.FinishScale * recipe.UnitsPerTile;
    }

    /// <summary>Below this a preview cube has no room to show its own corners.</summary>
    public const int MinimumPreviewCells = 3;

    /// <summary>
    /// Past this a preview block is more voxels than a preview is worth meshing:
    /// the dial reaches sixteen cells to a unit and a tile can hold six of them,
    /// so the whole tile can be ninety-six cells across — a block of nearly a
    /// million. Past the cap the block shows part of the pattern rather than all
    /// of it, which is the honest trade at a scale nobody works at.
    /// </summary>
    public const int MaximumPreviewCells = 12;

    /// <summary>
    /// How many cells to a side a preview block needs: one more than the period, so
    /// a face carries the whole pattern <b>and</b> the start of the next repeat,
    /// which is what makes a tiling facture legible as one rather than as a
    /// decoration that happens to end at the edge.
    /// </summary>
    public static int GetPreviewCells(MaterialAppearance appearance) =>
        Math.Clamp(
            (int)Mathf.Ceil(GetCells(appearance)) + 1,
            MinimumPreviewCells,
            MaximumPreviewCells);
}
