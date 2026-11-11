using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Creates a material, and on redo re-attaches the same instance, so voxels that
/// reference it by id survive undo/redo cycles.
/// </summary>
internal sealed class CreateMaterialCommand : ICommand
{
    private readonly Project _project;
    private readonly Material _material;
    private readonly int _index;

    public CreateMaterialCommand(Project project, Material material, int index)
    {
        _project = project;
        _material = material;
        _index = index;

        Record = HistoryRecordFactory.Create(ActionKind.CreateMaterial, material.Id, material.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _project.AttachMaterial(_material, _index);

    public void Undo() => _project.DetachMaterial(_material);
}
