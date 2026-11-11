using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Geometry;

/// <summary>
/// The face machinery every voxel mesher shares: the six faces of a cell in one
/// canonical order, and the opacity rule deciding whether a face between two
/// cells is visible at all. Two meshers exist — the viewport's, wound for Godot's
/// clockwise front faces, and the exporter's, wound CCW for the neutral frame —
/// and they must never disagree about <b>which</b> faces a model shows; how a
/// face is wound stays each mesher's own convention, so the corner tables do not
/// live here.
/// </summary>
public static class VoxelFaces
{
    /// <summary>
    /// Six faces, indexed +X, −X, +Y, −Y, +Z, −Z. The order is a contract: both
    /// meshers' corner tables are built against these indices.
    /// </summary>
    public const int FaceCount = 6;

    /// <summary>
    /// The cell on the other side of a face — also the face's outward normal.
    /// </summary>
    public static Vector3I GetNeighborOffset(int face) =>
        face switch
        {
            0 => new Vector3I(1, 0, 0),
            1 => new Vector3I(-1, 0, 0),
            2 => new Vector3I(0, 1, 0),
            3 => new Vector3I(0, -1, 0),
            4 => new Vector3I(0, 0, 1),
            5 => new Vector3I(0, 0, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(face), face, null)
        };

    /// <summary>
    /// The face index an outward normal names, the inverse of
    /// <see cref="GetNeighborOffset"/>. Here rather than copied into each caller
    /// for the reason the offsets are: three of them turn a picked face into an
    /// index, and a fourth order would be a silent disagreement about which face
    /// bit means what.
    /// </summary>
    public static bool TryGetFaceIndex(Vector3I normal, out int face)
    {
        for (face = 0; face < FaceCount; face++)
        {
            if (GetNeighborOffset(face) == normal)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the face towards a neighbour is visible. An open face always is.
    /// Against a filled neighbour, only the <b>denser</b> side shows its face.
    /// </summary>
    /// <remarks>
    /// Equal opacities hide each other, which is the obvious half: two opaque cells
    /// occlude, and two panes of the same glass fused together have no surface
    /// between them.
    /// <para>
    /// Where they differ, the face belongs to the more opaque cell and to it alone.
    /// An opaque cell under glass must still show its surface through that glass or
    /// the solid reads as a hollow shell — that much the first version of this rule
    /// had right — but it kept <b>both</b> faces, and two coincident quads at the
    /// same plane is not a seam, it is a coin toss: which one wins is whatever
    /// order the alpha pass happens to draw them in, and the loser blends over the
    /// winner. Photographed at last on a block where a pane at 0.25 met one at
    /// 0.75, with the further block's face plainly visible inside the nearer one.
    /// Keeping the denser side is what a boundary between two media actually looks
    /// like: you see the denser surface, through whatever is in front of it.
    /// </para>
    /// </remarks>
    public static bool ShouldEmitFace(float opacity, bool neighborPresent, float neighborOpacity) =>
        !neighborPresent || opacity > neighborOpacity;

    /// <summary>
    /// A material's opacity, defaulting to fully opaque when the registry does not
    /// know it — so nothing outside the model is ever occluded by a stray id.
    /// </summary>
    public static float GetOpacity(Guid material, IReadOnlyDictionary<Guid, float> opacityById) =>
        opacityById.TryGetValue(material, out float opacity) ? opacity : 1f;

}
