using System.Collections.Generic;

namespace VoxelCAD.Export;

/// <summary>
/// One element an import will create: a name, where it stands, and either the
/// cells it holds or the children under it.
/// </summary>
/// <remarks>
/// The tree is the file's own. A format that groups its models — MagicaVoxel's
/// scene graph does — becomes grouping elements holding voxel models, so the
/// structure the author built survives the crossing rather than being flattened
/// into a row of parts.
/// </remarks>
internal sealed class ImportedNode
{
    public ImportedNode(
        string name,
        CellTransform placement,
        ImportedVolume? volume,
        IReadOnlyList<ImportedNode> childrenList)
    {
        Name = name;
        Placement = placement;
        Volume = volume;
        Children = childrenList;
    }

    public string Name { get; }

    /// <summary>Where this node's lattice sits in its parent's, in cells.</summary>
    public CellTransform Placement { get; }

    /// <summary>The cells, or null for a grouping node.</summary>
    public ImportedVolume? Volume { get; }

    public IReadOnlyList<ImportedNode> Children { get; }
}
