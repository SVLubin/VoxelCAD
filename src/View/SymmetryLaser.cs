using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The line a mirror plane draws on the model, the way a construction laser
/// marks a wall: the outline of the plane's footprint — the columns of cells
/// the plane passes through or lies against — as a line list in the element's
/// own space.
/// </summary>
/// <remarks>
/// Deliberately unsorted against the scene's depth: the contour shows through
/// the model, sighted from anywhere. That is what removes the depth fight a
/// line lying exactly on a face would otherwise pick, and what makes the
/// geometry camera-independent — unlike the erase marker, nothing here is
/// re-derived per frame; the mesh changes only when the cells or the plane do.
/// </remarks>
internal static class SymmetryLaser
{
    /// <summary>
    /// The contour as line-list endpoint pairs. The footprint collects the
    /// layers whose cells touch the plane — the single layer a centre plane
    /// (odd doubled) runs through, the two beside a boundary plane (even
    /// doubled) — and an edge belongs to the contour when the column across it
    /// is outside the footprint.
    /// </summary>
    internal static IReadOnlyList<Vector3> CreateContourPoints(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        int axis,
        int planeDoubled)
    {
        // One floor-division formula covers both parities: for an odd doubled
        // plane the two layers coincide, for an even one they differ by one.
        var layerHigh = (int)Math.Floor(planeDoubled / 2.0);
        var layerLow = (int)Math.Floor((planeDoubled - 1) / 2.0);

        int axisU = (axis + 1) % 3;
        int axisV = (axis + 2) % 3;

        var footprintHashSet = new HashSet<(int U, int V)>();

        foreach (Vector3I cell in voxels.Keys)
        {
            if (cell[axis] == layerLow || cell[axis] == layerHigh)
                footprintHashSet.Add((cell[axisU], cell[axisV]));
        }

        float plane = planeDoubled / 2f;
        var pointsList = new List<Vector3>();

        foreach ((int u, int v) in footprintHashSet)
        {
            if (!footprintHashSet.Contains((u - 1, v)))
                AppendEdge(pointsList, axis, plane, axisU, axisV, u, v, u, v + 1);

            if (!footprintHashSet.Contains((u + 1, v)))
                AppendEdge(pointsList, axis, plane, axisU, axisV, u + 1, v, u + 1, v + 1);

            if (!footprintHashSet.Contains((u, v - 1)))
                AppendEdge(pointsList, axis, plane, axisU, axisV, u, v, u + 1, v);

            if (!footprintHashSet.Contains((u, v + 1)))
                AppendEdge(pointsList, axis, plane, axisU, axisV, u, v + 1, u + 1, v + 1);
        }

        return pointsList;
    }

    public static ArrayMesh Build(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        int axis,
        int planeDoubled)
    {
        IReadOnlyList<Vector3> points = CreateContourPoints(voxels, axis, planeDoubled);
        var mesh = new ArrayMesh();

        if (points.Count == 0)
            return mesh;

        var vertices = new Vector3[points.Count];

        for (var index = 0; index < points.Count; index++)
            vertices[index] = points[index];

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;

        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

        return mesh;
    }

    private static void AppendEdge(
        List<Vector3> pointsList,
        int axis,
        float plane,
        int axisU,
        int axisV,
        int startU,
        int startV,
        int endU,
        int endV)
    {
        pointsList.Add(CreatePoint(axis, plane, axisU, startU, axisV, startV));
        pointsList.Add(CreatePoint(axis, plane, axisU, endU, axisV, endV));
    }

    private static Vector3 CreatePoint(
        int axis,
        float plane,
        int axisU,
        float u,
        int axisV,
        float v)
    {
        Vector3 point = default;
        point[axis] = plane;
        point[axisU] = u;
        point[axisV] = v;

        return point;
    }
}
