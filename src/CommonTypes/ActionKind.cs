namespace VoxelCAD.CommonTypes;

/// <summary>
/// What an undoable action did. The history panel turns this into readable text
/// on the presenter side, so wording and localisation stay out of the model.
/// </summary>
public enum ActionKind
{
    CreateElement,
    DeleteElement,
    RenameElement,
    ReparentElement,
    MoveElement,
    RotateElement,
    ScaleElement,
    ChangeFieldMode,
    EditVoxels,
    ChangeSymmetry,
    ChangePivot,
    ChangeVisibility,
    CreateMaterial,
    DeleteMaterial,
    ChangeMaterial,
    CreateScene,
    DeleteScene,
    RenameScene,
    CreatePrefab,
    DeletePrefab,
    RenamePrefab,
    CreateInstance,
    UnpackInstance,
    ChangeCamera,
    ChangeActiveCamera,

    /// <summary>
    /// A reference was re-pointed at another file, resized, faded, or its drawing
    /// switched. Importing one is an ordinary <see cref="CreateElement"/>: what
    /// arrives is an element like any other.
    /// </summary>
    ChangeReference,

    /// <summary>
    /// Every cell wearing one material was given another, over an element, a
    /// scene, or the whole project — one record however many elements it reached.
    /// </summary>
    ReplaceMaterial
}
