using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// A material's whole editable state. One snapshot type covers renaming and
/// recolouring, the same way <see cref="TransformSnapshot"/> covers every
/// transform edit — undo restores a state, it does not replay a field change.
/// </summary>
internal readonly record struct MaterialSnapshot(
    string Name,
    Color Color,
    MaterialAppearance Appearance = default)
{
    /// <summary>
    /// The appearance as it should be applied. A <c>default</c> struct is all
    /// zeroes, which is a black mirror rather than the matte surface every
    /// material has always been, so the zero value is read as "not set".
    /// </summary>
    public MaterialAppearance AppearanceOrDefault =>
        Appearance == default ? MaterialAppearance.Default : Appearance;
}
