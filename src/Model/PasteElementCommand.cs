using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Attaches a pasted subtree, together with any material it wears that the project
/// had lost. Both in one command, because they are one thing the user did — and
/// because a paste that left half of itself behind on undo would be worse than no
/// paste at all.
/// </summary>
/// <remarks>
/// The prepared subtree is held here rather than rebuilt, the same discipline
/// <see cref="DeleteElementCommand"/> keeps: redo re-attaches the very same
/// instances, so ids survive any number of undo/redo cycles and the cells that
/// reference a re-created material go on referencing it.
/// </remarks>
internal sealed class PasteElementCommand : ICommand
{
    private readonly Scene _scene;
    private readonly Project _project;
    private readonly HierarchyElement _element;
    private readonly Guid _parentId;
    private readonly int _index;
    private readonly IReadOnlyList<Material> _materialsList;
    private readonly IReadOnlyList<Prefab> _prefabsList;

    public PasteElementCommand(
        Scene scene,
        Project project,
        HierarchyElement element,
        Guid parentId,
        int index,
        IReadOnlyList<Material> materialsList,
        IReadOnlyList<Prefab> prefabsList)
    {
        _scene = scene;
        _project = project;
        _element = element;
        _parentId = parentId;
        _index = index;
        _materialsList = materialsList;
        _prefabsList = prefabsList;

        Record = HistoryRecordFactory.Create(ActionKind.CreateElement, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    /// <summary>
    /// Materials first: the element's cells name them, and a registry that gained
    /// them a step later would be one frame of views drawn against ids naming
    /// nothing. They go at the end of the list rather than where they stood, since
    /// a material's place in it is not its identity. Prefabs next and for the same
    /// reason one step sharper — an instance attached before its source is in the
    /// library expands into nothing at all.
    /// </summary>
    public void Do()
    {
        foreach (Material material in _materialsList)
            _project.AttachMaterial(material, _project.Materials.Count);

        _project.AttachClipPrefabs(_prefabsList);
        _scene.AttachElement(_element, _parentId, _index);
    }

    public void Undo()
    {
        _scene.DetachElement(_element);
        _project.DetachClipPrefabs(_prefabsList);

        for (var i = _materialsList.Count - 1; i >= 0; i--)
            _project.DetachMaterial(_materialsList[i]);
    }
}
