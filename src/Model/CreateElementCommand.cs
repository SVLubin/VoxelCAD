using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Creates an element, and on redo re-attaches the very same instance — so its
/// <see cref="HierarchyElement.Id"/> survives any number of undo/redo cycles and
/// anything referring to it stays valid.
/// </summary>
internal sealed class CreateElementCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly Guid _parentId;
    private readonly int _index;

    /// <summary>
    /// <paramref name="kind"/> is what the history says happened. Adding an
    /// instance of a prefab is the same attach with a different sentence — the
    /// element carries the difference, and the record should say so.
    /// </summary>
    public CreateElementCommand(
        Scene scene,
        HierarchyElement element,
        Guid parentId,
        int index,
        ActionKind kind)
    {
        _scene = scene;
        _element = element;
        _parentId = parentId;
        _index = index;

        Record = HistoryRecordFactory.Create(kind, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public Guid ElementId => _element.Id;

    public void Do() => _scene.AttachElement(_element, _parentId, _index);

    public void Undo() => _scene.DetachElement(_element);
}
