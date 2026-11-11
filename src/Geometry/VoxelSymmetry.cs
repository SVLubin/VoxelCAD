using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Geometry;

/// <summary>
/// Reflection of voxel cells across axis-aligned mirror planes on the half-cell
/// lattice. A plane travels as <b>twice</b> its coordinate in cells: an even
/// value lies on a cell boundary, an odd one runs through cell centres, and a
/// plane off the half-cell lattice is unrepresentable rather than merely
/// rejected.
/// </summary>
/// <remarks>
/// Two sides of the view seam ask the same question and must not answer it
/// differently: the brush writes the whole orbit of a click into the model, and
/// the viewport previews that very orbit under the cursor. A one-cell
/// disagreement between them would make the preview lie, which is why the
/// formula lives here rather than being copied into each — the same argument
/// that placed <see cref="GridAlignment"/>.
/// </remarks>
public static class VoxelSymmetry
{
    /// <summary>
    /// The mirror image of one cell coordinate. Cell <c>i</c> occupies
    /// <c>[i, i+1)</c>, so reflecting it across the plane at
    /// <paramref name="planeDoubled"/> / 2 lands on this single expression for
    /// both parities — and an odd plane maps the cell it runs through to itself.
    /// </summary>
    public static int MirrorCoordinate(int coordinate, int planeDoubled) =>
        planeDoubled - 1 - coordinate;

    /// <summary>Reflects one axis of a cell and leaves the other two alone.</summary>
    public static Vector3I MirrorCell(Vector3I cell, int axis, int planeDoubled)
    {
        Vector3I mirrored = cell;
        mirrored[axis] = MirrorCoordinate(cell[axis], planeDoubled);

        return mirrored;
    }

    /// <summary>
    /// Every image of a cell under the enabled planes, the cell itself first, in
    /// a fixed X-then-Y-then-Z order. A null plane is a disabled axis. At most
    /// eight cells, deduplicated — a cell lying on a plane is its own image.
    /// </summary>
    /// <remarks>
    /// One plane per axis is what keeps this finite: two parallel mirrors would
    /// compose into a translation and give a cell infinitely many images, which
    /// is why the signature has exactly three slots rather than a list.
    /// </remarks>
    public static IReadOnlyList<Vector3I> ExpandOrbit(
        Vector3I cell,
        int? planeDoubledX,
        int? planeDoubledY,
        int? planeDoubledZ)
    {
        var orbitList = new List<Vector3I>(capacity: 8) { cell };

        AppendMirrored(orbitList, 0, planeDoubledX);
        AppendMirrored(orbitList, 1, planeDoubledY);
        AppendMirrored(orbitList, 2, planeDoubledZ);

        return orbitList;
    }

    private static void AppendMirrored(List<Vector3I> orbitList, int axis, int? planeDoubled)
    {
        if (planeDoubled == null)
            return;

        // The list grows while it is walked, so the bound is captured first:
        // only the images accumulated by earlier axes are reflected, which is
        // what makes the expansion order fixed rather than emergent.
        int count = orbitList.Count;

        for (var i = 0; i < count; i++)
        {
            Vector3I mirrored = MirrorCell(orbitList[i], axis, planeDoubled.Value);

            if (!orbitList.Contains(mirrored))
                orbitList.Add(mirrored);
        }
    }

    /// <summary>
    /// What "mirror now" writes: the image of every filled cell across the plane,
    /// wherever the image lands on an empty cell. A cell already standing where
    /// an image would go wins and is left alone — the existing model is the
    /// original, and a mirror is not allowed to repaint it — so mirroring an
    /// already symmetric model writes nothing at all. Deliberately one-shot and
    /// literal: symmetry as a brush modifier mirrors nothing retroactively, and
    /// this is the retroactive half, as a plain list of edits.
    /// </summary>
    /// <returns>
    /// One (cell, material) per image landing on an empty cell, in the order the
    /// source cells were enumerated. Two sources whose images coincide cannot
    /// occur — the mirror is a bijection — so no cell is named twice.
    /// </returns>
    public static IReadOnlyList<(Vector3I Cell, Guid Material)> CollectMirrorImages(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        int axis,
        int planeDoubled)
    {
        var imagesList = new List<(Vector3I Cell, Guid Material)>();

        foreach ((Vector3I cell, Guid material) in voxels)
        {
            Vector3I image = MirrorCell(cell, axis, planeDoubled);

            if (image == cell || voxels.ContainsKey(image))
                continue;

            imagesList.Add((image, material));
        }

        return imagesList;
    }
}
