using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Restores a whole transform snapshot in both directions. One command covers
/// setting a value, changing a field mode, and the mode degradation a reparent
/// can force — all three are "the transform was in state A, now it is in state B".
/// </summary>
internal sealed class TransformCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly TransformSnapshot _before;
    private readonly TransformSnapshot _after;

    public TransformCommand(
        Scene scene,
        HierarchyElement element,
        ActionKind kind,
        TransformSnapshot before,
        TransformSnapshot after)
    {
        _scene = scene;
        _element = element;
        _before = before;
        _after = after;

        Record = HistoryRecordFactory.Create(kind, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _scene.RestoreTransform(_element, _after);

    public void Undo() => _scene.RestoreTransform(_element, _before);
}
