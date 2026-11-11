using System;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// What just moved in the undo history: the record that was filed, applied or
/// reverted, and the scene it belongs to.
/// </summary>
/// <param name="Index">
/// Index of that record, or -1 when a navigation ended up moving nothing.
/// </param>
/// <param name="SceneId">
/// Scene the action happened in, or <see cref="Guid.Empty"/> for a project-level
/// action such as a material edit. The editor makes that scene active, so undoing
/// something always shows what it did.
/// </param>
public readonly record struct HistoryChange(int Index, Guid SceneId);
