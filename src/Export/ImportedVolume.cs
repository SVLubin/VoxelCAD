using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// The cells of one imported model, normalised into a zero-based box, together
/// with the point the source application turns it about.
/// </summary>
/// <remarks>
/// <b>Normalised</b> is the load-bearing word. A voxel format stores a model in a
/// grid of its own and places that grid in the scene; this document stores cells
/// in the element's own frame and places the element by its transform. So the
/// reader puts the cells at the origin and hands the placement to
/// <see cref="ImportedNode.Placement"/> — which is also what makes an
/// export-then-import round trip land on the cells it started from.
/// </remarks>
internal sealed class ImportedVolume
{
    public ImportedVolume(Vector3I size, IReadOnlyList<ImportedCell> cellsList, Vector3 pivot)
    {
        Size = size;
        Cells = cellsList;
        Pivot = pivot;
    }

    /// <summary>The box the cells occupy, counted from the origin.</summary>
    public Vector3I Size { get; }

    public IReadOnlyList<ImportedCell> Cells { get; }

    /// <summary>
    /// The point the source turns this model about, in the model's own cells and
    /// on the half-cell lattice this document's pivots live on.
    /// </summary>
    /// <remarks>
    /// Carried rather than recomputed because it is the source's decision: MagicaVoxel
    /// turns a model about the centre of its grid, which is not the centre of the
    /// cells that happen to be filled. Importing the grid's centre is what makes
    /// rotating the element here turn it the way it turned there.
    /// </remarks>
    public Vector3 Pivot { get; }
}
