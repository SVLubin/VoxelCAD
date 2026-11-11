namespace VoxelCAD.View;

/// <summary>
/// How one facture's tile is generated. Both maps a finish needs come from the
/// same height field, so the bumps the normal map claims and the shading the
/// albedo shows can never disagree — which is exactly what goes wrong when an
/// albedo and a normal map are downloaded as two separate files.
/// </summary>
/// <param name="Size">Tile side in pixels. Always square — see the remarks.</param>
/// <param name="PeriodU">
/// For the noise patterns, how many lattice cells the tile holds across. With
/// <paramref name="PeriodV"/> it also sets the anisotropy: unequal periods give
/// streaks, and they do it without stretching a picture.
/// </param>
/// <param name="PeriodV">The same along the other axis.</param>
/// <param name="Octaves">One is plain noise; more stacks detail into it.</param>
/// <param name="Pattern">Where the height field comes from: sampled noise, or a drawn surface.</param>
/// <param name="Repeats">
/// For a drawn pattern, how many units of it fit across the tile. Ignored by the
/// noise patterns, whose scale is <paramref name="Frequency"/>.
/// </param>
/// <param name="UnitsPerTile">
/// How many of the pattern's own units span one tile edge — boards, courses,
/// panels, knurls. It is what turns the material's scale dial, which is measured
/// in <b>cells per unit</b>, into the cells the tile covers. Without it the dial
/// meant the tile, and since a tile holds one panel and six hammer marks, the same
/// number gave a panel and a hammer mark sizes six times apart: setting two
/// materials to the same tiling did not make their features the same size, which
/// is the one thing a tiling number is for.
/// </param>
/// <param name="Feature">
/// A proportion the pattern reads its own way: groove width for the tiled and
/// boarded surfaces, furrow width for bark. Ignored by the rest.
/// </param>
/// <param name="BumpStrength">How hard the height field is differentiated into a normal.</param>
/// <param name="NormalScale">How much of that normal the material actually applies.</param>
/// <param name="AlbedoFloor">
/// The darkest the modulation goes, as a fraction of the flat colour. Near one on
/// purpose: the facture is meant to be felt in the shading, not seen as a pattern,
/// and a CAD model still has to read cell by cell.
/// </param>
/// <remarks>
/// The tile is square, and that is a correction rather than a simplification.
/// Anisotropy used to come from a 256-by-26 image laid over a square face, which
/// worked for the picture and was wrong for the normal map: the slopes are
/// measured in texels, so once stretched the short axis came out ten times too
/// steep. Unequal lattice periods give the same streaks with the geometry right.
/// </remarks>
internal readonly record struct FinishRecipe(
    int Size,
    int PeriodU,
    int PeriodV,
    int Octaves,
    FinishPattern Pattern,
    float Repeats,
    float Feature,
    float UnitsPerTile,
    float BumpStrength,
    float NormalScale,
    float AlbedoFloor)
{
    /// <summary>A finish that generates nothing; <see cref="MaterialFinish.Smooth"/> alone.</summary>
    public static FinishRecipe None =>
        new(0, 0, 0, 0, FinishPattern.Noise, 0f, 0f, 0f, 0f, 0f, 0f);

    public bool HasTexture => Size > 0;

}
