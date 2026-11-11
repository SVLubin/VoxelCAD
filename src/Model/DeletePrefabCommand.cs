using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Removes a prefab from the library, unpacking every instance of it first —
/// everywhere, including inside other prefabs' contents. One record, because a
/// deletion that left half the document holding references to something gone would
/// not be undoable in any useful sense.
/// </summary>
/// <remarks>
/// Nothing is lost by it: an unpacked instance keeps the geometry it was showing,
/// as ordinary elements. What goes is the link back to the source, which is the
/// thing being deleted.
/// </remarks>
internal sealed class DeletePrefabCommand : ICommand
{
    private readonly Project _project;
    private readonly Prefab _prefab;
    private readonly int _index;
    private readonly IReadOnlyList<InstanceUnpack> _unpacksList;

    public DeletePrefabCommand(
        Project project,
        Prefab prefab,
        int index,
        IReadOnlyList<InstanceUnpack> unpacksList)
    {
        _project = project;
        _prefab = prefab;
        _index = index;
        _unpacksList = unpacksList;

        Record = HistoryRecordFactory.Create(ActionKind.DeletePrefab, prefab.Id, prefab.Name);
    }

    public HistoryRecord Record { get; }

    public void Do()
    {
        foreach (InstanceUnpack unpack in _unpacksList)
            unpack.Scene.ApplyUnpack(unpack.Instance, unpack.Children);

        _project.DetachPrefab(_prefab);
    }

    /// <summary>
    /// The library entry first: re-packing an instance derives its contents from
    /// the source, which has to be back in the library by then.
    /// </summary>
    public void Undo()
    {
        _project.AttachPrefab(_prefab, _index);

        for (var i = _unpacksList.Count - 1; i >= 0; i--)
            _unpacksList[i].Scene.ApplyRepack(_unpacksList[i].Instance, _unpacksList[i].Component);
    }
}
