namespace VoxelCAD.CommonTypes;

/// <summary>
/// Which way the block in a material preview is turned. Half of what a facture
/// does is only visible at another angle — a brushed streak reads as a mark from
/// the front and as a sheen from the side — so the angle is part of the question
/// being asked, not a detail of how the picture happens to be taken.
/// </summary>
/// <param name="YawDegrees">Around the upright axis; wraps.</param>
/// <param name="PitchDegrees">Above and below the horizon; clamped short of the poles.</param>
public readonly record struct MaterialPreviewTurn(float YawDegrees, float PitchDegrees)
{
    /// <summary>
    /// The three-quarter view a preview opens at, and the one a picture asked for
    /// with no angle comes back at. Three faces at once, which is what makes a
    /// facture's behaviour on differently-facing surfaces legible in one look.
    /// </summary>
    public static MaterialPreviewTurn Default => new(YawDegrees: 35f, PitchDegrees: -25f);
}
