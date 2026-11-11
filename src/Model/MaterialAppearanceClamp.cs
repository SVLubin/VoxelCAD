using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Holds an appearance inside its bounds on the way in. Clamped rather than
/// refused, which is the bargain the model makes everywhere else a value arrives
/// from a control: a slider must not be able to raise an exception.
/// </summary>
internal static class MaterialAppearanceClamp
{
    public static MaterialAppearance Apply(MaterialAppearance appearance) => new(
        Roughness: Clamp(appearance.Roughness, 0f, 1f, MaterialAppearance.Default.Roughness),
        Metallic: Clamp(appearance.Metallic, 0f, 1f, MaterialAppearance.Default.Metallic),
        Emission: Clamp(appearance.Emission, 0f, 1f, MaterialAppearance.Default.Emission),
        Finish: Enum.IsDefined(typeof(MaterialFinish), appearance.Finish)
            ? appearance.Finish
            : MaterialFinish.Smooth,
        FinishScale: Clamp(
            appearance.FinishScale,
            MaterialAppearanceLimits.MinimumScale,
            MaterialAppearanceLimits.MaximumScale,
            MaterialAppearance.Default.FinishScale),
        FinishRelief: Clamp(
            appearance.FinishRelief,
            0f,
            MaterialAppearanceLimits.MaximumRelief,
            MaterialAppearance.Default.FinishRelief),

        // Nothing to clamp — a flag is already one of its two legal values — but
        // it has to be carried, and that is the point: this rebuilds the struct
        // field by field, so a field added tomorrow and not named here is silently
        // reset to its default on the way through. It cost a whole afternoon once:
        // a material declared see-through came back opaque, and the seam rule then
        // dropped a face the model was supposed to keep.
        Transparent: appearance.Transparent);

    /// <summary>
    /// Inside the bounds, and a number at all. <see cref="Math.Clamp(float, float, float)"/>
    /// is three comparisons, and every comparison with a NaN is false — so it
    /// hands a NaN straight back, and nothing downstream can tell it from a value
    /// somebody chose. A scale of NaN reaches the atlas as the divisor of every
    /// vertex of that material and takes the whole surface with it; the tracer
    /// forms the same product, and its own floor is the same three comparisons.
    /// So it is refused here, where every value from every control already
    /// arrives, and what is not a number takes the field's default rather than
    /// a bound — a NaN has no nearest bound to take.
    /// </summary>
    /// <remarks>
    /// An infinity is not the same case and needs nothing: it compares, so it
    /// clamps to the bound like any other out-of-range value.
    /// </remarks>
    private static float Clamp(float value, float least, float most, float fallback) =>
        float.IsNaN(value) ? fallback : Math.Clamp(value, least, most);
}
