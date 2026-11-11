using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Adds a scene, and on redo re-attaches the very same instance — so everything
/// it contains, and every history record that names it, stays valid across any
/// number of undo/redo cycles.
/// </summary>
internal sealed class CreateSceneCommand : ICommand
{
    private readonly Project _project;
    private readonly Scene _scene;
    private readonly int _index;

    public CreateSceneCommand(Project project, Scene scene, int index)
    {
        _project = project;
        _scene = scene;
        _index = index;

        Record = HistoryRecordFactory.Create(ActionKind.CreateScene, scene.Id, scene.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _project.AttachScene(_scene, _index);

    public void Undo() => _project.DetachScene(_scene);
}
