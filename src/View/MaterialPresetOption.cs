using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// One entry of the preset list as the panel needs it: what to select, what to
/// write, and which group it belongs under.
/// </summary>
/// <remarks>
/// The label is composed by the presenter and handed in. The model owns what a
/// preset <i>is</i> and has no business owning what it is <i>called</i> — the
/// same division that keeps history labels out of <c>ActionKind</c>.
/// </remarks>
public readonly record struct MaterialPresetOption(
    MaterialPreset Preset,
    string Label,
    MaterialPresetGroup Group);
