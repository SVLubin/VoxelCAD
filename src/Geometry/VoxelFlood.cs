using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Geometry;

/// <summary>
/// The connected volume of one material: every filled cell reachable from a
/// seed by stepping across shared faces without leaving cells of the seed's own
/// material. What a fill recolours.
/// </summary>
/// <remarks>
/// Six-connected and material-bounded, both deliberately. Six rather than
/// twenty-six because two cells touching only at an edge or a corner do not read
/// as one body — a checkerboard would flood whole. Bounded by material because a
/// fill is a recolour of what the eye reads as one region, and the region's edge
/// is where the colour changes. Empty cells are never part of it: a fill does
/// not pour into a cavity, which is what <c>fill-box</c> is for.
/// <para>
/// Here rather than in the presenter for the reason <see cref="VoxelFacePatch"/>
/// is: the command surface and the tool must answer "which cells" identically.
/// </para>
/// </remarks>
public static class VoxelFlood
{
    /// <summary>
    /// What a fill writes: the connected volume of the named cell, and one volume
    /// per seed <see cref="CollectOrbitSeeds"/> answers with. One answer for the
    /// tool and for the command, so the two cannot come to recolour different
    /// cells — the reason this lives in Geometry at all.
    /// </summary>
    /// <param name="planeDoubledX">
    /// The mirror plane on X in doubled cells, or null where the slot is off — the
    /// three planes arrive as numbers rather than as the model's own struct because
    /// this layer is a leaf and may not see it, which is
    /// <see cref="VoxelSymmetry.ExpandOrbit"/>'s own arrangement.
    /// </param>
    public static IReadOnlyList<Vector3I> CollectOrbitConnected(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        Vector3I seed,
        int? planeDoubledX,
        int? planeDoubledY,
        int? planeDoubledZ)
    {
        IReadOnlyList<Vector3I> seeds =
            CollectOrbitSeeds(voxels, seed, planeDoubledX, planeDoubledY, planeDoubledZ);

        if (seeds.Count <= 1)
            return CollectConnected(voxels, seed);

        var writtenHashSet = new HashSet<Vector3I>();
        var volumeList = new List<Vector3I>();

        foreach (Vector3I image in seeds)
        {
            foreach (Vector3I cell in CollectConnected(voxels, image))
            {
                if (writtenHashSet.Add(cell))
                    volumeList.Add(cell);
            }
        }

        return volumeList;
    }

    /// <summary>
    /// Where a fill acts: the named cell, and each image of it under the element's
    /// mirror planes that wears the named cell's own material. What the viewport
    /// marks before the click and what <see cref="CollectOrbitConnected"/> floods
    /// from, so the marker and the edit name the same places. Empty when the named
    /// cell is not a filled one.
    /// </summary>
    /// <remarks>
    /// An image landing on empty space seeds nothing, and neither does one landing
    /// on a cell of <em>another</em> material. A fill recolours the region the
    /// click named, and a mirror repeats that on the other side only where the
    /// other side wears the same material; a region of another colour is not the
    /// clicked region's twin, and recolouring it would be an edit nothing on
    /// screen had pointed at.
    /// </remarks>
    public static IReadOnlyList<Vector3I> CollectOrbitSeeds(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        Vector3I seed,
        int? planeDoubledX,
        int? planeDoubledY,
        int? planeDoubledZ)
    {
        var seedsList = new List<Vector3I>();

        if (!voxels.TryGetValue(seed, out Guid material))
            return seedsList;

        IReadOnlyList<Vector3I> orbit =
            VoxelSymmetry.ExpandOrbit(seed, planeDoubledX, planeDoubledY, planeDoubledZ);

        foreach (Vector3I image in orbit)
        {
            if (voxels.TryGetValue(image, out Guid worn) && worn == material)
                seedsList.Add(image);
        }

        return seedsList;
    }

    /// <summary>
    /// The cells of the seed's connected volume, the seed first, the rest in the
    /// order they were reached. Empty when the seed is not a filled cell.
    /// </summary>
    public static IReadOnlyList<Vector3I> CollectConnected(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        Vector3I seed)
    {
        var volumeList = new List<Vector3I>();

        if (!voxels.TryGetValue(seed, out Guid material))
            return volumeList;

        var visitedHashSet = new HashSet<Vector3I> { seed };
        var pendingQueue = new Queue<Vector3I>();

        volumeList.Add(seed);
        pendingQueue.Enqueue(seed);

        while (pendingQueue.Count > 0)
        {
            Vector3I cell = pendingQueue.Dequeue();

            for (var face = 0; face < VoxelFaces.FaceCount; face++)
            {
                Vector3I neighbor = cell + VoxelFaces.GetNeighborOffset(face);

                if (!voxels.TryGetValue(neighbor, out Guid worn) || worn != material)
                    continue;

                if (!visitedHashSet.Add(neighbor))
                    continue;

                volumeList.Add(neighbor);
                pendingQueue.Enqueue(neighbor);
            }
        }

        return volumeList;
    }
}
