using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The view cube's geometry: 54 cell quads, nine to a face, carrying the colour
/// that says which axis the face is and which one the pointer is on.
/// </summary>
/// <remarks>
/// The cells are contiguous and the cube is solid: the grid between them is drawn
/// by the material, not left as a gap. That is the scene box's own technique
/// (<see cref="SceneBox"/>) — a hairline of the surface's own colour darkened,
/// measured with <c>fwidth</c> so it stays the same width on screen — and using it
/// here is what makes the widget's grid read as the same grid the scene has,
/// rather than as black seams cut into a small object.
/// <para>
/// Nothing is coincident with anything, so there is no depth fudge and no nudged
/// offset. Adjacent cells share an edge; they never overlap.
/// </para>
/// <para>
/// The colours are flat and the material is unshaded. That is not the lighting
/// model being faked: these faces are <em>identifiers</em>, the way the transform
/// handles are, and they have to read the same whichever way the widget is turned.
/// Nothing about the scene's shading is hidden or stood in for — the widget renders
/// in a world of its own with no lights in it at all.
/// </para>
/// </remarks>
internal static class ViewCubeMesh
{
    /// <summary>
    /// One third of a face, which is the spacing the material draws its grid at.
    /// </summary>
    public const float CellSize = ViewCubeGeometry.HalfExtent * 2f / 3f;

    /// <summary>
    /// How far each face colour is lightened towards white. Pastel, because these
    /// are chrome rather than the saturated handles they take their hue from, and
    /// the two ends of an axis differ so a face says which way it points and not
    /// merely which axis it is on.
    /// </summary>
    private const float PositiveFaceTint = 0.55f;

    private const float NegativeFaceTint = 0.3f;

    /// <summary>
    /// The 54 cells, with <paramref name="hovered"/>'s own cells in the hover
    /// colour. A corner zone lights on three faces and an edge zone on two, which
    /// falls out of asking each cell which zone it belongs to rather than of any
    /// special case.
    /// </summary>
    /// <remarks>
    /// Normals are written even though the material is unshaded: the grid shader
    /// reads them to pick which pair of axes lies in the face's plane, exactly as
    /// the scene box's does.
    /// </remarks>
    public static ArrayMesh BuildCells(Vector3I? hovered)
    {
        var verticesList = new List<Vector3>(324);
        var normalsList = new List<Vector3>(324);
        var colorsList = new List<Color>(324);

        for (var axis = 0; axis < 3; axis++)
        {
            for (var sign = -1; sign <= 1; sign += 2)
                AppendFace(verticesList, normalsList, colorsList, axis, sign, hovered);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verticesList.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normalsList.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colorsList.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return mesh;
    }

    /// <summary>
    /// One face's nine cells. The two in-plane axes are taken in cyclic order, so
    /// <c>u × v</c> is the positive axis direction — which is what lets a cell's
    /// place in the plane be turned straight into the zone it belongs to, with no
    /// per-face table to keep in step.
    /// </summary>
    private static void AppendFace(
        List<Vector3> verticesList,
        List<Vector3> normalsList,
        List<Color> colorsList,
        int axis,
        int sign,
        Vector3I? hovered)
    {
        int axisU = (axis + 1) % 3;
        int axisV = (axis + 2) % 3;

        Vector3 normal = GetUnit(axis) * sign;
        Vector3 u = GetUnit(axisU);
        Vector3 v = GetUnit(axisV);
        Vector3 origin = normal * ViewCubeGeometry.HalfExtent;

        Color face = GetFaceColor(axis, sign);

        for (var cellU = 0; cellU < 3; cellU++)
        for (var cellV = 0; cellV < 3; cellV++)
        {
            Vector3I zone = Vector3I.Zero;
            zone[axis] = sign;
            zone[axisU] = cellU - 1;
            zone[axisV] = cellV - 1;

            (float lowU, float highU) = GetCellSpan(cellU);
            (float lowV, float highV) = GetCellSpan(cellV);

            Vector3 corner = origin + u * lowU + v * lowV;
            Vector3 alongU = u * (highU - lowU);
            Vector3 alongV = v * (highV - lowV);

            AppendQuad(verticesList, corner, alongU, alongV, sign > 0);

            Color color = hovered == zone ? EditorAxisColors.Hover : face;

            for (var vertex = 0; vertex < 6; vertex++)
            {
                normalsList.Add(normal);
                colorsList.Add(color);
            }
        }
    }

    /// <summary>
    /// Two triangles for one cell. <c>u × v</c> points along the positive axis, so
    /// the corner walk reads counter-clockwise from that side and clockwise from the
    /// other — and Godot's front faces are the clockwise ones, so the positive face
    /// is the one that has to be emitted in reverse.
    /// </summary>
    private static void AppendQuad(
        List<Vector3> verticesList,
        Vector3 corner,
        Vector3 alongU,
        Vector3 alongV,
        bool reversed)
    {
        Vector3 first = corner;
        Vector3 second = corner + alongU;
        Vector3 third = corner + alongU + alongV;
        Vector3 fourth = corner + alongV;

        if (reversed)
        {
            verticesList.AddRange(new[] { first, fourth, third, first, third, second });

            return;
        }

        verticesList.AddRange(new[] { first, second, third, first, third, fourth });
    }

    /// <summary>
    /// Where one third of a face begins and ends. Contiguous — a cell ends exactly
    /// where the next begins, and the outermost reach the cube's own edge — because
    /// the grid between them is drawn by the material rather than left out of the
    /// geometry.
    /// </summary>
    private static (float Low, float High) GetCellSpan(int cell)
    {
        float low = -ViewCubeGeometry.HalfExtent + cell * CellSize;

        return (low, low + CellSize);
    }

    private static Color GetFaceColor(int axis, int sign) =>
        EditorAxisColors.Get(axis).Lerp(Colors.White, sign > 0 ? PositiveFaceTint : NegativeFaceTint);

    private static Vector3 GetUnit(int axis) => axis switch
    {
        0 => new Vector3(1f, 0f, 0f),
        1 => new Vector3(0f, 1f, 0f),
        _ => new Vector3(0f, 0f, 1f)
    };
}
