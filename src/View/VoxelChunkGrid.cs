using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Which chunk a cell belongs to, which cells a chunk holds, and which chunks an
/// edit reaches. Pure arithmetic, out of <see cref="VoxelChunkMeshes"/> for the
/// standing reason: the rebuilding needs the node tree, these three answers do
/// not, and only these three are worth a test.
/// </summary>
internal static class VoxelChunkGrid
{
    /// <summary>
    /// A chunk's side, in cells. A trade rather than a tuning:
    /// <see cref="VoxelMesher"/> walks a box by probing every coordinate in it,
    /// so a chunk costs its own volume whether or not anything is in it — small
    /// chunks make an edit cheap and a full rebuild dearer.
    /// </summary>
    public const int Size = 16;

    /// <summary>
    /// Which chunk holds a cell. <b>Floored, never truncated</b>: C# division
    /// rounds toward zero, so <c>-1 / 16</c> is 0 and the chunks either side of
    /// the origin would be one chunk twice as wide — an edit at -1 would rebuild
    /// the chunk holding 0..15 and leave the cell it actually touched as it was.
    /// </summary>
    public static Vector3I KeyOf(Vector3I cell) => new(
        (int)Math.Floor(cell.X / (double)Size),
        (int)Math.Floor(cell.Y / (double)Size),
        (int)Math.Floor(cell.Z / (double)Size));

    /// <summary>The cells one chunk holds.</summary>
    public static CellRegion BoxOf(Vector3I chunk) => new(
        chunk * Size,
        chunk * Size + new Vector3I(Size - 1, Size - 1, Size - 1));

    /// <summary>
    /// How far one changed cell can change the look of another, in cells. Every
    /// chunk within this of an edit is rebuilt.
    /// </summary>
    /// <remarks>
    /// <b>Two, and the second cell was smoothing's</b>, which has been taken out.
    /// One covers everything the cubic mesher reads: a cell appearing hides the
    /// face of each of its six neighbours, and a face corner's ambient occlusion
    /// is read from the cells crowding the lattice point it sits on, which a cell
    /// shares only with the cells within one of it. Bringing this down to one is
    /// a change of its own, to be measured rather than argued.
    /// </remarks>
    public const int Reach = 2;

    /// <summary>
    /// Every chunk an edit over <paramref name="region"/> could have changed the
    /// look of — the chunks it touches, grown by <see cref="Reach"/> first.
    /// </summary>
    /// <remarks>
    /// This grows the <em>region</em>, not the box each chunk is then meshed
    /// over. Growing that would emit the faces of the overlap twice.
    /// </remarks>
    public static IEnumerable<Vector3I> Touching(CellRegion region)
    {
        var reach = new Vector3I(Reach, Reach, Reach);

        Vector3I from = KeyOf(region.Min - reach);
        Vector3I to = KeyOf(region.Max + reach);

        for (int z = from.Z; z <= to.Z; z++)
        {
            for (int y = from.Y; y <= to.Y; y++)
            {
                for (int x = from.X; x <= to.X; x++)
                    yield return new Vector3I(x, y, z);
            }
        }
    }
}
