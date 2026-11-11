using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The line a mirror plane throws onto the region's walls, the way a
/// construction laser marks the room and not just the part: the plane is
/// carried through the element's world transform — a rotation or a scale makes
/// the line slant — and cut against each wall rectangle. Where the plane lies
/// <i>in</i> a wall, the trace is the wall's own outline.
/// </summary>
/// <remarks>
/// Which walls carry the trace follows the box itself: <see cref="SceneBox"/>
/// culls front faces, so the walls on screen are exactly those whose outward
/// side faces away from the camera, and <see cref="IsWallVisible"/> is that
/// rule restated on the CPU. Everything here is engine-free arithmetic; the
/// node only turns the segments into a mesh.
/// </remarks>
internal static class SymmetryWallTrace
{
    /// <summary>
    /// Distances within this of zero count as on the plane. Loose for the same
    /// reason <c>GridAlignment.Epsilon</c> is: a right-angled transform is not
    /// exact in floating point, and a plane meant to lie in a wall must still
    /// read as lying in it after one.
    /// </summary>
    private const float Epsilon = 1e-4f;

    /// <summary>
    /// The mirror plane in world space: the local plane normal to
    /// <paramref name="axis"/> at half <paramref name="planeDoubled"/> cells,
    /// carried through <paramref name="elementWorld"/>. False when the basis
    /// has no usable inverse — a zero scale — which is also when the picker
    /// refuses to work; there is no plane to draw.
    /// </summary>
    public static bool TryGetWorldPlane(
        Transform3D elementWorld,
        int axis,
        int planeDoubled,
        out Plane plane)
    {
        plane = default;

        if (Mathf.Abs(elementWorld.Basis.Determinant()) < Epsilon)
            return false;

        Vector3 localNormal = default;
        localNormal[axis] = 1f;

        // Normals transform by the inverse transpose, not by the basis — the
        // two agree for the rotate-and-scale transforms elements can hold, but
        // only one of them is the rule.
        Vector3 worldNormal = (elementWorld.Basis.Inverse().Transposed() * localNormal).Normalized();
        Vector3 worldPoint = elementWorld * (localNormal * (planeDoubled / 2f));

        plane = new Plane(worldNormal, worldPoint);

        return true;
    }

    /// <summary>
    /// Whether the wall is one of those the box shows: its outward side faces
    /// away from the camera. From inside the region all six answer yes; from
    /// outside, the walls between the camera and the room drop out — the same
    /// faces <c>cull_front</c> drops on the GPU.
    /// </summary>
    public static bool IsWallVisible(
        int wallAxis,
        int wallSide,
        Vector3 boxSize,
        Vector3 cameraPosition)
    {
        return wallSide == 0
            ? cameraPosition[wallAxis] > 0f
            : cameraPosition[wallAxis] < boxSize[wallAxis];
    }

    /// <summary>
    /// The trace across one wall, as line-list endpoint pairs: a segment where
    /// the plane crosses the rectangle, the rectangle's four edges where it
    /// lies in it, nothing where it misses. A plane running exactly along a
    /// wall's edge yields that edge, because its two corners are the crossing
    /// points.
    /// </summary>
    public static IReadOnlyList<Vector3> TraceWall(
        Vector3 boxSize,
        int wallAxis,
        int wallSide,
        Plane plane)
    {
        IReadOnlyList<Vector3> corners = CreateWallCorners(boxSize, wallAxis, wallSide);

        var onPlane = 0;

        foreach (Vector3 corner in corners)
        {
            if (Mathf.Abs(plane.DistanceTo(corner)) <= Epsilon)
                onPlane++;
        }

        if (onPlane == corners.Count)
            return CreateOutline(corners);

        var pointsList = new List<Vector3>(capacity: 2);

        for (var index = 0; index < corners.Count; index++)
        {
            Vector3 start = corners[index];
            Vector3 end = corners[(index + 1) % corners.Count];
            float startDistance = plane.DistanceTo(start);
            float endDistance = plane.DistanceTo(end);

            if (Mathf.Abs(startDistance) <= Epsilon)
            {
                AddUnique(pointsList, start);

                continue;
            }

            if (Mathf.Abs(endDistance) <= Epsilon || startDistance > 0f == endDistance > 0f)
                continue;

            float t = startDistance / (startDistance - endDistance);

            AddUnique(pointsList, start.Lerp(end, t));
        }

        if (pointsList.Count != 2)
            return System.Array.Empty<Vector3>();

        return pointsList;
    }

    private static IReadOnlyList<Vector3> CreateWallCorners(
        Vector3 boxSize,
        int wallAxis,
        int wallSide)
    {
        int axisU = (wallAxis + 1) % 3;
        int axisV = (wallAxis + 2) % 3;
        float wall = wallSide == 0 ? 0f : boxSize[wallAxis];

        var corners = new Vector3[4];

        for (var index = 0; index < 4; index++)
        {
            Vector3 corner = default;
            corner[wallAxis] = wall;
            corner[axisU] = (index is 1 or 2) ? boxSize[axisU] : 0f;
            corner[axisV] = (index is 2 or 3) ? boxSize[axisV] : 0f;
            corners[index] = corner;
        }

        return corners;
    }

    private static IReadOnlyList<Vector3> CreateOutline(IReadOnlyList<Vector3> corners)
    {
        var pointsList = new List<Vector3>(capacity: 8);

        for (var index = 0; index < corners.Count; index++)
        {
            pointsList.Add(corners[index]);
            pointsList.Add(corners[(index + 1) % corners.Count]);
        }

        return pointsList;
    }

    private static void AddUnique(List<Vector3> pointsList, Vector3 point)
    {
        foreach (Vector3 existing in pointsList)
        {
            if (existing.DistanceTo(point) <= Epsilon)
                return;
        }

        pointsList.Add(point);
    }
}
