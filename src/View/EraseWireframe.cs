using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The erase marker's geometry: which of a cube's six faces the camera can see,
/// and the border-plus-X line pattern drawn on each of them.
/// </summary>
/// <remarks>
/// A cube always shows exactly three faces to a camera in general position — the
/// three whose outward normal has a positive component towards the eye. Drawing
/// only those (instead of a solid cube, or all six) is what lets the marker sit
/// on top of the voxel it marks without duplicating its far side or fighting it
/// for depth.
/// </remarks>
internal static class EraseWireframe
{
    private static readonly Vector3[] FaceNormals =
    {
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1)
    };

    /// <summary>
    /// Corners of a unit cube centred on the origin (matching <c>BoxMesh</c>'s own
    /// convention), one quad per face, in the same face order as <see cref="FaceNormals"/>.
    /// </summary>
    private static readonly Vector3[][] FaceCorners =
    {
        new[] { new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f) },
        new[] { new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, 0.5f) },
        new[] { new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f) },
        new[] { new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f) },
        new[] { new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f) },
        new[] { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f) }
    };

    /// <summary>
    /// The engine-free decision on its own: <paramref name="viewDirection"/> is
    /// the direction from the cube's centre towards the camera, in the cube's own
    /// (unrotated) frame. A face qualifies when the eye sits on its outward side.
    /// </summary>
    internal static IReadOnlyList<int> VisibleFaces(Vector3 viewDirection)
    {
        var visible = new List<int>(3);

        for (var face = 0; face < FaceNormals.Length; face++)
        {
            if (FaceNormals[face].Dot(viewDirection) > 0f)
                visible.Add(face);
        }

        return visible;
    }

    /// <summary>Border and the two diagonals of one face, as line-list endpoint pairs.</summary>
    internal static void AppendFaceLines(List<Vector3> points, int face)
    {
        Vector3[] corners = FaceCorners[face];

        points.Add(corners[0]); points.Add(corners[1]);
        points.Add(corners[1]); points.Add(corners[2]);
        points.Add(corners[2]); points.Add(corners[3]);
        points.Add(corners[3]); points.Add(corners[0]);

        points.Add(corners[0]); points.Add(corners[2]);
        points.Add(corners[1]); points.Add(corners[3]);
    }

    /// <summary>
    /// The mesh for one cell: an X on each of the (up to three) faces turned
    /// towards the camera. Rebuilding this is cheap — a handful of line
    /// segments — which matters because the camera can turn without the mouse
    /// moving, so the caller re-derives it every frame rather than only on hover.
    /// </summary>
    public static ArrayMesh Build(Vector3 viewDirection)
    {
        var points = new List<Vector3>();

        foreach (int face in VisibleFaces(viewDirection))
            AppendFaceLines(points, face);

        return ToLineMesh(points);
    }

    private static ArrayMesh ToLineMesh(List<Vector3> points)
    {
        var mesh = new ArrayMesh();

        if (points.Count == 0)
            return mesh;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points.ToArray();

        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

        return mesh;
    }
}
