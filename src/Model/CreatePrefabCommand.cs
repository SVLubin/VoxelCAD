using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Lifts an element into the prefab library and puts an instance of it back in the
/// element's place — one record, because it is one thing the user did.
/// </summary>
/// <remarks>
/// The library entry comes first and goes last: an instance attached before its
/// source exists would expand into nothing, and a source detached before the
/// instance would leave one standing over a prefab that is no longer there. Both
/// the detached original and the prepared instance are held here rather than
/// rebuilt, the discipline every other command in this layer keeps, so ids survive
/// any number of undo and redo cycles.
/// </remarks>
internal sealed class CreatePrefabCommand : ICommand
{
    private readonly Scene _scene;
    private readonly Project _project;
    private readonly Prefab _prefab;
    private readonly HierarchyElement _source;
    private readonly HierarchyElement _instance;
    private readonly Guid _parentId;
    private readonly int _index;
    private readonly int _prefabIndex;

    public CreatePrefabCommand(
        Scene scene,
        Project project,
        Prefab prefab,
        HierarchyElement source,
        HierarchyElement instance,
        Guid parentId,
        int index)
    {
        _scene = scene;
        _project = project;
        _prefab = prefab;
        _source = source;
        _instance = instance;
        _parentId = parentId;
        _index = index;
        _prefabIndex = project.Prefabs.Count;

        Record = HistoryRecordFactory.Create(ActionKind.CreatePrefab, prefab.Id, prefab.Name);
    }

    public HistoryRecord Record { get; }

    public void Do()
    {
        _project.AttachPrefab(_prefab, _prefabIndex);
        _scene.DetachElement(_source);
        _scene.AttachElement(_instance, _parentId, _index);
    }

    public void Undo()
    {
        _scene.DetachElement(_instance);
        _scene.AttachElement(_source, _parentId, _index);
        _project.DetachPrefab(_prefab);
    }
}
