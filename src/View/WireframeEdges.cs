using System.Collections.Generic;

namespace VoxelCAD.View;

/// <summary>
/// A triangle mesh's edges, as the index list a line primitive takes.
/// </summary>
/// <remarks>
/// Godot 4 has no per-instance wireframe: the engine's own is a viewport-wide
/// debug draw, which would put every voxel in the scene into lines along with the
/// reference. So the edges are made into geometry of their own — which is also
/// what lets one reference be drawn as lines while its neighbour is not.
/// </remarks>
internal static class WireframeEdges
{
    /// <summary>
    /// Each edge once, however many triangles share it. Without the dedupe an
    /// interior edge is drawn twice, which on a translucent reference reads as a
    /// darker line down the middle of every flat face.
    /// </summary>
    public static int[] From(int[] indices)
    {
        var drawnHashSet = new HashSet<long>();
        var edgesList = new List<int>();

        for (var triangle = 0; triangle + 2 < indices.Length; triangle += 3)
        {
            Add(indices[triangle], indices[triangle + 1]);
            Add(indices[triangle + 1], indices[triangle + 2]);
            Add(indices[triangle + 2], indices[triangle]);
        }

        return edgesList.ToArray();

        void Add(int from, int to)
        {
            if (from == to)
                return;

            // Ordered, so an edge walked one way by one triangle and the other way
            // by its neighbour is the same edge.
            (int least, int most) = from < to ? (from, to) : (to, from);

            if (!drawnHashSet.Add(((long)least << 32) | (uint)most))
                return;

            edgesList.Add(from);
            edgesList.Add(to);
        }
    }
}
