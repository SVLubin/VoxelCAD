using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What a shipped material is to the atlas: which layer of the arrays its cells
/// read, and how many cells one tile of it spans. Two numbers on a vertex, which
/// is what the custom channel carries.
/// </summary>
/// <remarks>
/// The period is here rather than as a uniform on the shared material because
/// the atlas surface is exactly that — <b>shared</b> by every shipped material in
/// a chunk, so a uniform cannot say anything per material. It was a constant
/// eight while the library could not be edited and every one of them shipped at
/// eight; a project material wearing a pack name at its own scale tiles the
/// viewport at that scale now, which is what the tracer and the preview have
/// always done (<see cref="FinishPeriod.GetCells"/> is the one answer all three
/// ask).
/// <para>
/// What the atlas still cannot say per material is everything else an appearance
/// holds — the colour, the roughness, the metal, the emission, the relief. Those
/// are the pack's own maps for every material in it, and a material that differs
/// from the shipped one in any of them is drawn by the atlas as though it did
/// not. The honest answers are a wider channel or refusing such a material its
/// layer; both are decisions rather than lines, and the ledger records them.
/// </para>
/// </remarks>
internal readonly record struct AtlasPlacement(int Layer, float Period)
{
    /// <summary>
    /// A material with no place in the atlas: it is drawn with a material of its
    /// own instead.
    /// </summary>
    public static AtlasPlacement None => new(-1, 0f);

    /// <summary>
    /// Where a material sits in the atlas and how it tiles there. The period is
    /// the material's own dial, because a pack tile is one unit of the pattern —
    /// so "cells to a unit" and "cells to a tile" are the same number — which is
    /// exactly the product the tracer forms (<c>Scale × UnitsPerTile</c>) and the
    /// number the preview divides by.
    /// </summary>
    /// <remarks>
    /// The dial rather than <see cref="FinishPeriod.GetCells"/>, and the
    /// difference is one case: that answers nought for a relief of nought, which
    /// means "no facture at all" — right for a drawn pattern, which is nothing but
    /// its relief, and wrong for a scan, whose albedo <em>is</em> the material.
    /// Dropping it would put back the blank white cube this ledger already
    /// records. What the atlas does about relief is still one switch for all of
    /// them; see the class remark.
    /// </remarks>
    public static AtlasPlacement For(int layer, MaterialAppearance appearance) =>
        new(layer, appearance.FinishScale);

    public bool HasLayer => Layer >= 0;

    /// <summary>
    /// The period as the shader may divide by it: inside the floor, and a number
    /// at all. A division by nought is not a tiling, it is a NaN across the whole
    /// surface — and so is a division <em>by</em> a NaN, which
    /// <see cref="Mathf.Max"/> would hand straight on, every comparison with one
    /// being false. The model refuses a NaN where every value from a control
    /// arrives (<c>MaterialAppearanceClamp</c>); this is the guard at the last
    /// place before the vertex, because what it protects is a whole surface and
    /// the cost is one comparison per material per rebuild.
    /// </summary>
    public float SafePeriod =>
        float.IsFinite(Period) ? Mathf.Max(Period, MinimumPeriod) : MinimumPeriod;

    /// <summary>Matches the tracer's own floor, so neither can tile what the other cannot.</summary>
    public const float MinimumPeriod = 0.01f;
}
