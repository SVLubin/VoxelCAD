using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What a camera looks like in the viewport: a small body, and the cone or box
/// of what it sees, as a line list in the camera's own frame.
/// </summary>
/// <remarks>
/// <para>
/// A camera holds no geometry, so without this it is a row in the tree and
/// nothing on screen — and a shot you cannot see is a shot you cannot aim. Lines
/// rather than a solid, on the selection border's own terms: it is a mark
/// <em>about</em> the scene, drawn over whatever the scene happens to be.
/// </para>
/// <para>
/// It is drawn at the picture's own aspect, so what the wireframe encloses is
/// what the render will contain. That is the whole reason the aspect crosses the
/// seam at all: a frustum drawn square would be a promise the picture does not
/// keep.
/// </para>
/// <para>
/// Godot's convention, which the tracer's own arithmetic restates: a camera
/// looks down its <b>-Z</b>, with +X to its right and +Y up.
/// </para>
/// </remarks>
internal static class CameraFrustum
{
    /// <summary>
    /// How much of the reach the body takes up behind the lens. Small enough to
    /// read as a camera rather than as a second box, large enough to be grabbed by
    /// eye at the distances this editor's rooms are seen from.
    /// </summary>
    private const float BodyFraction = 0.18f;

    /// <summary>
    /// How far above the top edge the up mark peaks, as a fraction of the frame's
    /// half height. Without it a frustum rolled on its own axis looks the same
    /// upside down, which is exactly the mistake a hand makes while aiming one.
    /// </summary>
    private const float UpMarkFraction = 0.35f;

    /// <summary>
    /// The lines, in the camera's own frame. <paramref name="reach"/> is how far
    /// down the aim the frame is drawn — a length taken from the room rather than
    /// written down, so a gizmo sized for a small scene is not a speck in a large
    /// one.
    /// </summary>
    public static IReadOnlyList<Vector3> CreateLines(CameraLens lens, float aspect, float reach)
    {
        aspect = Mathf.Max(aspect, 0.01f);
        reach = Mathf.Max(reach, 0.01f);

        float halfHeight = HalfHeight(lens, reach);
        float halfWidth = halfHeight * aspect;
        var pointsList = new List<Vector3>();

        AppendBody(pointsList, reach * BodyFraction);

        // The near end of what is seen: a point for a converging lens, the same
        // rectangle for a parallel one — which is the whole visible difference
        // between the two, and the reason the drawing is worth having.
        float nearHalfHeight = lens.Projection == RenderProjection.Perspective ? 0f : halfHeight;

        AppendFrame(pointsList, nearHalfHeight, nearHalfHeight * aspect, 0f);
        AppendFrame(pointsList, halfHeight, halfWidth, -reach);
        AppendSides(pointsList, nearHalfHeight, nearHalfHeight * aspect, halfHeight, halfWidth, reach);
        AppendUpMark(pointsList, halfHeight, halfWidth, reach);

        return pointsList;
    }

    /// <summary>
    /// Half the frame's height at <paramref name="reach"/>, in cells.
    /// </summary>
    /// <remarks>
    /// A parallel lens with no window of its own is drawn at the height the
    /// converging one would have had, which is exactly what nought means to the
    /// renderer: keep the framing and change only the drawing.
    /// </remarks>
    private static float HalfHeight(CameraLens lens, float reach)
    {
        if (lens.Projection == RenderProjection.Orthographic && lens.OrthographicHeight > 0f)
            return lens.OrthographicHeight * 0.5f;

        return reach * Mathf.Tan(Mathf.DegToRad(lens.FovDegrees) * 0.5f);
    }

    /// <summary>A box behind the lens, so a camera reads as an object and not only as a cone.</summary>
    private static void AppendBody(List<Vector3> pointsList, float side)
    {
        float half = side * 0.5f;

        foreach (Vector3 point in SelectionBox.CreateEdgePoints(
            new Vector3(-half, -half, 0f),
            new Vector3(half, half, side)))
        {
            pointsList.Add(point);
        }
    }

    private static void AppendFrame(
        List<Vector3> pointsList,
        float halfHeight,
        float halfWidth,
        float z)
    {
        if (halfHeight <= 0f)
            return;

        Vector3 topLeft = new(-halfWidth, halfHeight, z);
        Vector3 topRight = new(halfWidth, halfHeight, z);
        Vector3 bottomRight = new(halfWidth, -halfHeight, z);
        Vector3 bottomLeft = new(-halfWidth, -halfHeight, z);

        AppendLine(pointsList, topLeft, topRight);
        AppendLine(pointsList, topRight, bottomRight);
        AppendLine(pointsList, bottomRight, bottomLeft);
        AppendLine(pointsList, bottomLeft, topLeft);
    }

    /// <summary>The four lines down the aim, from the near end of the view to the far one.</summary>
    private static void AppendSides(
        List<Vector3> pointsList,
        float nearHalfHeight,
        float nearHalfWidth,
        float halfHeight,
        float halfWidth,
        float reach)
    {
        for (var corner = 0; corner < 4; corner++)
        {
            float x = (corner & 1) == 0 ? -1f : 1f;
            float y = (corner & 2) == 0 ? -1f : 1f;

            AppendLine(
                pointsList,
                new Vector3(x * nearHalfWidth, y * nearHalfHeight, 0f),
                new Vector3(x * halfWidth, y * halfHeight, -reach));
        }
    }

    /// <summary>
    /// A peak over the top edge of the far frame, so which way up the camera is
    /// can be read at a glance. Two lines: a frustum is symmetric about its own
    /// axis in every other respect, so nothing else in the drawing says it.
    /// </summary>
    private static void AppendUpMark(
        List<Vector3> pointsList,
        float halfHeight,
        float halfWidth,
        float reach)
    {
        if (halfHeight <= 0f)
            return;

        var peak = new Vector3(0f, halfHeight * (1f + UpMarkFraction), -reach);

        AppendLine(pointsList, new Vector3(-halfWidth, halfHeight, -reach), peak);
        AppendLine(pointsList, new Vector3(halfWidth, halfHeight, -reach), peak);
    }

    private static void AppendLine(List<Vector3> pointsList, Vector3 start, Vector3 end)
    {
        pointsList.Add(start);
        pointsList.Add(end);
    }

    /// <summary>The lines as a mesh; empty when there is nothing to draw.</summary>
    public static ArrayMesh Build(CameraLens lens, float aspect, float reach)
    {
        IReadOnlyList<Vector3> points = CreateLines(lens, aspect, reach);
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
}
