using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Export;

/// <summary>
/// One material an imported file asks for: what to call it, its colour with alpha
/// as opacity, and how the surface takes light.
/// </summary>
/// <remarks>
/// Already in this document's own vocabulary rather than the file's. Whatever a
/// format says about metal, glass or emission is flattened here, in the reader
/// that understands that format, so nothing downstream has to know which format a
/// material came out of — the same reason the export IR carries colours rather
/// than palettes.
/// </remarks>
internal readonly record struct ImportedMaterial(
    string Name,
    Color Color,
    MaterialAppearance Appearance);
