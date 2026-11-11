using System;
using System.Collections.Generic;

namespace VoxelCAD.Render;

/// <summary>
/// How much of the next sample each tile deserves, given what is being looked at.
/// </summary>
/// <remarks>
/// One rule: a tile fully inside the region asked for is worth
/// <see cref="Boost"/> times one nobody is looking at, and a tile half inside is
/// worth half way between. The interpolation is the point — a step at the edge of
/// the region would draw the region's own boundary in the noise, which is the
/// one artefact that would make this worse than not doing it.
/// <para>
/// Nothing is ever worth nought. A render is a picture somebody will save, so the
/// part off screen has to finish too; the priority decides the <em>order</em> and
/// never the total, and the finished picture is the same picture either way.
/// </para>
/// </remarks>
internal static class TilePriority
{
    /// <summary>
    /// How many times more often a tile on screen is sampled than one that is not.
    /// Eight is worth a great deal at the beginning of a render — where noise
    /// falls as the square root, eight times the samples is a third of it — and
    /// still leaves the rest of the picture visibly converging rather than frozen.
    /// </summary>
    public const float Boost = 8f;

    /// <summary>
    /// How far a tile's own noise may pull its share, either way. A tile twice as
    /// noisy as the average of the picture is sampled twice as often; the bounds
    /// stop a single stubborn tile from taking the whole render, and stop a quiet
    /// one from being abandoned to it.
    /// </summary>
    public const float LeastNoiseWeight = 0.25f;

    public const float MostNoiseWeight = 4f;

    /// <summary>
    /// A weight per tile. A region that covers the picture, or none at all, gives
    /// every tile the same weight — which is plain round robin, and is what the
    /// scheduler does when nobody has said where they are looking.
    /// </summary>
    public static float[] Weigh(RenderTileGrid grid, RenderTile region, IReadOnlyList<float>? noise = null)
    {
        ArgumentNullException.ThrowIfNull(grid);

        float[] weights = new float[grid.Count];

        if (region.IsEmpty)
            Array.Fill(weights, 1f);
        else
        {
            for (int i = 0; i < weights.Length; i++)
                weights[i] = 1f + (Boost - 1f) * grid.Overlap(i, region);
        }

        return noise is null || noise.Count != weights.Length ? weights : Roughen(weights, noise);
    }

    /// <summary>
    /// Multiplies in how noisy each tile still is, measured against the average of
    /// the picture. Multiplied rather than replacing: where somebody is looking and
    /// how far from settled it is are different questions, and a render answers
    /// both — a noisy corner off screen is still worth less than a noisy one under
    /// the eye.
    /// </summary>
    /// <remarks>
    /// Against the <em>average</em> rather than an absolute threshold, because what
    /// counts as noisy depends on the picture: a scene of flat colour and one full
    /// of caustics would need two different numbers, and neither would be one
    /// anybody could choose.
    /// </remarks>
    private static float[] Roughen(float[] weights, IReadOnlyList<float> noise)
    {
        double total = 0d;

        foreach (float value in noise)
            total += value;

        double average = total / noise.Count;

        if (average <= 1.0e-6d)
            return weights;

        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] *= Math.Clamp(
                (float)(noise[i] / average),
                LeastNoiseWeight,
                MostNoiseWeight);
        }

        return weights;
    }

    /// <summary>Whether two weightings differ enough to be worth re-aiming for.</summary>
    /// <remarks>
    /// The viewer reports where it is looking every frame, and re-weighting is
    /// cheap but not free; more to the point, a weighting that jittered with the
    /// last pixel of a pan would keep re-sorting the order for no visible gain.
    /// </remarks>
    public static bool Differ(IReadOnlyList<float> was, IReadOnlyList<float> wanted)
    {
        if (was.Count != wanted.Count)
            return true;

        for (int i = 0; i < was.Count; i++)
        {
            if (Math.Abs(was[i] - wanted[i]) > 0.01f)
                return true;
        }

        return false;
    }
}
