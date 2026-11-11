using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using Godot;

namespace VoxelCAD.Model;

/// <summary>
/// Filled cells of one voxel model, each carrying the id of the material that
/// fills it. Read-only, like every element interface — cells are changed through
/// <see cref="IScene.SetVoxels"/> so the edit is undoable.
/// </summary>
/// <remarks>
/// The model is unbounded and does not resolve material ids. A build volume is a
/// scene concern, and materials live in the project registry; a voxel model that
/// knew about either would have to be consulted before every edit.
/// </remarks>
public interface IVoxelModel : IElementComponent
{
    IReadOnlyDictionary<Vector3I, Guid> Voxels { get; }

    /// <summary>
    /// The model's three mirror-plane slots. Symmetry is a brush modifier, not
    /// derived geometry: it decides where an editing tool writes, never what the
    /// model contains — a cell placed under it is an ordinary cell, and moving
    /// or disabling a plane afterwards changes nothing that was built. Changed
    /// through <see cref="IScene.SetSymmetry"/> so the change is undoable.
    /// </summary>
    SymmetryValues Symmetry { get; }

    bool TryGetVoxel(Vector3I cell, out Guid materialId);
}
