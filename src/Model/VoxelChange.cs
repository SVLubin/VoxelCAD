using System;
using Godot;

namespace VoxelCAD.Model;

/// <summary>
/// One cell's before and after, the unit undo works in. Voxel edits are recorded
/// as a delta rather than a snapshot of the model: a brush stroke touches a
/// handful of cells in a model that may hold millions.
/// </summary>
internal readonly record struct VoxelChange(Vector3I Cell, Guid? Before, Guid? After);
