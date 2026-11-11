using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

internal sealed class RenameElementCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly string _before;
    private readonly string _after;

    public RenameElementCommand(Scene scene, HierarchyElement element, string name)
    {
        _scene = scene;
        _element = element;
        _before = element.Name;
        _after = name;

        // The old name is what the panel should show: "Rename Cube", not the name
        // the element ended up with.
        Record = HistoryRecordFactory.Create(ActionKind.RenameElement, element.Id, _before);
    }

    public HistoryRecord Record { get; }

    public void Do() => _scene.ApplyName(_element, _after);

    public void Undo() => _scene.ApplyName(_element, _before);
}
