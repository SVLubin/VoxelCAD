using System;
using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// One requested cell change. A null <paramref name="MaterialId"/> erases the cell,
/// so a single list can describe adding, painting and erasing in one operation.
/// </summary>
/// <param name="MaterialId">
/// Identifies a material in the project registry. The voxel model stores the id
/// without resolving it — materials are not the hierarchy's concern.
/// </param>
public readonly record struct VoxelEdit(Vector3I Cell, Guid? MaterialId);
