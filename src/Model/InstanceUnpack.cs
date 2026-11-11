using System.Collections.Generic;

namespace VoxelCAD.Model;

/// <summary>
/// One instance a prefab deletion has to unpack: where it lives, what it points at,
/// and the ordinary elements its contents become.
/// </summary>
/// <remarks>
/// Prepared before the deletion runs, because a command has to be able to replay
/// itself: the contents are derived from a source that will not be in the library
/// by the time redo comes around.
/// </remarks>
internal readonly record struct InstanceUnpack(
    Scene Scene,
    HierarchyElement Instance,
    PrefabInstance Component,
    IReadOnlyList<HierarchyElement> Children);
