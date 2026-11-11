using System;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// One entry of a project's change stream. <see cref="TargetId"/> names the
/// material or the scene the change is about; <see cref="Index"/> carries meaning
/// only where something was added, and is <c>-1</c> otherwise.
/// </summary>
public readonly record struct ProjectChange(ProjectChangeKind Kind, Guid TargetId, int Index);
