namespace VoxelCAD.CommonTypes;

/// <summary>
/// What kind of thing a selection addresses. The project tree holds rows of
/// several sorts — scenes, elements, materials, and the fixed category rows that
/// group them — so an id alone no longer says what was picked.
/// </summary>
/// <remarks>
/// The category rows and the project row are structure rather than data: they
/// carry no id, and nothing in the model corresponds to them.
/// </remarks>
public enum SelectionKind
{
    /// <summary>Nothing selected. The default, so an unset target reads as empty.</summary>
    None,
    Project,
    ScenesCategory,
    PrefabsCategory,
    MaterialsCategory,
    Scene,
    Element,
    Material,

    /// <summary>
    /// A prefab of the project's library. Activating one opens it for editing,
    /// the way activating a scene makes it the one being worked on.
    /// </summary>
    Prefab
}
