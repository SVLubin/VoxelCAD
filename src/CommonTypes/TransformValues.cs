using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// An element's local transform as it crosses a layer boundary. Always already
/// quantised to the element's modes — the model owns that rule.
/// </summary>
public readonly record struct TransformValues(
    Vector3 Position,
    Vector3 RotationDegrees,
    Vector3 Scale);
