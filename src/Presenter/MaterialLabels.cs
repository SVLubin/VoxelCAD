using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Presenter;

/// <summary>
/// What a preset and a facture are called. Composed here rather than stored with
/// the values, for the reason history labels are composed here rather than stored
/// with <c>ActionKind</c>: the model owns what a thing is and the presenter owns
/// what it is called, so wording changes reach no file.
/// </summary>
internal static class MaterialLabels
{
    public static string For(MaterialPreset preset) => preset switch
    {
        MaterialPreset.Stone => "Stone",
        MaterialPreset.Concrete => "Concrete",
        MaterialPreset.Sand => "Sand",
        MaterialPreset.Plaster => "Plaster",
        MaterialPreset.Brick => "Brick",
        MaterialPreset.Tile => "Tile",
        MaterialPreset.Mosaic => "Mosaic",

        MaterialPreset.Timber => "Timber",
        MaterialPreset.PlankFloor => "Plank floor",
        MaterialPreset.Parquet => "Parquet",
        MaterialPreset.Bark => "Bark",

        MaterialPreset.Steel => "Steel",
        MaterialPreset.BrushedSteel => "Brushed steel",
        MaterialPreset.HammeredMetal => "Hammered metal",
        MaterialPreset.TreadPlate => "Tread plate",
        MaterialPreset.Corrugated => "Corrugated sheet",
        MaterialPreset.KnurledGrip => "Knurled grip",
        MaterialPreset.Gold => "Gold",
        MaterialPreset.Copper => "Copper",

        MaterialPreset.Plastic => "Plastic",
        MaterialPreset.Rubber => "Rubber",
        MaterialPreset.Painted => "Painted",

        MaterialPreset.Glass => "Glass",
        MaterialPreset.FrostedGlass => "Frosted glass",
        MaterialPreset.Mirror => "Mirror",
        MaterialPreset.Water => "Water",
        MaterialPreset.GlassBlockWave => "Glass block, wave",
        MaterialPreset.GlassBlockCross => "Glass block, cross",
        MaterialPreset.GlassBlockBubble => "Glass block, bubble",
        MaterialPreset.Lamp => "Lamp",

        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
    };

    public static string For(MaterialFinish finish) => finish switch
    {
        MaterialFinish.Smooth => "None",
        MaterialFinish.Sand => "Sand",
        MaterialFinish.Rough => "Rough",
        MaterialFinish.Grain => "Grain",
        MaterialFinish.Standard => "Standard",
        MaterialFinish.Wood => "Wood",
        MaterialFinish.Bark => "Bark",
        MaterialFinish.Ripples => "Ripples",
        MaterialFinish.Laminate => "Boards",
        MaterialFinish.Brick => "Brickwork",
        MaterialFinish.Parquet => "Herringbone",
        MaterialFinish.Brushed => "Brushed",
        MaterialFinish.Hammered => "Hammered",
        MaterialFinish.Tread => "Tread plate",
        MaterialFinish.Ribbed => "Corrugated",
        MaterialFinish.Tiles => "Tiles",
        MaterialFinish.Mosaic => "Mosaic",
        MaterialFinish.Knurled => "Knurled",
        MaterialFinish.GlassWave => "Glass block, wave",
        MaterialFinish.GlassCross => "Glass block, cross",
        MaterialFinish.GlassBubble => "Glass block, bubble",

        _ => throw new ArgumentOutOfRangeException(nameof(finish), finish, null)
    };
}
