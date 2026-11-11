using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Ray against the voxels of one element and against the build region, returning
/// the cell that was hit and the face it was hit on.
/// </summary>
/// <remarks>
/// Everything is tested analytically rather than with physics bodies: voxels have
/// no colliders, and keeping a collision shape per cell in step with the mesh
/// would cost far more than the arithmetic. The voxel pass is linear in filled
/// cells — fine at this size, and noted in the ledger.
/// </remarks>
internal static class VoxelPicker
{
    private static readonly Vector3I[] FaceNormals =
    {
        new(-1, 0, 0), new(1, 0, 0),
        new(0, -1, 0), new(0, 1, 0),
        new(0, 0, -1), new(0, 0, 1)
    };

    /// <summary>
    /// The region's six faces, seen only from inside — the box is a room the user
    /// works in, so the near wall a ray enters through is not a hit, the far one
    /// is. Returns the inward normal, so the point plus half of it lands in the
    /// cell against that face.
    /// </summary>
    public static bool TryPickRegion(
        Vector3 origin,
        Vector3 direction,
        Vector3I size,
        out Vector3 point,
        out Vector3 inwardNormal,
        out float distance)
    {
        point = default;
        inwardNormal = default;
        distance = float.MaxValue;

        var found = false;
        var extent = new Vector3(size.X, size.Y, size.Z);

        for (var axis = 0; axis < 3; axis++)
        {
            for (var side = 0; side < 2; side++)
            {
                Vector3 outward = Vector3.Zero;
                outward[axis] = side == 0 ? -1f : 1f;

                // Only a face the ray is heading out through counts.
                if (direction.Dot(outward) <= 0f)
                    continue;

                var plane = new Plane(outward, side == 0 ? 0f : extent[axis]);

                if (plane.IntersectsRay(origin, direction) is not { } candidate)
                    continue;

                if (!IsWithinFace(candidate, extent, axis))
                    continue;

                var candidateDistance = origin.DistanceTo(candidate);

                if (candidateDistance >= distance)
                    continue;

                distance = candidateDistance;
                point = candidate;
                inwardNormal = -outward;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Ray against filled cells, in the element's own space.</summary>
    public static bool TryPickVoxels(
        Vector3 origin,
        Vector3 direction,
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        out Vector3I cell,
        out Vector3I face,
        out float distance) =>
        TryPickVoxels(origin, direction, voxels, null, null, out cell, out face, out distance);

    /// <summary>
    /// Ray against the cells as they stood before a stroke began: the cells in
    /// <paramref name="hidden"/> are read through as though empty, and the cells
    /// in <paramref name="restored"/> are met as though still filled. A brush
    /// stroke hands over what it has placed as the first and what it has erased
    /// as the second, so its own track never becomes the surface it paints on
    /// (see <see cref="BrushStroke"/>). Either may be null for "nothing".
    /// </summary>
    public static bool TryPickVoxels(
        Vector3 origin,
        Vector3 direction,
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        IReadOnlySet<Vector3I>? hidden,
        IReadOnlyCollection<Vector3I>? restored,
        out Vector3I cell,
        out Vector3I face,
        out float distance)
    {
        cell = default;
        face = default;
        distance = float.MaxValue;

        var found = false;

        foreach (Vector3I filled in voxels.Keys)
        {
            if (hidden != null && hidden.Contains(filled))
                continue;

            if (TryPickCell(origin, direction, filled, ref distance, out Vector3I hitFace))
            {
                cell = filled;
                face = hitFace;
                found = true;
            }
        }

        if (restored == null)
            return found;

        foreach (Vector3I filled in restored)
        {
            if (TryPickCell(origin, direction, filled, ref distance, out Vector3I hitFace))
            {
                cell = filled;
                face = hitFace;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// One cell of the walk above: nearer than the best so far, and with a face
    /// the ray actually entered through. Tightens <paramref name="distance"/> when
    /// it answers true.
    /// </summary>
    private static bool TryPickCell(
        Vector3 origin,
        Vector3 direction,
        Vector3I filled,
        ref float distance,
        out Vector3I face)
    {
        face = default;
        Vector3 min = new(filled.X, filled.Y, filled.Z);

        if (!TryRayBox(origin, direction, min, min + Vector3.One, out float candidate)
            || candidate >= distance)
        {
            return false;
        }

        if (!TryFaceNormal(origin + direction * candidate, min, direction, out face))
            return false;

        distance = candidate;

        return true;
    }

    /// <summary>
    /// Puts a region hit into the same shape a voxel hit has: the cell on the far
    /// side of the face, and the normal pointing back in, so <c>Cell + Face</c> is
    /// the empty cell against that wall either way.
    /// </summary>
    public static (Vector3I Cell, Vector3I Face) ToRegionPick(Vector3 point, Vector3I inwardFace)
    {
        Vector3I outside = Floor(point - (Vector3)inwardFace * 0.5f);

        return (outside, inwardFace);
    }

    /// <summary>Rounds a direction to the axis it points along most.</summary>
    public static Vector3I ToAxis(Vector3 direction)
    {
        var axis = 0;

        for (var i = 1; i < 3; i++)
        {
            if (Mathf.Abs(direction[i]) > Mathf.Abs(direction[axis]))
                axis = i;
        }

        Vector3I result = Vector3I.Zero;
        result[axis] = direction[axis] >= 0f ? 1 : -1;

        return result;
    }

    public static Vector3I Floor(Vector3 point) =>
        new(Mathf.FloorToInt(point.X), Mathf.FloorToInt(point.Y), Mathf.FloorToInt(point.Z));

    /// <summary>Whether a point on a face's plane lies within the face itself.</summary>
    private static bool IsWithinFace(Vector3 point, Vector3 extent, int normalAxis)
    {
        const float tolerance = 1e-3f;

        for (var axis = 0; axis < 3; axis++)
        {
            if (axis == normalAxis)
                continue;

            if (point[axis] < -tolerance || point[axis] > extent[axis] + tolerance)
                return false;
        }

        return true;
    }

    /// <summary>Which face of the cell the hit point sits on, facing the ray.</summary>
    private static bool TryFaceNormal(
        Vector3 hit,
        Vector3 min,
        Vector3 direction,
        out Vector3I face)
    {
        const float epsilon = 1e-3f;

        Vector3 max = min + Vector3.One;
        var distances = new[]
        {
            Mathf.Abs(hit.X - min.X), Mathf.Abs(hit.X - max.X),
            Mathf.Abs(hit.Y - min.Y), Mathf.Abs(hit.Y - max.Y),
            Mathf.Abs(hit.Z - min.Z), Mathf.Abs(hit.Z - max.Z)
        };

        face = default;
        var bestError = float.MaxValue;
        var found = false;

        for (var i = 0; i < FaceNormals.Length; i++)
        {
            if (distances[i] > epsilon || distances[i] >= bestError)
                continue;

            // Only faces turned towards the ray can be the one that was hit.
            if (((Vector3)FaceNormals[i]).Dot(-direction) <= 0f)
                continue;

            bestError = distances[i];
            face = FaceNormals[i];
            found = true;
        }

        return found;
    }

    /// <summary>Slab test: where the ray crosses each pair of parallel planes.</summary>
    private static bool TryRayBox(
        Vector3 origin,
        Vector3 direction,
        Vector3 min,
        Vector3 max,
        out float distance)
    {
        distance = 0f;

        Vector3 inverse = new(
            Mathf.IsZeroApprox(direction.X) ? float.PositiveInfinity : 1f / direction.X,
            Mathf.IsZeroApprox(direction.Y) ? float.PositiveInfinity : 1f / direction.Y,
            Mathf.IsZeroApprox(direction.Z) ? float.PositiveInfinity : 1f / direction.Z);

        float x1 = (min.X - origin.X) * inverse.X;
        float x2 = (max.X - origin.X) * inverse.X;
        float y1 = (min.Y - origin.Y) * inverse.Y;
        float y2 = (max.Y - origin.Y) * inverse.Y;
        float z1 = (min.Z - origin.Z) * inverse.Z;
        float z2 = (max.Z - origin.Z) * inverse.Z;

        float nearest = Mathf.Max(
            Mathf.Max(Mathf.Min(x1, x2), Mathf.Min(y1, y2)),
            Mathf.Min(z1, z2));
        float farthest = Mathf.Min(
            Mathf.Min(Mathf.Max(x1, x2), Mathf.Max(y1, y2)),
            Mathf.Max(z1, z2));

        if (farthest < 0f || nearest > farthest)
            return false;

        distance = nearest >= 0f ? nearest : farthest;

        return distance >= 0f;
    }
}
