using System;
using VoxelCAD.CommonTypes;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// What the document said about one material, kept so the viewport can build it
/// again without being told a second time.
/// </summary>
/// <remarks>
/// A material's look is derived from its appearance <em>and</em> from which of the
/// viewport's optional effects are running, so switching one of those on rebuilds
/// every material — and rebuilding one means having what it was made from. Reading
/// it back off the <see cref="StandardMaterial3D"/> standing there is the mistake
/// this file exists to prevent: an opaque material keeps its unused alpha in that
/// colour, and a facture that carries colour has already reduced it to a tint.
/// </remarks>
internal readonly record struct VoxelMaterialDescription(
    Guid Id,
    string Name,
    Color Color,
    MaterialAppearance Appearance);
