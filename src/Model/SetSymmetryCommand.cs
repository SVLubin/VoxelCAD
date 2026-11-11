using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Restores a voxel model's whole mirror-plane state in both directions — the
/// same shape as <see cref="TransformCommand"/>: the slots were in state A, now
/// they are in state B, and no cell moves either way.
/// </summary>
internal sealed class SetSymmetryCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly VoxelModel _model;
    private readonly SymmetryValues _before;
    private readonly SymmetryValues _after;

    public SetSymmetryCommand(
        Scene scene,
        HierarchyElement element,
        VoxelModel model,
        SymmetryValues before,
        SymmetryValues after)
    {
        _scene = scene;
        _element = element;
        _model = model;
        _before = before;
        _after = after;

        Record = HistoryRecordFactory.Create(ActionKind.ChangeSymmetry, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _scene.ApplySymmetry(_element, _model, _after);

    public void Undo() => _scene.ApplySymmetry(_element, _model, _before);
}
