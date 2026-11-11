using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// One voxel edit, and — exactly once per element — the pivot default that rides
/// with it. The first edit of an empty model gives the pivot the middle of what it
/// filled; that is part of what this edit did, so undoing the edit takes the
/// default back with it rather than leaving a pivot pointing at cells that are no
/// longer there.
/// </summary>
internal sealed class SetVoxelsCommand : ICommand
{
    private readonly Scene _scene;
    private readonly HierarchyElement _element;
    private readonly VoxelModel _model;
    private readonly IReadOnlyList<VoxelChange> _changesList;
    private readonly Vector3I? _pivotDefault;

    public SetVoxelsCommand(
        Scene scene,
        HierarchyElement element,
        VoxelModel model,
        IReadOnlyList<VoxelChange> changesList,
        Vector3I? pivotDefault)
    {
        _scene = scene;
        _element = element;
        _model = model;
        _changesList = changesList;
        _pivotDefault = pivotDefault;

        Record = HistoryRecordFactory.Create(ActionKind.EditVoxels, element.Id, element.Name);
    }

    public HistoryRecord Record { get; }

    public void Do()
    {
        _scene.ApplyVoxels(_element, _model, _changesList, forward: true);

        if (_pivotDefault is { } pivot)
            _scene.ApplyPivot(_element, pivot);
    }

    public void Undo()
    {
        _scene.ApplyVoxels(_element, _model, _changesList, forward: false);

        if (_pivotDefault != null)
            _scene.ApplyPivot(_element, null);
    }
}
