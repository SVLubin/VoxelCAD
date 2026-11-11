using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Attaches a pasted scene, already filled with its elements, together with any
/// material they wear that the project had lost. The scene-shaped twin of
/// <see cref="PasteElementCommand"/>, and one record for the same reason.
/// </summary>
internal sealed class PasteSceneCommand : ICommand
{
    private readonly Project _project;
    private readonly Scene _scene;
    private readonly int _index;
    private readonly IReadOnlyList<Material> _materialsList;
    private readonly IReadOnlyList<Prefab> _prefabsList;

    public PasteSceneCommand(
        Project project,
        Scene scene,
        int index,
        IReadOnlyList<Material> materialsList,
        IReadOnlyList<Prefab> prefabsList)
    {
        _project = project;
        _scene = scene;
        _index = index;
        _materialsList = materialsList;
        _prefabsList = prefabsList;

        Record = HistoryRecordFactory.Create(ActionKind.CreateScene, scene.Id, scene.Name);
    }

    public HistoryRecord Record { get; }

    public void Do()
    {
        foreach (Material material in _materialsList)
            _project.AttachMaterial(material, _project.Materials.Count);

        _project.AttachClipPrefabs(_prefabsList);
        _project.AttachScene(_scene, _index);
    }

    public void Undo()
    {
        _project.DetachScene(_scene);
        _project.DetachClipPrefabs(_prefabsList);

        for (var i = _materialsList.Count - 1; i >= 0; i--)
            _project.DetachMaterial(_materialsList[i]);
    }
}
