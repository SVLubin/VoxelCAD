namespace VoxelCAD.CommonTypes;

/// <summary>
/// The surface facture laid over a material: a tiling height field that supplies
/// a normal map and a faint albedo modulation. Independent of
/// the shading numbers beside it — brushed glass is odd rather than illegal, and
/// refusing it would be taste dressed as an invariant.
/// </summary>
/// <remarks>
/// Which picture a finish expands to is the view's business and lives there; the
/// document carries only the choice. Editor-only by decision either way: nothing
/// here reaches an export, because carrying it there means UVs in the export IR
/// and image files beside the mesh.
/// </remarks>
public enum MaterialFinish
{
    /// <summary>No facture at all — the flat colour, and the default.</summary>
    Smooth,

    /// <summary>
    /// One of the materials the editor ships with, named by the material itself.
    /// The maps are a photograph rather than a drawn field, so this says only
    /// "look me up by name" — what the surface is is the scan's business, and the
    /// scale dial means cells to one tile of it.
    /// </summary>
    Standard,

    /// <summary>Fine even granulation: sand, cast powder, sugar.</summary>
    Sand,

    /// <summary>Isotropic grain: stone, concrete, cast surfaces.</summary>
    Rough,

    /// <summary>Broad directional streaks: extruded stock, drawn or pulled surfaces.</summary>
    Grain,

    /// <summary>Growth rings: the face of a sawn plank, with its arches.</summary>
    Wood,

    /// <summary>Bark: deep furrows running up the trunk, broken into plates.</summary>
    Bark,

    /// <summary>A laid floor of staggered boards.</summary>
    Laminate,

    /// <summary>Brickwork: staggered courses, mortar joints, a face without fibre.</summary>
    Brick,

    /// <summary>Parquet in herringbone.</summary>
    Parquet,

    /// <summary>Fine directional streaks: machined and polished metal.</summary>
    Brushed,

    /// <summary>Cellular dents: forged, beaten, hand-worked metal.</summary>
    Hammered,

    /// <summary>Anti-slip checker plate: paired lentils at alternating angles.</summary>
    Tread,

    /// <summary>Corrugated sheet: parallel rounded ridges.</summary>
    Ribbed,

    /// <summary>Large panels parted by chamfered grooves.</summary>
    Tiles,

    /// <summary>Small square tiles with proportionally wide grout — pool mosaic.</summary>
    Mosaic,

    /// <summary>The crossed knurl of a grip.</summary>
    Knurled,

    /// <summary>Glass block with parallel waves — the common stairwell block.</summary>
    GlassWave,

    /// <summary>Glass block with crossed reeding.</summary>
    GlassCross,

    /// <summary>Glass block pressed with round dimples.</summary>
    GlassBubble,

    /// <summary>
    /// The swell on still water: a few long waves crossing, shallow enough to
    /// bend a reflection rather than break it.
    /// </summary>
    Ripples
}
