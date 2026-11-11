namespace VoxelCAD.View;

/// <summary>
/// Where a facture's height field comes from. The split is not decorative: noise
/// is sampled and a manufactured surface is <b>drawn</b>, and the two need
/// different generators. A tread plate has lentils in a known place, at a known
/// angle, and no amount of noise tuning produces one.
/// </summary>
internal enum FinishPattern
{
    /// <summary>Gradient noise, optionally stacked into octaves.</summary>
    Noise,

    /// <summary>Worley cells.</summary>
    Cellular,

    /// <summary>Fine parallel marks: scratches, fibre, drawn or brushed stock.</summary>
    Streaks,

    /// <summary>Pairs of raised lentils at alternating angles — anti-slip floor plate.</summary>
    Tread,

    /// <summary>Parallel rounded ridges — corrugated sheet.</summary>
    Ribbed,

    /// <summary>A grid of grooves — panels, tiles, plating with visible seams.</summary>
    Tiles,

    /// <summary>Crossed diagonal ridges — the knurl on a grip.</summary>
    Knurled,

    /// <summary>Growth rings warped into the arches of a sawn plank.</summary>
    Wood,

    /// <summary>Deep vertical furrows broken into plates — bark.</summary>
    Bark,

    /// <summary>Staggered boards with seams and grain — a laid floor.</summary>
    Laminate,

    /// <summary>The same courses at a brick's proportions, with a roughened face.</summary>
    Brick,

    /// <summary>Herringbone: planks meeting at right angles, alternating.</summary>
    Parquet,

    /// <summary>Glass block, moulded with parallel waves.</summary>
    GlassWave,

    /// <summary>Glass block, moulded with crossed reeding.</summary>
    GlassCross,

    /// <summary>Glass block, moulded with a field of round dimples.</summary>
    GlassBubble,

    /// <summary>Long waves crossing on still water.</summary>
    Ripples
}
