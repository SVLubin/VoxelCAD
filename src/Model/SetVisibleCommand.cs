using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Hides or shows an element, in both directions. Nothing about the geometry
/// changes — a hidden element keeps its cells, its transform and its place in the
/// tree; it is simply not drawn and not exported.
/// </summary>
internal sealed class SetVisibleCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly bool _before;
    private readonly bool _after;

    public SetVisibleCommand(Scene scene, HierarchyElement element, bool visible)
    {
        _scene = scene;
        _element = element;
        _before = element.Visible;
        _after = visible;

        Record = HistoryRecordFactory.Create(
            ActionKind.ChangeVisibility, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _scene.ApplyVisible(_element, _after);

    public void Undo() => _scene.ApplyVisible(_element, _before);
}
