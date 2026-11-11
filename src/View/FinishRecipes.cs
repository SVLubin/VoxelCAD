using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The one table mapping a <see cref="MaterialFinish"/> to the height field it is
/// drawn from. The document carries the choice; how that choice becomes a picture
/// is the view's business and lives here, so adding a finish is a member and a row
/// and nothing outside this assembly learns how it is made.
/// </summary>
internal static class FinishRecipes
{
    /// <summary>
    /// The recipe behind a facture. Anisotropy comes from the tile's own
    /// proportions rather than from a per-axis frequency Godot's noise does not
    /// offer: a 512×16 tile stretched over a square face is a streak.
    /// </summary>
    public static FinishRecipe GetRecipe(MaterialFinish finish) => finish switch
    {
        MaterialFinish.Smooth => FinishRecipe.None,

        // Nothing drawn: a shipped material's maps come out of the pack, and this
        // is only reached when the pack has no entry under that name.
        MaterialFinish.Standard => FinishRecipe.None,

        // Grains, not drifts: a high frequency with only a couple of octaves, so
        // the surface is evenly granular instead of gathering into the lumps that
        // make Rough read as stone.
        MaterialFinish.Sand => new FinishRecipe(
            Size: 256,
            PeriodU: 20,
            PeriodV: 20,
            Octaves: 2,
            Pattern: FinishPattern.Noise,
            Repeats: 1f,
            Feature: 0f,
            UnitsPerTile: 1f,
            BumpStrength: 6f,
            NormalScale: 0.85f,
            AlbedoFloor: 0.9f),

        MaterialFinish.Rough => new FinishRecipe(
            Size: 256,
            PeriodU: 8,
            PeriodV: 8,
            Octaves: 4,
            Pattern: FinishPattern.Noise,
            Repeats: 1f,
            Feature: 0f,
            UnitsPerTile: 1f,
            BumpStrength: 4f,
            NormalScale: 0.9f,
            AlbedoFloor: 0.9f),

        // Two numbers make a streak, and only one of them is obvious. The tile's
        // proportions set how long a feature ends up — 256 by 26 is a ten-to-one
        // stretch once it is laid over a square face. The frequency has to keep up
        // with the short side: a feature wider than 26 pixels wraps into a blotch
        // instead of a fibre, which is what a coarser grain looked like when it was
        // tried.
        MaterialFinish.Grain => new FinishRecipe(
            Size: 256,
            PeriodU: 36,
            PeriodV: 7,
            Octaves: 2,
            Pattern: FinishPattern.Streaks,
            Repeats: 1f,
            Feature: 0f,
            UnitsPerTile: 1f,
            BumpStrength: 5f,
            NormalScale: 0.9f,
            AlbedoFloor: 0.88f),

        // Eight to one, not forty. An extreme stretch reads as brushed steel on a
        // block and as a curtain of drips on a wall, because the streak's length
        // grows with the period and there is nothing to break it up. This is also
        // why a directional finish wants a short period: coarsen it and the marks
        // stop being marks.
        MaterialFinish.Brushed => new FinishRecipe(
            Size: 256,
            PeriodU: 64,
            PeriodV: 9,
            Octaves: 2,
            Pattern: FinishPattern.Streaks,
            Repeats: 1f,
            Feature: 0f,
            UnitsPerTile: 1f,
            BumpStrength: 3f,
            NormalScale: 0.6f,
            AlbedoFloor: 0.93f),

        MaterialFinish.Hammered => new FinishRecipe(
            Size: 256,
            PeriodU: 6,
            PeriodV: 6,
            Octaves: 1,
            Pattern: FinishPattern.Cellular,
            Repeats: 1f,
            Feature: 0f,
            UnitsPerTile: 6f,
            BumpStrength: 7f,
            NormalScale: 1.0f,
            AlbedoFloor: 0.93f),

        // Rings are mostly a colour, barely a groove — so the albedo carries this
        // one and the normal only hints at it. A tile spanning four cells keeps the
        // arches long enough to read as timber rather than as stripes.
        MaterialFinish.Wood => new FinishRecipe(
            Size: 256,
            PeriodU: 0,
            PeriodV: 0,
            Octaves: 0,
            Pattern: FinishPattern.Wood,
            Repeats: 1f,
            Feature: 0f,
            // Four growth rings to a tile, so the dial is cells to a ring.
            UnitsPerTile: 4f,
            // Barely any. A sawn board is flat — what a person sees in timber is
            // the colour of the late wood, not a groove at every ring, and driving
            // this hard is what made a post read as corrugated card.
            BumpStrength: 1.2f,
            NormalScale: 0.7f,
            // And correspondingly deep. Late wood really is about half the
            // brightness of early wood; at 0.74 the bands were a fifth of that and
            // the figure had to come from the relief, which is exactly backwards.
            AlbedoFloor: 0.56f),

        // Bark is the deepest surface here on purpose: the furrows are the shape,
        // not a texture over it, so the bump runs hard and the albedo follows.
        MaterialFinish.Bark => new FinishRecipe(
            Size: 256,
            PeriodU: 0,
            PeriodV: 0,
            Octaves: 0,
            Pattern: FinishPattern.Bark,
            Repeats: 1f,
            Feature: 0f,
            UnitsPerTile: 1f,
            BumpStrength: 22f,
            NormalScale: 1.1f,
            AlbedoFloor: 0.66f),

        MaterialFinish.Laminate => new FinishRecipe(
            Size: 256,
            PeriodU: 0,
            PeriodV: 0,
            Octaves: 0,
            Pattern: FinishPattern.Laminate,
            Repeats: 1f,
            // A slot between boards, not a chamfer on each of them. It was two and
            // a half times this, on the reasoning that a groove measured in board
            // widths is a thin line on the surface — true of its width and not of
            // what it does to the light: a ramp that wide is a bevel, and every
            // board came out with a lit edge and a shaded one, so a floor read as
            // a stack of mouldings. A joint is a line, and a line is drawn.
            Feature: 0.03f,
            // Eight cells to the tile, so a board is two cells: on the lattice,
            // where a voxel editor's numbers belong, and simply bigger than a voxel.
            // At four the tile held four boards over four cells — a board to a cell
            // exactly — and every edge and corner of every model had a joint driven
            // along it, three times the flat material's contrast across a concave
            // corner. What was wrong was never that the period was a whole number
            // of cells; it was that the board *was* the cell. See the ledger.
            UnitsPerTile: 4f,
            // Halved with the groove, and then some. Narrowing the ramp steepens it
            // in the same proportion, so holding this would have traded a wide
            // bevel for a hard bright wire; what is wanted is a joint that reads as
            // a shadow, so most of it is carried by the albedo below.
            BumpStrength: 6f,
            NormalScale: 0.9f,
            // Deep enough for the per-board tone above to be visible: at 0.66 the
            // sixteen per cent between the lightest board and the darkest came out
            // as five, which is nothing.
            AlbedoFloor: 0.42f),

        MaterialFinish.Brick => new FinishRecipe(
            Size: 256,
            PeriodU: 0,
            PeriodV: 0,
            Octaves: 0,
            Pattern: FinishPattern.Brick,
            Repeats: 1f,
            Feature: 0.055f,
            UnitsPerTile: 4f,
            BumpStrength: 20f,
            NormalScale: 1f,
            AlbedoFloor: 0.78f),

        MaterialFinish.Parquet => new FinishRecipe(
            Size: 256,
            PeriodU: 0,
            PeriodV: 0,
            Octaves: 0,
            Pattern: FinishPattern.Parquet,
            Repeats: 1f,
            Feature: 0.13f,
            UnitsPerTile: 4f,
            BumpStrength: 16f,
            NormalScale: 0.9f,
            AlbedoFloor: 0.8f),

        // --- manufactured surfaces: drawn, not sampled ---
        //
        // A regular pattern wants a whole number of cells to a tile, or its rows
        // drift across the voxel grid and the eye reads the drift rather than the
        // pattern. One is the honest default here: one plate per cell.
        MaterialFinish.Tread => new FinishRecipe(
            Size: 256,
            PeriodU: 0,
            PeriodV: 0,
            Octaves: 0,
            Pattern: FinishPattern.Tread,
            Repeats: 1f,
            Feature: 0f,
            UnitsPerTile: 2f,
            BumpStrength: 26f,
            NormalScale: 1f,
            AlbedoFloor: 0.9f),

        MaterialFinish.Ribbed => new FinishRecipe(
            Size: 256,
            PeriodU: 0,
            PeriodV: 0,
            Octaves: 0,
            Pattern: FinishPattern.Ribbed,
            Repeats: 4f,
            Feature: 0f,
            UnitsPerTile: 4f,
            // A quarter of what it was. This is a metal, and a metal has no diffuse
            // term to fill a trough the sky cannot reach — deep corrugation came
            // out as black and white stripes.
            BumpStrength: 5f,
            NormalScale: 1f,
            AlbedoFloor: 0.92f),

        MaterialFinish.Tiles => new FinishRecipe(
            Size: 256,
            PeriodU: 0,
            PeriodV: 0,
            Octaves: 0,
            Pattern: FinishPattern.Tiles,
            Repeats: 1f,
            Feature: 0.05f,
            UnitsPerTile: 1f,
            BumpStrength: 30f,
            NormalScale: 1f,
            AlbedoFloor: 0.82f),

        // The same generator as Tiles, and a different surface: many small squares
        // with proportionally far wider grout is what makes a pool wall rather than
        // a shrunken panel.
        MaterialFinish.Mosaic => new FinishRecipe(
            Size: 256,
            PeriodU: 0,
            PeriodV: 0,
            Octaves: 0,
            Pattern: FinishPattern.Tiles,
            Repeats: 4f,
            Feature: 0.12f,
            UnitsPerTile: 4f,
            BumpStrength: 18f,
            NormalScale: 1f,
            AlbedoFloor: 0.84f),

        MaterialFinish.Knurled => new FinishRecipe(
            Size: 256,
            PeriodU: 0,
            PeriodV: 0,
            Octaves: 0,
            // Five, not eight. At eight the diamonds are finer than the screen can
            // hold at any honest distance and the surface shimmers with moiré,
            // which is a knurl nobody could grip and a texture nobody can read.
            Pattern: FinishPattern.Knurled,
            Repeats: 5f,
            Feature: 0f,
            UnitsPerTile: 5f,
            BumpStrength: 10f,
            NormalScale: 1f,
            AlbedoFloor: 0.93f),

        // Glass blocks: one block to a cell, and a deep mould. The rim is wide
        // because on a block it is a real chamfer rather than a grout line, and it
        // is what makes a wall of these read as blocks at any distance.
        MaterialFinish.Ripples => new FinishRecipe(
            Size: 256,
            PeriodU: 0,
            PeriodV: 0,
            Octaves: 0,
            Pattern: FinishPattern.Ripples,
            Repeats: 1f,
            Feature: 0f,
            // One unit is one wavelength of the long swell, which is the wave a
            // person would point at and call the ripple.
            UnitsPerTile: 1f,
            // Shallow, and this is the number the whole material turns on: water is
            // what it does to a reflection, and a reflection is bent by a gentle
            // slope and shattered by a steep one. Every other drawn finish here is
            // between six and thirty.
            BumpStrength: 2.5f,
            NormalScale: 1f,
            // Almost none: a wave does not change the colour of the water, it
            // changes where the light comes from. What little there is reads as the
            // sky being brighter in the troughs.
            AlbedoFloor: 0.94f),

        MaterialFinish.GlassWave => GlassBlockRecipe(FinishPattern.GlassWave),
        MaterialFinish.GlassCross => GlassBlockRecipe(FinishPattern.GlassCross),
        MaterialFinish.GlassBubble => GlassBlockRecipe(FinishPattern.GlassBubble),

        _ => throw new ArgumentOutOfRangeException(nameof(finish), finish, null)
    };

    private static FinishRecipe GlassBlockRecipe(FinishPattern pattern) => new(
        Size: 256,
        PeriodU: 0,
        PeriodV: 0,
        Octaves: 0,
        Pattern: pattern,
        Repeats: 1f,
        Feature: 0.11f,
        UnitsPerTile: 1f,
        BumpStrength: 24f,
        NormalScale: 1f,
        AlbedoFloor: 0.88f);
}
