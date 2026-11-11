using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>Renames a library entry. The instances of it are untouched — they carry their own names.</summary>
internal sealed class RenamePrefabCommand : ICommand
{
    private readonly Project _project;
    private readonly Prefab _prefab;
    private readonly string _before;
    private readonly string _after;

    public RenamePrefabCommand(Project project, Prefab prefab, string name)
    {
        _project = project;
        _prefab = prefab;
        _before = prefab.Name;
        _after = name;

        Record = HistoryRecordFactory.Create(ActionKind.RenamePrefab, prefab.Id, name);
    }

    public HistoryRecord Record { get; }

    public void Do() => _project.ApplyPrefabName(_prefab, _after);

    public void Undo() => _project.ApplyPrefabName(_prefab, _before);
}
