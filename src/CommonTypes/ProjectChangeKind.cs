namespace VoxelCAD.CommonTypes;

/// <summary>What happened to a project, as reported by its change stream.</summary>
/// <remarks>
/// Scenes are project-level the way materials are: they are created, removed and
/// renamed through the project, and what happens inside one is the scene's own
/// change stream.
/// </remarks>
public enum ProjectChangeKind
{
    MaterialAdded,
    MaterialRemoved,
    MaterialChanged,
    SceneAdded,
    SceneRemoved,
    SceneChanged,
    PrefabAdded,
    PrefabRemoved,
    PrefabChanged
}
