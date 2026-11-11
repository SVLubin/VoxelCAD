using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Model;

/// <summary>
/// A prefab a clip's instances refer to, carried with the clip under the id they
/// refer to it by, so that a paste into another document can put the library entry
/// back rather than writing an instance that names nothing.
/// </summary>
/// <remarks>
/// The same bargain <see cref="ClipMaterial"/> strikes, one level up, and for a
/// wider window: an instance copied out of one project and pasted into another
/// would otherwise be an element whose contents cannot be derived at all. The
/// closure travels — a prefab that contains an instance of another brings that one
/// too — because half a library is worse than none.
/// </remarks>
internal readonly record struct ClipPrefab(
    Guid Id,
    string Name,
    Vector3I Size,
    IReadOnlyList<ElementNode> Elements);
