using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Removes a scene, holding on to the detached original rather than a copy, so
/// undo restores the scene itself — same ids, same contents, same history records
/// pointing at it.
/// </summary>
internal sealed class DeleteSceneCommand : ICommand
{
    private readonly Project _project;
    private readonly Scene _scene;
    private readonly int _index;

    public DeleteSceneCommand(Project project, Scene scene, int index)
    {
        _project = project;
        _scene = scene;
        _index = index;

        Record = HistoryRecordFactory.Create(ActionKind.DeleteScene, scene.Id, scene.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _project.DetachScene(_scene);

    public void Undo() => _project.AttachScene(_scene, _index);
}
