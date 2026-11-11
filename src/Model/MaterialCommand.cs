using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>Restores a whole material state in both directions — rename, recolour, or both.</summary>
internal sealed class MaterialCommand : ICommand
{
    private readonly Project _project;
    private readonly Material _material;
    private readonly MaterialSnapshot _before;
    private readonly MaterialSnapshot _after;

    public MaterialCommand(
        Project project,
        Material material,
        MaterialSnapshot before,
        MaterialSnapshot after)
    {
        _project = project;
        _material = material;
        _before = before;
        _after = after;

        Record = HistoryRecordFactory.Create(ActionKind.ChangeMaterial, material.Id, before.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _project.ApplyMaterial(_material, _after);

    public void Undo() => _project.ApplyMaterial(_material, _before);
}
