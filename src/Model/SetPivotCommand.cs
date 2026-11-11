using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Restores an element's pivot in both directions — it stood here, now it stands
/// there, and the geometry never moves either way. The before side is nullable
/// because "never set" is a real state: it is what the first voxel edit's default
/// is allowed to fill in, and undoing past this command has to put it back.
/// </summary>
internal sealed class SetPivotCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly Vector3I? _before;
    private readonly Vector3I _after;

    public SetPivotCommand(
        Scene scene,
        HierarchyElement element,
        Vector3I? before,
        Vector3I after)
    {
        _scene = scene;
        _element = element;
        _before = before;
        _after = after;

        Record = HistoryRecordFactory.Create(ActionKind.ChangePivot, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _scene.ApplyPivot(_element, _after);

    public void Undo() => _scene.ApplyPivot(_element, _before);
}
