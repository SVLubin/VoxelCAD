namespace VoxelCAD.CommonTypes;

/// <summary>
/// A ready-made material: a bundle of appearance values and a colour, applied in
/// one act. Deliberately not stored — which preset a material matches is asked of
/// its values, so a material tuned back onto a preset's numbers is that preset
/// again and nothing can go stale.
/// </summary>
public enum MaterialPreset
{
    Stone,
    Concrete,
    Sand,
    Plaster,
    Brick,
    Tile,
    Mosaic,

    Timber,
    PlankFloor,
    Parquet,
    Bark,

    Steel,
    BrushedSteel,
    HammeredMetal,
    TreadPlate,
    Corrugated,
    KnurledGrip,
    Gold,

    /// <summary>A silvered surface: everything it takes it gives back.</summary>
    Mirror,
    Copper,

    Plastic,
    Rubber,
    Painted,

    Glass,
    FrostedGlass,
    GlassBlockWave,
    GlassBlockCross,
    GlassBlockBubble,
    Water,

    Lamp
}
