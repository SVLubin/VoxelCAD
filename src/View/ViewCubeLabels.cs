using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Where a letter stands on a face of the view cube, as arithmetic rather than
/// as three lines inside the widget.
/// </summary>
/// <remarks>
/// Pure because the one thing that can go wrong here cannot be seen: at the
/// pose the editor opens at, the top face is thirty degrees off the eye and a
/// letter lying on its side is a smudge either way. The first attempt did lie on
/// its side, and what settled it was a test rather than a screenshot.
/// </remarks>
internal static class ViewCubeLabels
{
    /// <summary>The letter each axis is named by, positive and negative.</summary>
    public static string TextFor(int axis, int sign)
    {
        string name = axis switch
        {
            0 => "X",
            1 => "Y",
            _ => "Z"
        };

        return sign > 0 ? name : $"-{name}";
    }

    /// <summary>
    /// A frame whose +Z points out of the face, +X runs the way the text reads
    /// and +Y is the top of the letters — which is how a <see cref="Label3D"/>
    /// lays its own glyphs out.
    /// </summary>
    /// <remarks>
    /// The up vector is chosen away from the normal rather than fixed: with a
    /// constant <see cref="Vector3.Up"/> the top and bottom faces have no frame
    /// at all, their normal being the up vector itself and the cross product
    /// collapsing to nothing.
    /// <para>
    /// <b>Forward, not back</b>, for those two. With back the cross product puts
    /// the text's own right along world -X, which is a letter written right to
    /// left — a Y lying on its side, which is what shipped first.
    /// </para>
    /// </remarks>
    public static Basis FaceBasis(Vector3 normal)
    {
        Vector3 up = Mathf.Abs(normal.Y) > 0.5f ? Vector3.Forward : Vector3.Up;
        Vector3 right = up.Cross(normal).Normalized();

        return new Basis(right, normal.Cross(right).Normalized(), normal);
    }
}
