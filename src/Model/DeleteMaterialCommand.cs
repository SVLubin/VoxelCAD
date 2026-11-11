using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

internal sealed class DeleteMaterialCommand : ICommand
{
    private readonly Project _project;
    private readonly Material _material;
    private readonly int _index;

    public DeleteMaterialCommand(Project project, Material material, int index)
    {
        _project = project;
        _material = material;
        _index = index;

        Record = HistoryRecordFactory.Create(ActionKind.DeleteMaterial, material.Id, material.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _project.DetachMaterial(_material);

    public void Undo() => _project.AttachMaterial(_material, _index);
}
