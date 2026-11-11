using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Geometry;

/// <summary>
/// The face of a voxel model, as a set of cells: the connected run of exposed
/// surface a click belongs to, and the cells an extrusion of it would write.
/// </summary>
/// <remarks>
/// Here rather than in the presenter for the reason
/// <see cref="VoxelSymmetry"/> is: two sides of the view seam ask the same
/// question and must not answer it differently. The extruder writes the cells,
/// and the viewport draws a marker on every one of them before the click lands —
/// a one-cell disagreement between the two would make the preview a lie about
/// what the click does.
/// <para>
/// A patch is deliberately not restricted by material. What the eye reads as one
/// face of a shape is one face of a shape whatever it has been painted, and a
/// stripe of another colour across a wall should not stop the wall extruding.
/// </para>
/// </remarks>
public static class VoxelFacePatch
{
    /// <summary>
    /// How deep one extrude click goes. Here rather than in either caller because
    /// both measure in it: the viewport draws a marker on the cells one click
    /// would write, and the presenter writes them, and a disagreement about the
    /// depth would be the preview lying about the click.
    /// </summary>
    public const int ClickLayers = 1;

    /// <summary>
    /// The connected run of exposed face the seed belongs to: filled cells whose
    /// neighbour in <paramref name="face"/> is empty, reached from the seed by
    /// stepping between cells that share an edge in that face's own plane. Planar
    /// by construction — every cell of it sits at the seed's coordinate along the
    /// face axis.
    /// </summary>
    /// <returns>
    /// The seed first, then the rest in the order they were reached. Empty when the
    /// seed is not a filled cell, or when the face it names is not exposed — there
    /// is no face there to take hold of.
    /// </returns>
    public static IReadOnlyList<Vector3I> CollectPatch(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        Vector3I seed,
        int face)
    {
        Vector3I normal = VoxelFaces.GetNeighborOffset(face);
        var patchList = new List<Vector3I>();

        if (!IsExposed(voxels, seed, normal))
            return patchList;

        var visitedHashSet = new HashSet<Vector3I> { seed };
        var pendingQueue = new Queue<Vector3I>();

        patchList.Add(seed);
        pendingQueue.Enqueue(seed);

        while (pendingQueue.Count > 0)
        {
            Vector3I cell = pendingQueue.Dequeue();

            foreach (Vector3I step in GetPlaneSteps(normal))
            {
                Vector3I neighbor = cell + step;

                if (!visitedHashSet.Add(neighbor))
                    continue;

                if (!IsExposed(voxels, neighbor, normal))
                    continue;

                patchList.Add(neighbor);
                pendingQueue.Enqueue(neighbor);
            }
        }

        return patchList;
    }

    /// <summary>
    /// The cells an extrusion of that face writes, mirror planes included.
    /// A positive <paramref name="layers"/> builds outward — the empty cells in
    /// front of the patch, nearest first — and a negative one carves inward,
    /// starting with the patch's own cells. Zero writes nothing.
    /// </summary>
    /// <remarks>
    /// Mirroring is applied to the resulting cells rather than to the patch,
    /// which is what makes the mirrored extrusion run the mirrored way with no
    /// direction arithmetic of its own: reflecting <c>c + n·k</c> lands on
    /// <c>mirror(c) − n·k</c> along a mirrored axis, exactly where the other
    /// side's face points.
    /// </remarks>
    public static IReadOnlyList<Vector3I> CollectExtrusion(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        Vector3I seed,
        int face,
        int layers,
        int? planeDoubledX,
        int? planeDoubledY,
        int? planeDoubledZ)
    {
        var cellsList = new List<Vector3I>();

        if (layers == 0)
            return cellsList;

        IReadOnlyList<Vector3I> patch = CollectPatch(voxels, seed, face);

        if (patch.Count == 0)
            return cellsList;

        Vector3I normal = VoxelFaces.GetNeighborOffset(face);
        var writtenHashSet = new HashSet<Vector3I>();

        // Layer by layer rather than cell by cell, so the nearest layer is written
        // first whichever way the extrusion runs — which is the order a reader of
        // the list, and a marker drawn from it, expects.
        for (var layer = 0; layer < Mathf.Abs(layers); layer++)
        {
            // Outward starts one cell in front of the surface; inward starts on it.
            int step = layers > 0 ? layer + 1 : -layer;

            foreach (Vector3I cell in patch)
                AppendOrbit(cellsList, writtenHashSet, cell + normal * step,
                    planeDoubledX, planeDoubledY, planeDoubledZ);
        }

        return cellsList;
    }

    private static void AppendOrbit(
        List<Vector3I> cellsList,
        HashSet<Vector3I> writtenHashSet,
        Vector3I cell,
        int? planeDoubledX,
        int? planeDoubledY,
        int? planeDoubledZ)
    {
        IReadOnlyList<Vector3I> orbit =
            VoxelSymmetry.ExpandOrbit(cell, planeDoubledX, planeDoubledY, planeDoubledZ);

        foreach (Vector3I image in orbit)
        {
            if (writtenHashSet.Add(image))
                cellsList.Add(image);
        }
    }

    /// <summary>Whether the cell is filled and shows that face to the air.</summary>
    private static bool IsExposed(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        Vector3I cell,
        Vector3I normal) =>
        voxels.ContainsKey(cell) && !voxels.ContainsKey(cell + normal);

    /// <summary>
    /// The four ways to walk to an edge-sharing cell without leaving the face's
    /// plane — the two axes the normal is not on, each way.
    /// </summary>
    private static IEnumerable<Vector3I> GetPlaneSteps(Vector3I normal)
    {
        for (var axis = 0; axis < 3; axis++)
        {
            if (normal[axis] != 0)
                continue;

            Vector3I step = Vector3I.Zero;
            step[axis] = 1;

            yield return step;
            yield return -step;
        }
    }
}
