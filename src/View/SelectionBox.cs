using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The wireframe parallelepiped drawn around a selected element: the twelve edges
/// of the box its voxels occupy, as a line list.
/// </summary>
/// <remarks>
/// Edges rather than a translucent solid, because the box has to read as a border
/// around geometry the user is still looking at. It is drawn as a child of the
/// element, so the engine's own transform gives it the element's rotation and
/// scale and the "parallelepiped" part costs nothing here — this only ever works
/// in the element's own coordinates.
/// </remarks>
internal static class SelectionBox
{
    /// <summary>
    /// The twelve edges as endpoint pairs. Four edges along each axis, taken in
    /// axis order, so the walk is a pure function of the corner pair rather than a
    /// table anyone has to keep in step with the corner numbering.
    /// </summary>
    internal static IReadOnlyList<Vector3> CreateEdgePoints(Vector3 min, Vector3 max)
    {
        var points = new List<Vector3>(24);

        for (var axis = 0; axis < 3; axis++)
        {
            // The two axes the edge does not run along; each of their four
            // low/high combinations puts one edge on the box.
            int first = (axis + 1) % 3;
            int second = (axis + 2) % 3;

            for (var corner = 0; corner < 4; corner++)
            {
                Vector3 start = min;
                start[first] = (corner & 1) == 0 ? min[first] : max[first];
                start[second] = (corner & 2) == 0 ? min[second] : max[second];

                Vector3 end = start;
                end[axis] = max[axis];

                points.Add(start);
                points.Add(end);
            }
        }

        return points;
    }

    /// <summary>
    /// How far along its own edge each vertex sits, in the element's cells: zero at
    /// one end and the edge's length at the other. Interpolated across the line, it
    /// is the coordinate a dash pattern runs on — which is why it is carried in the
    /// mesh rather than derived in the shader, where a line primitive has nothing
    /// to derive it from.
    /// </summary>
    /// <remarks>
    /// Measured per edge, so every edge starts its pattern at a corner and the box
    /// reads as twelve marked-out lengths rather than one pattern the corners
    /// happen to fall in the middle of. The unit is the cell, so the dashes count
    /// cells — the same ruler the grid on the walls uses.
    /// </remarks>
    internal static IReadOnlyList<float> CreateEdgeDistances(IReadOnlyList<Vector3> points)
    {
        var distances = new float[points.Count];

        for (var index = 0; index < points.Count; index += 2)
        {
            distances[index] = 0f;
            distances[index + 1] = points[index].DistanceTo(points[index + 1]);
        }

        return distances;
    }

    public static ArrayMesh Build(Vector3 min, Vector3 max)
    {
        IReadOnlyList<Vector3> points = CreateEdgePoints(min, max);
        IReadOnlyList<float> distances = CreateEdgeDistances(points);
        var vertices = new Vector3[points.Count];
        var uvs = new Vector2[points.Count];

        for (var index = 0; index < points.Count; index++)
        {
            vertices[index] = points[index];
            uvs[index] = new Vector2(distances[index], 0f);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

        return mesh;
    }
}
