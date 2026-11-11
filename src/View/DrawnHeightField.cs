using System;

namespace VoxelCAD.View;

/// <summary>
/// Draws a manufactured surface as a tiling height field, in the range zero to
/// one. Every pattern here is built from periodic functions of the tile
/// coordinate, so it wraps exactly by construction — no blend skirt, and no seam
/// to hide.
/// </summary>
internal static class DrawnHeightField
{
    /// <summary>Row-major, <c>recipe.Size</c> square.</summary>
    public static float[] Create(FinishRecipe recipe)
    {
        var heights = new float[recipe.Size * recipe.Size];

        for (var y = 0; y < recipe.Size; y++)
        {
            for (var x = 0; x < recipe.Size; x++)
            {
                float u = (float)x / recipe.Size * recipe.Repeats;
                float v = (float)y / recipe.Size * recipe.Repeats;

                heights[y * recipe.Size + x] = Sample(recipe, u, v);
            }
        }

        return heights;
    }

    /// <summary>
    /// The height at one point of the tile, in the tile's own units — the whole
    /// tile spans <c>recipe.Repeats</c> of them.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Create"/> so a test can ask the thing that
    /// actually matters: whether <c>f(u, v)</c> equals <c>f(u + Repeats, v)</c>.
    /// A pattern that fails that has a seam, and a seam is exactly what nobody
    /// notices until the surface is large.
    /// </remarks>
    public static float Sample(FinishRecipe recipe, float u, float v) =>
        Sample(recipe, u, v, 1f);

    /// <summary>
    /// The same, with the pattern's fine detail scaled — one for the surface as it
    /// ships, zero for its bare structure.
    /// </summary>
    /// <remarks>
    /// Exists so a test can ask a question that cannot be asked of the finished
    /// height alone: <b>does the detail ride on the structure's edges?</b> A grain
    /// that keeps oscillating as it crosses a chamfer turns that chamfer into a
    /// chain of beads, because on a steep slope a small wobble in height is a large
    /// wobble in the normal. Comparing the two answers finds it mechanically.
    /// </remarks>
    public static float Sample(FinishRecipe recipe, float u, float v, float detail) =>
        recipe.Pattern switch
    {
        FinishPattern.Noise => Fbm(u, v, recipe.PeriodU, recipe.PeriodV, recipe.Octaves),
        FinishPattern.Cellular => Cellular(u, v, recipe.PeriodU, recipe.PeriodV),
        FinishPattern.Streaks => Streaks(u, v, recipe.PeriodU, recipe.PeriodV, recipe.Octaves),
        FinishPattern.Tread => Tread(u, v),
        FinishPattern.Ribbed => Ribbed(u),
        FinishPattern.Tiles => Tiles(u, v, recipe.Feature),
        FinishPattern.Bark => Bark(u, v, detail),
        FinishPattern.Laminate => Laminate(u, v, recipe.Feature, detail),
        FinishPattern.Brick => Brick(u, v, recipe.Feature, detail),
        FinishPattern.Parquet => Parquet(u, v, recipe.Feature, detail),
        FinishPattern.Knurled => Knurled(u, v),
        FinishPattern.Wood => Wood(u, v, detail),
        FinishPattern.GlassWave => GlassBlock(u, v, recipe.Feature, 0),
        FinishPattern.GlassCross => GlassBlock(u, v, recipe.Feature, 1),
        FinishPattern.GlassBubble => GlassBlock(u, v, recipe.Feature, 2),
        FinishPattern.Ripples => Ripples(u, v),
        _ => throw new ArgumentOutOfRangeException(
            nameof(recipe), recipe.Pattern, "Unknown pattern.")
    };

    /// <summary>
    /// Checker plate. The tile divides into quadrants; each holds a pair of
    /// parallel lentils, and the angle alternates by quadrant — which is what
    /// makes the real thing grip in both directions and what makes it read as
    /// itself rather than as generic diagonal hatching.
    /// </summary>
    private static float Tread(float u, float v)
    {
        float cellU = Fract(u) * 2f;
        float cellV = Fract(v) * 2f;

        var quadrantU = (int)cellU;
        var quadrantV = (int)cellV;

        // Local coordinates inside the quadrant, centred.
        float localU = cellU - quadrantU - 0.5f;
        float localV = cellV - quadrantV - 0.5f;

        // The diagonal the pair runs along flips with the quadrant.
        float diagonal = (quadrantU + quadrantV) % 2 == 0 ? 1f : -1f;

        float along = (localU + diagonal * localV) * 0.70710678f;
        float across = (localU - diagonal * localV) * 0.70710678f;

        // Two lentils, offset either side of the quadrant's middle.
        return Math.Max(
            Lentil(along, across - 0.13f),
            Lentil(along, across + 0.13f));
    }

    /// <summary>
    /// One raised lens: a dome inside an ellipse, flat ground outside it.
    /// </summary>
    /// <remarks>
    /// The profile is <c>(1 − r²)²</c> and not the hemisphere's <c>√(1 − r²)</c>,
    /// which was the first thing tried and was wrong for a reason worth keeping:
    /// a hemisphere is <b>vertical</b> where it meets the plane, so the normal map
    /// turns over inside a single texel and every lentil wears a ragged dark rim.
    /// This profile lands tangent to the ground, which is also what a rolled
    /// lentil actually does.
    /// </remarks>
    private static float Lentil(float along, float across)
    {
        const float halfLength = 0.34f;
        const float halfWidth = 0.10f;

        float a = along / halfLength;
        float b = across / halfWidth;
        float radius = a * a + b * b;

        if (radius >= 1f)
            return 0f;

        float dome = 1f - radius;

        return dome * dome;
    }

    /// <summary>Corrugation: rounded ridges with flat valleys between them.</summary>
    private static float Ribbed(float u)
    {
        // A plain cosine, and the squaring that used to sharpen it is gone. The
        // profile was not the problem the sharpening solved: a rolled sheet is
        // metal, so wherever its normal turns away from the sky there is no
        // diffuse term to fall back on and the trough goes black. Crests white and
        // troughs black is a barcode, not a sheet; what a sheet actually shows is
        // the environment smeared along the corrugation, grey over grey.
        return 0.5f + 0.5f * MathF.Cos(2f * MathF.PI * Fract(u));
    }

    /// <summary>
    /// Panels: flat faces parted by a chamfered groove. The groove is measured
    /// from whichever edge is nearer, so corners meet without a notch.
    /// </summary>
    private static float Tiles(float u, float v, float groove) =>
        Math.Min(Chamfer(EdgeDistance(u), groove), Chamfer(EdgeDistance(v), groove));

    /// <summary>
    /// Bark. Furrows run up the trunk and are far deeper than any other surface
    /// here, and — this is what separates bark from a fluted column — they are
    /// broken along their length into plates, so no furrow runs the whole height.
    /// </summary>
    /// <remarks>
    /// The only pattern here built on noise rather than on sines, and it had to
    /// be: a sum of sines gave furrows that wandered in step with each other and
    /// broke on a schedule, so the surface read as plaited rope. Bark's whole
    /// character is that no two furrows agree. The noise is the periodic kind
    /// below, so this still tiles exactly — the tiling test covers it like the
    /// rest.
    /// </remarks>
    private static float Bark(float u, float v, float detail)
    {
        // Sideways wander, slow and irregular: this is what stops the furrows
        // running parallel.
        // Wide enough to be worth more than a whole furrow's spacing, which is
        // what lets two furrows run together and one split in two. A gentler
        // wander only makes parallel wavy lines.
        float wander = 2.0f * (Fbm(u, v, 1, 3, 3) - 0.5f);

        float furrow = Fract(u * 5f + wander) - 0.5f;
        float depth = MathF.Exp(-(furrow * furrow) / (2f * 0.09f * 0.09f));

        // Plates: the furrow fades along its length, so the ridges join and part
        // at no fixed interval.
        float breaks = Math.Clamp(0.1f + 1.6f * Fbm(u, v, 2, 5, 3), 0f, 1f);

        // Relief on the plates: fine and hard, so a plate is rough rather than
        // billowed. Billowing is what the sines gave, and it looked like fabric.
        float relief = detail * 0.24f * (Fbm(u, v, 9, 13, 2) - 0.5f);

        return Math.Clamp(1f - 0.95f * depth * breaks + relief, 0f, 1f);
    }

    /// <summary>
    /// A laid floor of long boards, staggered so the end joints never line up,
    /// with the grain running the length of each board.
    /// </summary>
    private static float Laminate(float u, float v, float groove, float detail) =>
        Courses(u, v, groove, dressing: 0.3f, detail, across: 4, along: 1, timber: true);

    /// <summary>
    /// Brickwork: the same staggered courses at a brick's proportions, and a face
    /// that is roughened rather than grained — a brick has no fibre.
    /// </summary>
    private static float Brick(float u, float v, float groove, float detail) =>
        Courses(u, v, groove, dressing: 0.22f, detail, across: 4, along: 2, timber: false);

    /// <summary>
    /// Staggered courses: <paramref name="across"/> of them, each holding
    /// <paramref name="along"/> units, every course shifted a quarter of a unit so
    /// the end joints never line up. What separates a floor from a wall here is
    /// only the proportion and what dresses the face — four to one with fibre is a
    /// board, two to one with a rough face is a brick.
    /// </summary>
    /// <remarks>
    /// Everything that indexes a unit — the course, the stagger, the face's phase —
    /// is reduced to its place <b>within the tile</b>. Reading the raw course
    /// number instead is what put a seam right across this surface: the number
    /// keeps climbing past the tile edge, so the unit on one side of it was a
    /// different unit from the one on the other.
    /// <para>
    /// <paramref name="groove"/> and <paramref name="dressing"/> are two decisions
    /// and not one, which is what narrowing the joint taught. The dressing's phase
    /// steps from unit to unit — that is what makes each board its own board — so
    /// it has to be gone before the joint or the step is a cliff in the surface.
    /// While the two shared a number, thinning the joint pulled the mask in with
    /// it and let that step through: measured, half of a 0.033 rise still standing
    /// at a thirty-second of the interval, which is <c>NoPatternHasACliff</c>
    /// saying exactly what it was written to say.
    /// </para>
    /// </remarks>
    /// <param name="groove">Half-width of the joint, in unit widths.</param>
    /// <param name="dressing">
    /// How far the face's own texture is held off that joint. Absolute rather than
    /// a multiple of the joint, so a thinner joint does not drag it in.
    /// </param>
    private static float Courses(
        float u,
        float v,
        float groove,
        float dressing,
        float detail,
        int across,
        int along,
        bool timber)
    {
        int course = Index(v * across, across);

        // A quarter-unit stagger per course, returning to zero after four.
        float shift = course * 0.25f * along;

        float alongUnit = u * along + shift;
        float acrossUnit = v * across;

        float seam = Math.Min(
            Chamfer(EdgeDistance(alongUnit), groove),
            Chamfer(EdgeDistance(acrossUnit), groove * 1.4f));

        // Where the face is genuinely flat, measured from the joint rather than
        // read off the seam's own height. Reading the height is not enough: a
        // chamfer is at its steepest half way up, and half way up the seam is
        // already 0.5.
        float flat = Math.Min(
            Chamfer(EdgeDistance(alongUnit), dressing),
            Chamfer(EdgeDistance(acrossUnit), dressing * 1.5f));

        int column = Index(alongUnit, Math.Max(1, along));
        float phase = Fract(course * 0.61f + column * 0.29f);

        float face = timber

            // Fibre along the board, its phase set by which board this is. Counted
            // per tile, so it has to be re-said whenever the tile changes size: the
            // grain is a property of the timber and not of how wide the boards were
            // cut, and when the tile went from four cells to eight the same numbers
            // made it twice as coarse — on a wall it stopped reading as wood and
            // started reading as corduroy.
            //
            // Three rates rather than two, the third slow enough to swell and fade
            // the other two along the board. Two clean sines at a fixed ratio are a
            // ruled pattern however fine they are: what a board actually shows is
            // grain that gathers in places and opens out in others.
            // Modulated by u rather than by the board's own coordinate: the
            // stagger jumps that coordinate by a quarter at every course boundary,
            // so an amplitude read from it steps there — and an amplitude that
            // steps is a step in the surface, which NoPatternHasACliff duly found.
            // Halved against the first cut of this. On a floor seen from standing
            // height the grain is not what carries the wood — the tone from board
            // to board is — and grain loud enough to see from there reads as
            // ribbing. What is left is a texture you find by looking for it.
            ? (0.040f + 0.022f * MathF.Sin(2f * MathF.PI * u))
                * MathF.Sin(2f * MathF.PI * (acrossUnit * 11f + phase))

              // And one slow term across the board, so a board has a lighter and a
              // darker side rather than an even fill between its joints.
              + 0.05f * MathF.Sin(2f * MathF.PI * (acrossUnit + phase * 1.7f))
              // Twenty and not thirty-two. At thirty-two cycles to the tile edge
              // and four boards to a tile the finest term ran at two texels a
              // cycle — Nyquist exactly, so what it drew was not fibre but the
              // moiré of fibre, and NoPatternHasACliff reads an aliased sine as
              // the discontinuity it effectively is.
              + 0.022f * MathF.Sin(2f * MathF.PI * (acrossUnit * 20f + phase * 2f))
              + 0.020f * MathF.Sin(2f * MathF.PI * (acrossUnit * 5f + phase * 3f))

            // A brick face is grainless and slightly coarse.
            : 0.16f * (Fbm(u, v, 24, 24, 2) - 0.5f);

        // Every unit its own tone. This is the largest single thing that separates
        // a laid surface from a stamped one: no two boards come off the same log
        // and no two bricks out of the same part of the kiln, and a wall of one
        // colour reads as a repeated tile whatever is drawn on it. Carried on the
        // dressing's own mask, so it is gone by the time the joint is reached and
        // the step between neighbours never lands anywhere the surface is flat.
        float tone = 1f - 0.16f * Fract(course * 0.732f + column * 0.397f + 0.13f) * flat;

        // The dressing belongs on the flat and must be gone before the chamfer, or
        // it rocks the chamfer and the joint becomes a row of dark beads.
        return Math.Clamp(seam * tone + detail * face * flat, 0f, 1f);
    }

    /// <summary>
    /// Herringbone. Planks are twice as long as they are wide, and each cell of
    /// the underlying grid belongs to one of them; which one, and which way it
    /// lies, follows from <c>(i + j) mod 4</c> — the one rule of that shape under
    /// which every cell agrees with its neighbours about which plank it is in, and
    /// the rule that makes the courses step diagonally instead of stacking into
    /// brickwork.
    /// </summary>
    private static float Parquet(float u, float v, float groove, float detail)
    {
        const float cells = 4f;

        float gridU = u * cells;
        float gridV = v * cells;

        // Reduced into the tile, for the reason spelled out on Laminate: an index
        // that keeps climbing past the tile edge gives the pattern a different
        // answer on each side of it.
        int i = Index(gridU, (int)cells);
        int j = Index(gridV, (int)cells);
        int step = (i + j) % 4;

        float localU = Fract(gridU);
        float localV = Fract(gridV);

        // Where this cell sits inside its plank, in plank coordinates: two long,
        // one across.
        float along;
        float across;

        if (step < 2)
        {
            along = (localU + step) * 0.5f;
            across = localV;
        }
        else
        {
            along = (localV + (step - 2)) * 0.5f;
            across = localU;
        }

        float seam = Math.Min(
            Chamfer(Math.Min(along, 1f - along), groove * 0.5f),
            Chamfer(Math.Min(across, 1f - across), groove));

        // See Laminate: measured from the joint, not read off the seam.
        float flat = Math.Min(
            Chamfer(Math.Min(along, 1f - along), groove * 2.5f),
            Chamfer(Math.Min(across, 1f - across), groove * 4f));

        // Grain runs the length of the plank, with a phase from the plank's place —
        // the <b>plank's</b>, not the cell's. A plank covers two cells, so a phase
        // read off the cell gives its two halves different grain and leaves a
        // zipper down the middle of every board. Invisible in the height (the step
        // is about a fortieth) and loud in the normal, which is where a bump map
        // puts its emphasis.
        int originU = step < 2 ? Index(i - step, (int)cells) : i;
        int originV = step < 2 ? j : Index(j - (step - 2), (int)cells);

        float phase = Fract((originU * 0.31f + originV * 0.57f) * 3f);
        float grain = detail * 0.07f * MathF.Sin(2f * MathF.PI * (across * 9f + phase));

        // Held off the chamfer, for the reason given on Laminate.
        return Math.Clamp(seam + grain * flat, 0f, 1f);
    }

    /// <summary>Knurling: two crossed families of ridges, leaving a field of pyramids.</summary>
    private static float Knurled(float u, float v)
    {
        float first = 0.5f + 0.5f * MathF.Cos(2f * MathF.PI * (u + v));
        float second = 0.5f + 0.5f * MathF.Cos(2f * MathF.PI * (u - v));

        return first * second;
    }

    /// <summary>
    /// A sawn plank: growth rings crossing the face, warped so they arch the way
    /// they do on a board cut wide of the pith. True concentric rings are the
    /// honest model and they cannot tile, so the rings here are bands displaced by
    /// a sum of sines — every term periodic in both directions, which is what
    /// makes the plank repeat without a seam.
    /// </summary>
    private static float Wood(float u, float v, float detail)
    {
        // Terms along the grain give the bands their sway; terms across it displace
        // neighbouring rings by different amounts, which is what stops the whole
        // face repeating one wave and turns the sway into the cathedral figure.
        float warp =
            0.42f * MathF.Sin(2f * MathF.PI * (v + 0.13f))
            + 0.20f * MathF.Sin(2f * MathF.PI * (2f * v + 0.61f))
            + 0.11f * MathF.Sin(2f * MathF.PI * (3f * v + 0.29f))
            + 0.24f * MathF.Sin(2f * MathF.PI * u)
            + 0.09f * MathF.Sin(2f * MathF.PI * (2f * u + 0.5f));

        // Rings of unequal width, which is most of what tells timber from a
        // corrugated sheet: a tree's growth varies year to year, and evenly spaced
        // rings read as a machined ripple however well they are shaded. The term
        // stays periodic and its slope stays positive — at this amplitude the
        // widest ring is about three times the narrowest, and past it the rings
        // would fold back through each other.
        float position = u * Rings + 0.3f * MathF.Sin(2f * MathF.PI * u) + warp;

        // Late wood: a narrow dark band once per ring. Not every ring is as dark
        // as the next — a hard year leaves a heavier band — and it is that
        // unevenness the eye reads as wood rather than as stripes.
        //
        // Modulated smoothly rather than hashed per ring, and that is not a style
        // choice: a weight indexed by the ring steps at the ring boundary, and
        // although the band there is already dark to sixteen decimal places, the
        // fibre riding over it is not — NoPatternHasACliff caught the step at a
        // rise of 0.011. A term that varies slowly along the grain gives the same
        // unevenness with nothing to step at.
        float year = 0.5f + 0.5f * MathF.Sin(2f * MathF.PI * (position / Rings + 0.31f));
        float weight = 0.55f + 0.45f * year;

        // A heavy year lays down a wider band as well as a darker one, so the two
        // move together. Bands of one width read as ruled lines however they are
        // shaded — which is what the first cut of this came out as.
        float spread = 0.055f + 0.045f * year;
        float offset = Fract(position) - 0.5f;
        float late = weight * MathF.Exp(-(offset * offset) / (2f * spread * spread));

        // The early wood between the bands is not a flat fill either. The warp is
        // already a smooth field that follows the figure, so leaning on it gives
        // the blotchy variation a sawn face has for nothing — a fresh noise term
        // here would only fight the grain it is supposed to belong to.
        float earlywood = 0.05f * warp;

        // Striations between the bands, at two rates. One term gave a clean ripple
        // that read as ruling; wood is striped at every scale it has, and the
        // second term is what stops the first from looking drawn.
        float fibre = detail * (
            0.055f * MathF.Sin(2f * MathF.PI * (position * 3f + warp * 4f))
            + 0.030f * MathF.Sin(2f * MathF.PI * (position * 7f + warp * 2.2f)));

        // The band is a shade of the wood, not a line ruled on it. At 0.92 the late
        // wood went nearly black against a saturated ground and the whole face read
        // as wood-effect paper — the contrast a real board has between its late and
        // early wood is a third, not a whole.
        return Math.Clamp(1f - 0.68f * late + earlywood + fibre, 0f, 1f);
    }

    /// <summary>How many growth rings a wood tile holds across.</summary>
    private const int Rings = 4;

    /// <summary>
    /// A wall of glass blocks: a chamfered square rim, and inside it the moulded
    /// relief that decides which block this is. The rim and the relief multiply, so
    /// the pattern always stops at the mortar line the way a real block does.
    /// </summary>
    private static float GlassBlock(float u, float v, float rim, int variant)
    {
        float blockU = Fract(u);
        float blockV = Fract(v);

        float edge = Math.Min(
            Math.Min(blockU, 1f - blockU),
            Math.Min(blockV, 1f - blockV));

        float inner = variant switch
        {
            0 => 0.5f + 0.5f * MathF.Cos(2f * MathF.PI * blockU * 4f),
            1 => (0.5f + 0.5f * MathF.Cos(2f * MathF.PI * blockU * 4f))
                 * (0.5f + 0.5f * MathF.Cos(2f * MathF.PI * blockV * 4f)),
            _ => Dimples(blockU, blockV)
        };

        return Chamfer(edge, rim) * (0.55f + 0.45f * inner);
    }

    /// <summary>
    /// Still water: a few long waves crossing at angles that do not divide each
    /// other, so the swell never repeats visibly inside its own tile even though it
    /// tiles exactly.
    /// </summary>
    /// <remarks>
    /// Every term's frequency is a whole number of cycles per tile in both
    /// directions, which is what makes it periodic — the same discipline the noise
    /// patterns keep, reached with sines because water has no grain to it. The
    /// amplitudes fall off with the frequency the way a real sea's do, so the long
    /// swell carries the shape and the short chop only breaks up the highlight.
    /// <para>
    /// Deliberately shallow. Water's whole character in a render is what it does to
    /// a <em>reflection</em>, and a reflection is bent by a gentle slope and
    /// shattered by a steep one; a normal map that looked right on a wall would
    /// turn a pool into frosted glass.
    /// </para>
    /// </remarks>
    private static float Ripples(float u, float v)
    {
        float swell =
            0.50f * MathF.Sin(2f * MathF.PI * (u + 2f * v))
            + 0.31f * MathF.Sin(2f * MathF.PI * (3f * u - v + 0.37f))
            + 0.13f * MathF.Sin(2f * MathF.PI * (2f * u + 5f * v + 0.11f))
            + 0.06f * MathF.Sin(2f * MathF.PI * (5f * u + 3f * v + 0.61f));

        // The amplitudes sum to one, so this lands inside nought to one without a
        // clamp — and a clamp is what would put a flat spot in a wave.
        return 0.5f + 0.5f * swell;
    }

    /// <summary>A grid of round depressions pressed into the face of a block.</summary>
    private static float Dimples(float blockU, float blockV)
    {
        const float subdivisions = 3f;
        const float radius = 0.42f;

        float u = Fract(blockU * subdivisions) - 0.5f;
        float v = Fract(blockV * subdivisions) - 0.5f;
        float distance = (u * u + v * v) / (radius * radius);

        if (distance >= 1f)
            return 1f;

        float dip = 1f - distance;

        // Landing tangent, for the reason given on Lentil.
        return 1f - 0.8f * dip * dip;
    }

    /// <summary>Distance to the nearest whole-number line, in the range zero to a half.</summary>
    private static float EdgeDistance(float value)
    {
        float phase = Fract(value);

        return Math.Min(phase, 1f - phase);
    }

    private static float Chamfer(float distance, float groove) =>
        distance >= groove ? 1f : Smoothstep(distance / groove);

    private static float Smoothstep(float t) => t * t * (3f - 2f * t);

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    /// <summary>
    /// Which cell of the tile a coordinate falls in, wrapped into
    /// <c>0..count-1</c>. Every pattern that indexes a repeated part goes through
    /// this: a raw <c>floor</c> is what breaks tiling.
    /// </summary>
    private static int Index(float value, int count)
    {
        var cell = (int)MathF.Floor(value);

        return ((cell % count) + count) % count;
    }

    /// <summary>
    /// Stacked octaves of <see cref="Noise"/>, returned in the range zero to one.
    /// Each octave doubles both periods, so every one of them completes a whole
    /// number of cycles across the tile and the sum tiles as exactly as one layer
    /// does.
    /// </summary>
    /// <param name="periodU">Lattice cells across the tile in the first octave.</param>
    /// <param name="periodV">The same along the other axis; unequal values stretch the noise.</param>
    private static float Fbm(float u, float v, int periodU, int periodV, int octaves)
    {
        var sum = 0f;
        var amplitude = 1f;
        var total = 0f;

        for (var octave = 0; octave < octaves; octave++)
        {
            int factor = 1 << octave;

            sum += amplitude * Noise(u, v, periodU * factor, periodV * factor);
            total += amplitude;
            amplitude *= 0.5f;
        }

        return sum / total;
    }

    /// <summary>
    /// Value noise on a lattice that wraps: the corner a sample interpolates from
    /// is indexed modulo the period, so the field repeats after exactly one tile
    /// whatever the periods are. That is the whole reason this is written out here
    /// rather than taken from the engine — Godot's noise tiles by blending a skirt
    /// over the seam, which is a way of hiding one.
    /// </summary>
    private static float Noise(float u, float v, int periodU, int periodV)
    {
        float scaledU = u * periodU;
        float scaledV = v * periodV;

        var cellU = (int)MathF.Floor(scaledU);
        var cellV = (int)MathF.Floor(scaledV);

        // Quintic, not cubic: its second derivative vanishes at the lattice lines
        // too, so the normal map has no ridge along every cell boundary. With a
        // cubic ease the grid shows through as a faint quilt.
        float easeU = Fade(scaledU - cellU);
        float easeV = Fade(scaledV - cellV);

        float bottom = Lerp(
            Corner(cellU, cellV, periodU, periodV),
            Corner(cellU + 1, cellV, periodU, periodV),
            easeU);

        float top = Lerp(
            Corner(cellU, cellV + 1, periodU, periodV),
            Corner(cellU + 1, cellV + 1, periodU, periodV),
            easeU);

        return Lerp(bottom, top, easeV);
    }

    /// <summary>
    /// Fine parallel marks. The mark itself is <b>one-dimensional</b> noise —
    /// sampled at a fixed second coordinate, so it does not vary along the mark at
    /// all — and a slow two-dimensional field makes the marks start and stop.
    /// </summary>
    /// <remarks>
    /// Stretching ordinary noise was the obvious way to do this and it is the
    /// wrong one. A lattice pulled ten to one has cells forty texels tall, and
    /// interpolating between nodes that far apart is a smear, not a scratch — with
    /// the lattice itself showing through as a field of diamonds, which is what
    /// one octave of value noise looks like when you can see its cells. Here the
    /// fine direction gets a lattice fine enough to resolve, and the coherence
    /// along the mark costs nothing because it is simply the absence of a second
    /// coordinate.
    /// </remarks>
    private static float Streaks(float u, float v, int across, int along, int octaves)
    {
        // The fixed second coordinate is arbitrary and only has to be fixed.
        float marks = Fbm(u, 0.317f, across, 1, octaves);

        // Slow, and two-dimensional, so a mark fades in and out along its length
        // instead of running the whole surface.
        float breaks = Fbm(u, v, Math.Max(2, across / 6), along, 2);

        return Math.Clamp(marks * 0.68f + breaks * 0.32f, 0f, 1f);
    }

    /// <summary>
    /// Worley cells: the distance to the nearest of one feature point per lattice
    /// cell, normalised into zero to one. The cell indices wrap like
    /// <see cref="Noise"/>'s do, so the field of dents repeats exactly.
    /// </summary>
    private static float Cellular(float u, float v, int periodU, int periodV)
    {
        float scaledU = u * periodU;
        float scaledV = v * periodV;

        var homeU = (int)MathF.Floor(scaledU);
        var homeV = (int)MathF.Floor(scaledV);

        var nearest = float.MaxValue;

        for (var offsetV = -1; offsetV <= 1; offsetV++)
        {
            for (var offsetU = -1; offsetU <= 1; offsetU++)
            {
                int cellU = homeU + offsetU;
                int cellV = homeV + offsetV;

                float pointU = cellU + Corner(cellU, cellV, periodU, periodV);
                float pointV = cellV + Corner(cellU + 7919, cellV + 104729, periodU, periodV);

                float distanceU = pointU - scaledU;
                float distanceV = pointV - scaledV;

                nearest = Math.Min(nearest, distanceU * distanceU + distanceV * distanceV);
            }
        }

        return Math.Clamp(MathF.Sqrt(nearest), 0f, 1f);
    }

    private static float Corner(int u, int v, int periodU, int periodV) =>
        Hash(((u % periodU) + periodU) % periodU, ((v % periodV) + periodV) % periodV);

    /// <summary>A fixed scramble, so a facture is the same picture on every run.</summary>
    private static float Hash(int u, int v)
    {
        int mixed = u * 374761393 + v * 668265263;

        mixed = (mixed ^ (mixed >> 13)) * 1274126177;
        mixed ^= mixed >> 16;

        return (mixed & 0x7FFFFFFF) / (float)0x7FFFFFFF;
    }

    private static float Lerp(float from, float to, float t) => from + (to - from) * t;

    private static float Fract(float value) => value - MathF.Floor(value);
}
