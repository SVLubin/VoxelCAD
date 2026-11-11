using VoxelCAD.CommonTypes;

namespace VoxelCAD.Presenter;

/// <summary>
/// Turns a history record into something readable. Wording lives here rather than
/// in the model, so translating the UI never means touching the document layer.
/// </summary>
internal static class ActionLabelHelper
{
    public static string Describe(HistoryRecord record) => record.Kind switch
    {
        ActionKind.CreateElement => $"Create {record.TargetName}",
        ActionKind.DeleteElement => $"Delete {record.TargetName}",
        ActionKind.RenameElement => $"Rename {record.TargetName}",
        ActionKind.ReparentElement => $"Reparent {record.TargetName}",
        ActionKind.MoveElement => $"Move {record.TargetName}",
        ActionKind.RotateElement => $"Rotate {record.TargetName}",
        ActionKind.ScaleElement => $"Scale {record.TargetName}",
        ActionKind.ChangeFieldMode => $"Change field mode of {record.TargetName}",
        ActionKind.EditVoxels => $"Edit voxels of {record.TargetName}",
        ActionKind.ChangeSymmetry => $"Change symmetry of {record.TargetName}",
        ActionKind.ChangePivot => $"Change pivot of {record.TargetName}",
        ActionKind.ChangeVisibility => $"Show or hide {record.TargetName}",
        ActionKind.CreateMaterial => $"Create material {record.TargetName}",
        ActionKind.DeleteMaterial => $"Delete material {record.TargetName}",
        ActionKind.ChangeMaterial => $"Change material {record.TargetName}",
        ActionKind.CreateScene => $"Create scene {record.TargetName}",
        ActionKind.DeleteScene => $"Delete scene {record.TargetName}",
        ActionKind.RenameScene => $"Rename scene {record.TargetName}",
        ActionKind.CreatePrefab => $"Make prefab {record.TargetName}",
        ActionKind.DeletePrefab => $"Delete prefab {record.TargetName}",
        ActionKind.RenamePrefab => $"Rename prefab {record.TargetName}",
        ActionKind.CreateInstance => $"Place {record.TargetName}",
        ActionKind.UnpackInstance => $"Unpack {record.TargetName}",
        ActionKind.ChangeCamera => $"Change lens of {record.TargetName}",
        ActionKind.ChangeActiveCamera => $"Render through {record.TargetName}",
        ActionKind.ChangeReference => $"Change reference {record.TargetName}",
        ActionKind.ReplaceMaterial => $"Replace uses of {record.TargetName}",
        _ => record.Kind.ToString()
    };

    /// <summary>
    /// What to call the thing a gesture over several targets acted on. Composed
    /// rather than counted — "3 elements" would read as "Delete material 3
    /// materials" once the kind's own template wrapped it, while a name and a
    /// remainder fits every one of them.
    /// </summary>
    /// <remarks>
    /// Here rather than at the two call sites for the reason the rest of this file
    /// is here: a record's wording is one decision, and the history panel is the
    /// only thing that should ever have to make it.
    /// </remarks>
    public static string DescribeTargets(string activeName, int count) =>
        count > 1 ? $"{activeName} and {count - 1} more" : activeName;
}
