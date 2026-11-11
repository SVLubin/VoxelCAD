namespace VoxelCAD.CommonTypes;

/// <summary>
/// What the toolbar's create commands make. Which of them apply depends on what
/// is selected in the project tree, so each is enabled on its own.
/// </summary>
public enum CreateKind
{
    Group,
    VoxelModel,

    /// <summary>
    /// A shot kept in the document. Beside the other elements rather than in a
    /// menu of its own, because a camera <em>is</em> one: it has a transform, it
    /// lives in the tree, and what makes it different is only that it holds a lens
    /// where a model holds cells.
    /// </summary>
    Camera,
    Scene,
    Material,

    /// <summary>
    /// An instance of the selected prefab, placed in the scene being worked on.
    /// The one create that acts on what is selected rather than beside it, which is
    /// also why it is the one that is off unless a library entry is picked out.
    /// </summary>
    PrefabInstance,

    /// <summary>
    /// Somebody else's picture or 3D model, put in the scene to be traced over. The
    /// one create that names a file, which is why it is the one that opens a picker
    /// rather than placing something outright: a reference with no file is nothing.
    /// </summary>
    Reference
}
