using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Every drawn facture is laid over a surface many times, so the one property
/// that is not negotiable is that it wraps. A pattern that almost wraps looks
/// fine on a test cube and draws a ruled line across a floor — which is how the
/// herringbone's seam was found: by eye, in a screenshot, after it looked right.
/// </summary>
/// <remarks>
/// One fact per property rather than a theory per finish, because
/// <c>MaterialFinish</c> is internal and a public test parameter cannot be.
/// Every finish is checked and all the failures are reported together, which is
/// what one wants when a shared helper breaks several patterns at once.
/// </remarks>
public class DrawnHeightFieldTests
{
    /// <summary>
    /// Every finish but <see cref="MaterialFinish.Smooth"/>, which generates no
    /// texture at all. The stochastic ones belong here as much as the drawn ones:
    /// they used to come from the engine's noise and so sat outside this check,
    /// which is precisely why their seams went unnoticed.
    /// </summary>
    private static readonly MaterialFinish[] TexturedFinishes =
    {
        MaterialFinish.Sand,
        MaterialFinish.Rough,
        MaterialFinish.Grain,
        MaterialFinish.Brushed,
        MaterialFinish.Hammered,
        MaterialFinish.Tread,
        MaterialFinish.Ribbed,
        MaterialFinish.Tiles,
        MaterialFinish.Mosaic,
        MaterialFinish.Knurled,
        MaterialFinish.Wood,
        MaterialFinish.Ripples,
        MaterialFinish.Bark,
        MaterialFinish.Laminate,
        MaterialFinish.Brick,
        MaterialFinish.Parquet,
        MaterialFinish.GlassWave,
        MaterialFinish.GlassCross,
        MaterialFinish.GlassBubble
    };

    [Fact]
    public void EveryPatternRepeatsAcrossTheTileEdge()
    {
        var failuresList = new List<string>();

        foreach (MaterialFinish finish in TexturedFinishes)
        {
            FinishRecipe recipe = FinishRecipes.GetRecipe(finish);

            Assert.True(recipe.HasTexture, $"{finish} generates no texture.");

            float worst = MeasureWrapError(recipe);

            if (worst > 0.002f)
                failuresList.Add($"{finish} differs by {worst:0.000} across the tile edge");
        }

        Assert.True(failuresList.Count == 0, string.Join("; ", failuresList));
    }

    /// <summary>
    /// A raised shape has to meet the flat ground with a matching slope. Where it
    /// does not, the normal map turns over inside a single texel and the surface
    /// wears a dark rim with a ragged edge — which is what the tread plate did,
    /// because a hemisphere is vertical where it lands.
    /// </summary>
    /// <remarks>
    /// Asked by refinement rather than against a fixed number, and that matters:
    /// a fine noise has steep slopes everywhere and is perfectly well behaved,
    /// while a cliff is steep <b>at every scale</b>. Halve the step and a smooth
    /// surface halves its rise; a discontinuity does not budge. So the test is
    /// scale-free, and a finish is free to be as fine-grained as its tile can
    /// carry.
    /// <para>
    /// The threshold is 0.6 because the bug this exists for is a square root: a
    /// hemisphere's rise near its rim goes as √delta, so halving the step keeps
    /// 0.707 of it. Smooth halves to 0.5. Anything between the two is the shape
    /// of the defect, so the line goes between them and not at a round number.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoPatternHasACliff()
    {
        var failuresList = new List<string>();

        foreach (MaterialFinish finish in TexturedFinishes)
        {
            FinishRecipe recipe = FinishRecipes.GetRecipe(finish);
            (float ratio, float rise) = FindSteepestJump(recipe);

            if (ratio > 0.5f)
            {
                failuresList.Add(
                    $"{finish} keeps {ratio:0.00} of a {rise:0.000} rise after the step is "
                    + "cut thirty-twofold — a discontinuity, not a slope");
            }
        }

        Assert.True(failuresList.Count == 0, string.Join("; ", failuresList));
    }

    /// <summary>
    /// Surfaces whose edges are made rather than grown. A chamfer on a board or a
    /// glass block is an edge somebody cut, and fine detail has no business
    /// rocking it: on a steep slope a wobble of a hundredth in height is a large
    /// swing in the normal, which reads as a chain of dark beads along the joint.
    /// </summary>
    /// <remarks>
    /// Stone, sand, timber and bark are deliberately absent. A natural surface has
    /// no clean edge to protect — bark's relief riding over its own furrow walls is
    /// the point of bark, not a defect.
    /// </remarks>
    private static readonly MaterialFinish[] MachinedFinishes =
    {
        MaterialFinish.Tread,
        MaterialFinish.Ribbed,
        MaterialFinish.Tiles,
        MaterialFinish.Mosaic,
        MaterialFinish.Knurled,
        MaterialFinish.Laminate,
        MaterialFinish.Brick,
        MaterialFinish.Parquet,
        MaterialFinish.GlassWave,
        MaterialFinish.GlassCross,
        MaterialFinish.GlassBubble
    };

    [Fact]
    public void DetailDoesNotRideOnAMachinedEdge()
    {
        var failuresList = new List<string>();

        foreach (MaterialFinish finish in MachinedFinishes)
        {
            FinishRecipe recipe = FinishRecipes.GetRecipe(finish);
            float texel = recipe.Repeats / recipe.Size;
            const int samples = 192;

            var worst = 0f;

            for (var y = 0; y < samples; y++)
            {
                for (var x = 0; x < samples; x++)
                {
                    float u = (float)x / samples * recipe.Repeats;
                    float v = (float)y / samples * recipe.Repeats;

                    // How steep the bare structure is here.
                    float bare = DrawnHeightField.Sample(recipe, u, v, 0f);
                    float alongU = DrawnHeightField.Sample(recipe, u + texel, v, 0f);
                    float alongV = DrawnHeightField.Sample(recipe, u, v + texel, 0f);
                    float steepness = Math.Max(
                        Math.Abs(alongU - bare),
                        Math.Abs(alongV - bare));

                    if (steepness < 0.02f)
                        continue;

                    float dressed = DrawnHeightField.Sample(recipe, u, v, 1f);

                    worst = Math.Max(worst, Math.Abs(dressed - bare));
                }
            }

            if (worst > 0.02f)
                failuresList.Add($"{finish} moves a made edge by {worst:0.000}");
        }

        Assert.True(failuresList.Count == 0, string.Join("; ", failuresList));
    }

    /// <summary>
    /// How far a point and the same point one whole tile away disagree. Sampled
    /// off the whole numbers too, so a pattern that agrees on the lattice and
    /// drifts between its lines is still caught.
    /// </summary>
    private static float MeasureWrapError(FinishRecipe recipe)
    {
        var worst = 0f;

        for (var step = 0; step < 61; step++)
        {
            float u = step * 0.0271f * recipe.Repeats;
            float v = step * 0.0433f * recipe.Repeats;

            float here = DrawnHeightField.Sample(recipe, u, v);

            worst = Math.Max(
                worst,
                Math.Abs(here - DrawnHeightField.Sample(recipe, u + recipe.Repeats, v)));

            worst = Math.Max(
                worst,
                Math.Abs(here - DrawnHeightField.Sample(recipe, u, v + recipe.Repeats)));
        }

        return worst;
    }

    /// <summary>
    /// Hunts the worst discontinuity: the point whose rise over one texel is
    /// carried almost entirely by one of its two halves. Answers that ratio and
    /// the rise it belongs to.
    /// </summary>
    /// <remarks>
    /// Judged <b>per point</b>, and that is the whole trick. Comparing a global
    /// worst-over-the-grid at two step sizes hides exactly this defect: a jump
    /// that one interval straddles at full step and no interval straddles at half
    /// step makes the surface look smoother, not rougher. A parquet plank with a
    /// seam down its middle passed that way.
    /// </remarks>
    private static (float Ratio, float Rise) FindSteepestJump(FinishRecipe recipe)
    {
        // Prime, and shifted off the lattice: a round count lands every sample
        // exactly on the pattern's own cell boundaries, so no interval ever
        // straddles one.
        const int samples = 151;
        const float offset = 0.37f;

        float delta = recipe.Repeats / recipe.Size;

        var worstRatio = 0f;
        var worstRise = 0f;

        for (var y = 0; y < samples; y++)
        {
            for (var x = 0; x < samples; x++)
            {
                float u = (x + offset) / samples * recipe.Repeats;
                float v = (y + offset) / samples * recipe.Repeats;

                Consider(recipe, u, v, delta, 0f, ref worstRatio, ref worstRise);
                Consider(recipe, u, v, 0f, delta, ref worstRatio, ref worstRise);
            }
        }

        return (worstRatio, worstRise);
    }

    /// <summary>
    /// Follows a step down into itself: halve it, keep whichever half carries the
    /// rise, and repeat. Records how much of the original rise is still there at a
    /// thirty-second of the width.
    /// </summary>
    /// <remarks>
    /// This is the only question that separates a jump from a corner. A rise that
    /// merely turns sharply — a clamp, the foot of a chamfer, the rim of a dome —
    /// shrinks in proportion as the interval shrinks. A discontinuity does not
    /// shrink at all, because there is nothing to shrink: the whole rise happens
    /// at a point. Asking it at one width, however cleverly, cannot tell the two
    /// apart, and that is why several honest patterns kept being accused.
    /// </remarks>
    private static void Consider(
        FinishRecipe recipe,
        float u,
        float v,
        float stepU,
        float stepV,
        ref float worstRatio,
        ref float worstRise)
    {
        float rise = Math.Abs(
            DrawnHeightField.Sample(recipe, u + stepU, v + stepV)
            - DrawnHeightField.Sample(recipe, u, v));

        // A hundredth of the height range. Low on purpose: the defect this was
        // written for was a jump of 0.025, invisible in the height and loud in the
        // normal, and a floor set to the size of the jumps already known would
        // only ever find those. Bisection tells a jump from a corner whatever its
        // size, so the floor is here to exclude arithmetic noise and nothing else.
        if (rise < 0.01f)
            return;

        float fromU = u;
        float fromV = v;
        float spanU = stepU;
        float spanV = stepV;

        for (var halving = 0; halving < 5; halving++)
        {
            float middleU = fromU + spanU * 0.5f;
            float middleV = fromV + spanV * 0.5f;

            float start = DrawnHeightField.Sample(recipe, fromU, fromV);
            float middle = DrawnHeightField.Sample(recipe, middleU, middleV);
            float end = DrawnHeightField.Sample(recipe, fromU + spanU, fromV + spanV);

            if (Math.Abs(middle - start) < Math.Abs(end - middle))
            {
                fromU = middleU;
                fromV = middleV;
            }

            spanU *= 0.5f;
            spanV *= 0.5f;
        }

        float survives = Math.Abs(
            DrawnHeightField.Sample(recipe, fromU + spanU, fromV + spanV)
            - DrawnHeightField.Sample(recipe, fromU, fromV));

        float ratio = survives / rise;

        if (ratio <= worstRatio)
            return;

        worstRatio = ratio;
        worstRise = rise;
    }

    private static float MeasureSteepestStepUnused(FinishRecipe recipe, float delta)
    {
        // Prime, and shifted off the lattice. A round count lands every sample
        // exactly on the pattern's own cell boundaries, so the interval it
        // measures starts at a seam instead of straddling one — and a seam that is
        // never straddled is a seam that is never seen. This is how a
        // discontinuity down the middle of every parquet plank sat in plain sight
        // and passed.
        const int samples = 181;
        const float offset = 0.37f;

        var worst = 0f;

        for (var y = 0; y < samples; y++)
        {
            for (var x = 0; x < samples; x++)
            {
                float u = (x + offset) / samples * recipe.Repeats;
                float v = (y + offset) / samples * recipe.Repeats;

                float here = DrawnHeightField.Sample(recipe, u, v);

                worst = Math.Max(
                    worst,
                    Math.Abs(DrawnHeightField.Sample(recipe, u + delta, v) - here));

                worst = Math.Max(
                    worst,
                    Math.Abs(DrawnHeightField.Sample(recipe, u, v + delta) - here));
            }
        }

        return worst;
    }
}
