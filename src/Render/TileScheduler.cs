using System;
using System.Collections.Generic;

namespace VoxelCAD.Render;

/// <summary>
/// Which tile the next sample goes to, and how many each has had.
/// </summary>
/// <remarks>
/// Weighted round robin by virtual time: the tile chosen is the one with the
/// least <c>taken / weight</c>. Three properties fall out of that one line, and
/// all three are what this had to have.
/// <list type="bullet">
/// <item>In the long run each tile gets samples in proportion to its weight,
/// exactly — no accumulator drifts, because the ratio <em>is</em> the state.</item>
/// <item>Nothing starves. Every weight is above nought, so every tile's virtual
/// time rises and the least of them is eventually somebody else.</item>
/// <item>The first sweep is complete. Every tile starts at virtual time nought
/// whatever its weight, so the picture is covered once before any of it is
/// covered twice — which is why a render still <em>appears</em> whole
/// immediately rather than growing out of the middle.</item>
/// </list>
/// <para>
/// A tile that has had everything asked of it drops out and the rest carry on, so
/// the total work is fixed: priority changes when a pixel converges, never what
/// it converges to.
/// </para>
/// </remarks>
internal sealed class TileScheduler
{
    private readonly int[] _takenList;
    private float[] _weightsList;
    private int _wanted;

    public TileScheduler(int tiles, int wanted)
    {
        if (tiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(tiles));

        _takenList = new int[tiles];
        _weightsList = new float[tiles];
        _wanted = Math.Max(wanted, 1);

        Array.Fill(_weightsList, 1f);
    }

    public int Tiles => _takenList.Length;

    public int Wanted => _wanted;

    /// <summary>
    /// The samples the least-sampled tile has had — what the whole picture is
    /// worth, rather than what its best corner is worth.
    /// </summary>
    public int Minimum
    {
        get
        {
            int least = int.MaxValue;

            foreach (int taken in _takenList)
                least = Math.Min(least, taken);

            return least;
        }
    }

    /// <summary>The samples the best-served tile has had.</summary>
    public int Peak
    {
        get
        {
            int most = 0;

            foreach (int taken in _takenList)
                most = Math.Max(most, taken);

            return most;
        }
    }

    /// <summary>Every sample every tile has had, which is what a rate is measured in.</summary>
    public long Total
    {
        get
        {
            long total = 0;

            foreach (int taken in _takenList)
                total += taken;

            return total;
        }
    }

    /// <summary>How far along the whole render is, from nought to one.</summary>
    public float Fraction => (float)((double)Total / ((double)_wanted * Tiles));

    public bool IsFinished => Minimum >= _wanted;

    public IReadOnlyList<float> Weights => _weightsList;

    /// <summary>
    /// Aims at a region. What has already been traced is kept: a scheduler is
    /// about what to do next, and moving the view must never throw away samples.
    /// </summary>
    public void SetWeights(float[] weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        if (weights.Length != _takenList.Length)
            throw new ArgumentException("A weight per tile, and no more.", nameof(weights));

        _weightsList = weights;
    }

    /// <summary>
    /// Asks for more, or fewer. Lowering it below what a tile already has leaves
    /// that tile alone — a render is never un-traced — and simply finishes it.
    /// </summary>
    public void SetWanted(int wanted) =>
        _wanted = Math.Max(wanted, 1);

    /// <summary>
    /// The tile the next sample belongs to, or false when every one of them has
    /// had what was asked for.
    /// </summary>
    public bool TryNext(out int tile)
    {
        tile = -1;

        double best = double.MaxValue;

        for (int i = 0; i < _takenList.Length; i++)
        {
            if (_takenList[i] >= _wanted)
                continue;

            double virtualTime = _takenList[i] / (double)Math.Max(_weightsList[i], 1.0e-3f);

            if (virtualTime >= best)
                continue;

            best = virtualTime;
            tile = i;
        }

        return tile >= 0;
    }

    /// <summary>How many samples a tile has had, which is what the resolve divides by.</summary>
    public int TakenAt(int tile) => _takenList[tile];

    public void Record(int tile) => _takenList[tile]++;
}
