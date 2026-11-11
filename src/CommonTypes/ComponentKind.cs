namespace VoxelCAD.CommonTypes;

/// <summary>
/// What occupies a hierarchy element's single component slot. An element with
/// <see cref="None"/> is a pure grouping node.
/// </summary>
public enum ComponentKind
{
    None,
    VoxelModel,
    PrefabInstance,

    /// <summary>
    /// A shot kept in the document: where the picture is taken from, which way it
    /// points, and the lens it is taken through. It holds no geometry, so nothing
    /// that walks the tree for cells — the mesher, the exporter, the render's own
    /// compile — sees it at all.
    /// </summary>
    Camera,

    /// <summary>
    /// A picture standing in the scene to be traced over. Like a camera it holds
    /// no cells, so the mesher, the exporter and the render's own compile pass
    /// over it; unlike a camera it is drawn, which is the whole of what it is for.
    /// </summary>
    ReferenceImage,

    /// <summary>
    /// Somebody else's 3D model standing in the scene to be built around. Placed
    /// and never edited: the document owns where it stands and how it is drawn,
    /// the file it names belongs to the disk.
    /// </summary>
    ReferenceModel
}
