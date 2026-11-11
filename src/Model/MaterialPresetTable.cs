using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// The one table of ready-made materials. It lives in the model because applying
/// a preset is a model operation, and because the inspector and the command
/// surface must not each keep their own idea of what Brick is.
/// </summary>
/// <remarks>
/// No display words here. A preset's label is how it is written to a person and
/// belongs to the presenter; what the model owns is the numbers.
/// </remarks>
public static class MaterialPresetTable
{
    /// <summary>Every preset, in the order a list should show them.</summary>
    public static IReadOnlyList<MaterialPreset> All { get; } =
        (MaterialPreset[])Enum.GetValues(typeof(MaterialPreset));

    /// <remarks>
    /// <b>No two presets may share an appearance.</b> Which preset a material is
    /// gets asked of its values alone, so two entries with the same numbers are
    /// one entry the list can never show as chosen — and the three metals nearly
    /// were, differing only in colour until their polish was made to differ too,
    /// which is a fact about gold and copper rather than a fudge to satisfy a
    /// lookup. <c>NoTwoPresetsShareAnAppearance</c> holds it.
    /// <para>
    /// Plaster is deliberately the default appearance, so a material nobody has
    /// touched reads as Plaster rather than as Custom: white and matte is what
    /// plaster is, and Custom is a word for a decision somebody made.
    /// </para>
    /// </remarks>
    public static MaterialPresetEntry Get(MaterialPreset preset) => preset switch
    {
        // --- mineral ---
        MaterialPreset.Stone => Mineral(0.95f, MaterialFinish.Rough, 2f, 1f, 0.55f, 0.54f, 0.52f),
        MaterialPreset.Concrete => Mineral(1f, MaterialFinish.Rough, 3f, 0.7f, 0.66f, 0.66f, 0.64f),
        MaterialPreset.Sand => Mineral(1f, MaterialFinish.Sand, 1f, 1f, 0.82f, 0.74f, 0.54f),
        MaterialPreset.Plaster => Mineral(1f, MaterialFinish.Smooth, 2f, 1f, 0.88f, 0.87f, 0.84f),
        MaterialPreset.Brick => Mineral(0.95f, MaterialFinish.Brick, 2f, 1f, 0.66f, 0.30f, 0.24f),
        MaterialPreset.Tile => Mineral(0.35f, MaterialFinish.Tiles, 8f, 1f, 0.86f, 0.85f, 0.82f),
        MaterialPreset.Mosaic => Mineral(0.30f, MaterialFinish.Mosaic, 0.7f, 1f, 0.42f, 0.66f, 0.74f),

        // --- timber ---
        MaterialPreset.Timber => Timber(MaterialFinish.Wood, 0.7f, 1f, 0.62f, 0.46f, 0.33f),
        MaterialPreset.PlankFloor => Timber(MaterialFinish.Laminate, 4f, 1f, 0.72f, 0.52f, 0.32f),
        MaterialPreset.Parquet => Timber(MaterialFinish.Parquet, 2f, 1f, 0.70f, 0.48f, 0.28f),
        MaterialPreset.Bark => Timber(MaterialFinish.Bark, 3f, 1.2f, 0.46f, 0.35f, 0.26f),

        // --- metal ---
        MaterialPreset.Steel => Metal(0.30f, MaterialFinish.Smooth, 1f, 1f, 0.76f, 0.77f, 0.80f),
        MaterialPreset.BrushedSteel => Metal(0.34f, MaterialFinish.Brushed, 1f, 1f, 0.76f, 0.77f, 0.80f),
        MaterialPreset.HammeredMetal => Metal(0.38f, MaterialFinish.Hammered, 0.4f, 1f, 0.74f, 0.75f, 0.78f),
        MaterialPreset.TreadPlate => Metal(0.32f, MaterialFinish.Tread, 0.8f, 1f, 0.74f, 0.75f, 0.78f),
        MaterialPreset.Corrugated => Metal(0.34f, MaterialFinish.Ribbed, 2f, 1f, 0.74f, 0.75f, 0.78f),
        MaterialPreset.KnurledGrip => Metal(0.40f, MaterialFinish.Knurled, 0.25f, 1f, 0.62f, 0.63f, 0.66f),
        MaterialPreset.Gold => Metal(0.20f, MaterialFinish.Smooth, 1f, 1f, 0.92f, 0.74f, 0.30f),

        // The one metal that goes all the way to one. Everywhere else stopping
        // short of it is the point — a fully metallic surface has no diffuse term,
        // so a face turned from every light goes black — and here that is exactly
        // what a mirror does, which is why it is the exception rather than an
        // oversight. Nearly white and nearly polished: silver takes about a
        // twentieth of what lands on it and gives the rest back.
        MaterialPreset.Mirror => new MaterialPresetEntry(
            new MaterialAppearance(0.02f, 1f, 0f, MaterialFinish.Smooth, 1f, 1f),
            new Color(0.95f, 0.96f, 0.97f),
            MaterialPresetGroup.Metal),
        MaterialPreset.Copper => Metal(0.38f, MaterialFinish.Smooth, 1f, 1f, 0.83f, 0.48f, 0.32f),

        // --- synthetic ---
        MaterialPreset.Plastic => Synthetic(0.38f, MaterialFinish.Smooth, 1f, 1f, 0.80f, 0.30f, 0.26f),
        MaterialPreset.Rubber => Synthetic(0.85f, MaterialFinish.Knurled, 0.5f, 0.7f, 0.16f, 0.16f, 0.17f),
        MaterialPreset.Painted => Synthetic(0.52f, MaterialFinish.Smooth, 1f, 1f, 0.30f, 0.52f, 0.70f),

        // --- glass and light ---
        MaterialPreset.Glass => Glassy(0.05f, MaterialFinish.Smooth, 1f, 1f, 0.30f),
        MaterialPreset.FrostedGlass => Glassy(0.55f, MaterialFinish.Smooth, 1f, 1f, 0.55f),
        MaterialPreset.GlassBlockWave => Glassy(0.08f, MaterialFinish.GlassWave, 5f, 1f, 0.40f),
        MaterialPreset.GlassBlockCross => Glassy(0.08f, MaterialFinish.GlassCross, 5f, 1f, 0.40f),
        MaterialPreset.GlassBlockBubble => Glassy(0.08f, MaterialFinish.GlassBubble, 5f, 1f, 0.40f),

        // Water is a smooth dielectric that lets light through, tinted by what it
        // is deep enough to absorb, with the swell on its surface as a facture. The
        // reflection is not a property here at all: the tracer reflects a dielectric
        // by Fresnel already, so what makes water read as water is being smooth
        // enough for the reflection to survive and rippled enough for it to move.
        // Eight cells to a wavelength — a swell a person could step across.
        MaterialPreset.Water => new MaterialPresetEntry(
            new MaterialAppearance(0.03f, 0f, 0f, MaterialFinish.Ripples, 8f, 1f, Transparent: true),
            new Color(0.16f, 0.46f, 0.52f, 0.42f),
            MaterialPresetGroup.Glass),

        MaterialPreset.Lamp => new MaterialPresetEntry(
            new MaterialAppearance(1f, 0f, 0.75f, MaterialFinish.Smooth, 2f, 1f),
            new Color(1f, 0.86f, 0.55f),
            MaterialPresetGroup.Glass),

        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
    };

    /// <summary>
    /// Which preset a material's values are, or null for none. Compared on the
    /// appearance alone — a recoloured brick is still Brick, and a preset list
    /// that fell to Custom the moment somebody picked a shade would be no use.
    /// </summary>
    public static MaterialPreset? Match(MaterialAppearance appearance)
    {
        foreach (MaterialPreset preset in All)
        {
            if (Matches(Get(preset).Appearance, appearance))
                return preset;
        }

        return null;
    }

    /// <summary>
    /// Compared with a tolerance, because these values arrive from sliders and a
    /// slider lands where the pointer did. A thousandth is finer than any control
    /// here can express and coarser than float drift.
    /// </summary>
    private static bool Matches(MaterialAppearance left, MaterialAppearance right) =>
        left.Finish == right.Finish

        // Every field, transparency included. Left out, a material stayed
        // "chosen" as Water or Glass after its transparency was switched off —
        // the panel naming a preset whose defining property the material no
        // longer has. The whole struct is compared for the same reason the clamp
        // is tested by reflection: a field added tomorrow is covered the day it
        // is added.
        && left.Transparent == right.Transparent
        && Near(left.Roughness, right.Roughness)
        && Near(left.Metallic, right.Metallic)
        && Near(left.Emission, right.Emission)
        && Near(left.FinishScale, right.FinishScale)
        && Near(left.FinishRelief, right.FinishRelief);

    private static bool Near(float left, float right) => Math.Abs(left - right) < 0.001f;

    private static MaterialPresetEntry Mineral(
        float roughness,
        MaterialFinish finish,
        float scale,
        float relief,
        float red,
        float green,
        float blue) =>
        new(
            new MaterialAppearance(roughness, 0f, 0f, finish, scale, relief),
            new Color(red, green, blue),
            MaterialPresetGroup.Mineral);

    private static MaterialPresetEntry Timber(
        MaterialFinish finish,
        float scale,
        float relief,
        float red,
        float green,
        float blue) =>
        new(
            new MaterialAppearance(0.72f, 0f, 0f, finish, scale, relief),
            new Color(red, green, blue),
            MaterialPresetGroup.Timber);

    /// <summary>
    /// Metals stop at 0.85 rather than 1: a fully metallic surface has no diffuse
    /// term, so a face turned away from both the key light and the sky goes black
    /// and the block stops reading as its own colour.
    /// </summary>
    private static MaterialPresetEntry Metal(
        float roughness,
        MaterialFinish finish,
        float scale,
        float relief,
        float red,
        float green,
        float blue) =>
        new(
            new MaterialAppearance(roughness, 0.85f, 0f, finish, scale, relief),
            new Color(red, green, blue),
            MaterialPresetGroup.Metal);

    private static MaterialPresetEntry Synthetic(
        float roughness,
        MaterialFinish finish,
        float scale,
        float relief,
        float red,
        float green,
        float blue) =>
        new(
            new MaterialAppearance(roughness, 0f, 0f, finish, scale, relief),
            new Color(red, green, blue),
            MaterialPresetGroup.Synthetic);

    /// <summary>Glass carries its transparency in the colour, which is the only opacity there is.</summary>
    private static MaterialPresetEntry Glassy(
        float roughness,
        MaterialFinish finish,
        float scale,
        float relief,
        float alpha) =>
        new(
            // Declared, not deduced from the alpha beside it: a glass block that
            // did not say so would be a solid pane of the same colour.
            new MaterialAppearance(roughness, 0f, 0f, finish, scale, relief, Transparent: true),
            new Color(0.62f, 0.80f, 0.86f, alpha),
            MaterialPresetGroup.Glass);
}
