using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Turns an instance into ordinary elements and back. The element itself stays put
/// throughout — what changes is its component and what hangs under it.
/// </summary>
/// <remarks>
/// The replacements are prepared before the command runs and held here, so redo
/// re-attaches the same instances rather than a second copy of them. Undo does not
/// restore the old contents: they are derived, so re-deriving them from the source
/// is not merely equivalent to putting them back — it is the only answer that is
/// still true if the source changed meanwhile.
/// </remarks>
internal sealed class UnpackInstanceCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _instance;
    private readonly PrefabInstance _component;
    private readonly IReadOnlyList<HierarchyElement> _childrenList;

    public UnpackInstanceCommand(
        Scene scene,
        HierarchyElement instance,
        PrefabInstance component,
        IReadOnlyList<HierarchyElement> childrenList)
    {
        _scene = scene;
        _instance = instance;
        _component = component;
        _childrenList = childrenList;

        Record = HistoryRecordFactory.Create(
            ActionKind.UnpackInstance, instance.Id, instance.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _scene.ApplyUnpack(_instance, _childrenList);

    public void Undo() => _scene.ApplyRepack(_instance, _component);
}
