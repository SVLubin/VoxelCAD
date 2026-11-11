using System;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// One entry of the export palette: a material's identity, its name (used to name
/// materials in formats that carry names), and its colour with alpha as opacity.
/// </summary>
public readonly record struct ExportMaterial(Guid Id, string Name, Color Rgba);
