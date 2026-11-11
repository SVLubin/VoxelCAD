using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Removes an element and its subtree. The detached original is held here rather
/// than copied, so undo restores identity — the same ids, not look-alikes.
/// </summary>
internal sealed class DeleteElementCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly Guid _parentId;
    private readonly int _index;

    public DeleteElementCommand(Scene scene, HierarchyElement element, Guid parentId, int index)
    {
        _scene = scene;
        _element = element;
        _parentId = parentId;
        _index = index;

        Record = HistoryRecordFactory.Create(ActionKind.DeleteElement, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _scene.DetachElement(_element);

    public void Undo() => _scene.AttachElement(_element, _parentId, _index);
}
