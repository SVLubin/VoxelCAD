using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Moves an element to another parent while its world transform stays put.
/// </summary>
/// <remarks>
/// The resulting local transform is worked out once, on the first <see cref="Do"/>,
/// and replayed verbatim afterwards. Recomputing it on every redo would let
/// floating-point error accumulate across undo/redo cycles.
/// </remarks>
internal sealed class ReparentElementCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly Guid _parentIdBefore;
    private readonly int _indexBefore;
    private readonly Guid _parentIdAfter;
    private readonly int _indexAfter;
    private readonly TransformSnapshot _transformBefore;
    private readonly Transform3D _world;

    private TransformSnapshot? _transformAfter;

    public ReparentElementCommand(
        Scene scene,
        HierarchyElement element,
        Guid parentId,
        int index)
    {
        _scene = scene;
        _element = element;
        _parentIdBefore = element.Parent.Id;
        _indexBefore = scene.IndexOf(element);
        _parentIdAfter = parentId;
        _indexAfter = index;
        _transformBefore = scene.CaptureTransform(element);
        _world = Scene.CalculateWorldTransform(element);

        Record = HistoryRecordFactory.Create(ActionKind.ReparentElement, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public void Do()
    {
        _scene.MoveElement(_element, _parentIdAfter, _indexAfter);

        if (_transformAfter == null)
        {
            _scene.FitLocalToWorld(_element, _world);
            _transformAfter = _scene.CaptureTransform(_element);
        }
        else
        {
            _scene.RestoreTransform(_element, _transformAfter.Value);
        }
    }

    public void Undo()
    {
        _scene.MoveElement(_element, _parentIdBefore, _indexBefore);
        _scene.RestoreTransform(_element, _transformBefore);
    }
}
