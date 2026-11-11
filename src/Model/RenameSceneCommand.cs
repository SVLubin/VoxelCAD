using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>Renames a scene, both ways.</summary>
internal sealed class RenameSceneCommand : ICommand
{
    private readonly Project _project;
    private readonly Scene _scene;
    private readonly string _before;
    private readonly string _after;

    public RenameSceneCommand(Project project, Scene scene, string name)
    {
        _project = project;
        _scene = scene;
        _before = scene.Name;
        _after = name;

        Record = HistoryRecordFactory.Create(ActionKind.RenameScene, scene.Id, scene.Name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _project.ApplySceneName(_scene, _after);

    public void Undo() => _project.ApplySceneName(_scene, _before);
}
