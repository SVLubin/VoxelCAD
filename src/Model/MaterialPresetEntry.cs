using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>What one preset stands for: how it takes light, what colour it is, and where it is listed.</summary>
public readonly record struct MaterialPresetEntry(
    MaterialAppearance Appearance,
    Color Color,
    MaterialPresetGroup Group);
