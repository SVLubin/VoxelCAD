using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The quad a reference picture is drawn on: where its corners are, which way it
/// is wound, and which corner of the picture lands on which corner of it.
/// </summary>
/// <remarks>
/// <para>
/// It spans <b>from the element's own zero</b> rather than being centred on it,
/// and that is the load-bearing decision here: quantisation acts on the stored
/// origin, so a corner at local zero is a corner on the cell lattice — which is
/// the whole of what makes one pixel of the picture land on one voxel. Centred, a
/// plane of odd width would put half its pixels across cell boundaries whatever
/// anybody typed into the position field.
/// </para>
/// <para>
/// It lies in the element's own XY and faces +Z, so a fresh reference stands
/// upright like a drawing on a wall and the Snap90 rotation every reference starts
/// with turns it onto the other two walls.
/// </para>
/// </remarks>
internal static class ReferencePlaneGeometry
{
    /// <summary>
    /// Bottom left, bottom right, top right, top left, in the element's cells.
    /// </summary>
    public static Vector3[] Corners(Vector2 sizeInCells) => new[]
    {
        Vector3.Zero,
        new Vector3(sizeInCells.X, 0f, 0f),
        new Vector3(sizeInCells.X, sizeInCells.Y, 0f),
        new Vector3(0f, sizeInCells.Y, 0f)
    };

    /// <summary>
    /// The picture's own corners against those. A texture's V runs down from its
    /// top row while the plane's Y runs up, so the two are opposite — which is
    /// exactly the sort of thing that ships upside down and is noticed by nobody
    /// until a drawing with writing on it turns up.
    /// </summary>
    public static Vector2[] Uvs() => new[]
    {
        new Vector2(0f, 1f),
        new Vector2(1f, 1f),
        new Vector2(1f, 0f),
        new Vector2(0f, 0f)
    };

    /// <summary>
    /// Two triangles, wound <b>clockwise seen from +Z</b> — Godot's front-face
    /// convention, which is the opposite of the OpenGL one and the reason this is
    /// written down rather than typed out from habit.
    /// </summary>
    public static int[] Indices() => new[] { 3, 2, 1, 3, 1, 0 };

    /// <summary>
    /// The one normal every corner wears. Flat, because a plane is.
    /// </summary>
    public static Vector3 Normal => Vector3.Back;
}
