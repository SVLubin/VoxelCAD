using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Geometry;

/// <summary>
/// The box a set of filled cells occupies, in the coordinates the cells are
/// numbered in. A cell spans from its own coordinate to the next one, so the two
/// corners are read differently: <see cref="Min"/> and <see cref="Max"/> are cells
/// and both ends are inclusive, while <see cref="Corner"/> and
/// <see cref="FarCorner"/> are the box's own faces and the far one is the highest
/// cell plus one — the half-open convention the mesher and the picker already use.
/// </summary>
/// <remarks>
/// Four layers ask this and must not answer it differently: the viewport draws the
/// selection box and frames the scene by it, the command surface reports it as
/// <c>bounds</c>, and the model resolves a Centre pivot from its middle. It lived
/// here twice over before this — once in <c>View</c> and once in <c>Commands</c> —
/// which is the bar this assembly exists for.
/// </remarks>
public readonly struct VoxelExtent
{
    private readonly bool _hasCells;

    private VoxelExtent(Vector3I min, Vector3I max)
    {
        _hasCells = true;
        Min = min;
        Max = max;
    }

    /// <summary>True for a set with no filled cells, which is what <c>default</c> is.</summary>
    public bool IsEmpty => !_hasCells;

    /// <summary>Lowest filled cell, inclusive.</summary>
    public Vector3I Min { get; }

    /// <summary>Highest filled cell, inclusive.</summary>
    public Vector3I Max { get; }

    /// <summary>Inclusive of both corners, so a single cell measures one by one by one.</summary>
    public Vector3I Size => IsEmpty ? Vector3I.Zero : Max - Min + Vector3I.One;

    /// <summary>The near face of the box, at the low corner of the lowest cell.</summary>
    public Vector3 Corner => new(Min.X, Min.Y, Min.Z);

    /// <summary>The far face of the box, past the high corner of the highest cell.</summary>
    public Vector3 FarCorner => new(Max.X + 1f, Max.Y + 1f, Max.Z + 1f);

    /// <summary>
    /// Twice the box's middle, which is exactly representable where the middle
    /// itself is not: a run of an odd number of cells is centred on a cell
    /// boundary and an even one on a cell centre, so the middle lands on the
    /// half-cell lattice — the same one a mirror plane lives on.
    /// </summary>
    public Vector3I CenterDoubled => IsEmpty ? Vector3I.Zero : Min + Max + Vector3I.One;

    /// <summary>The box's middle. Zero for an empty set, which has no middle.</summary>
    public Vector3 Center
    {
        get
        {
            Vector3I doubled = CenterDoubled;

            return new Vector3(doubled.X, doubled.Y, doubled.Z) * 0.5f;
        }
    }

    public static VoxelExtent Measure(IEnumerable<Vector3I> cells)
    {
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        var found = false;

        foreach (Vector3I cell in cells)
        {
            min = new Vector3I(
                Mathf.Min(min.X, cell.X),
                Mathf.Min(min.Y, cell.Y),
                Mathf.Min(min.Z, cell.Z));

            max = new Vector3I(
                Mathf.Max(max.X, cell.X),
                Mathf.Max(max.Y, cell.Y),
                Mathf.Max(max.Z, cell.Z));

            found = true;
        }

        return found ? new VoxelExtent(min, max) : default;
    }
}
