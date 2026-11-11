namespace VoxelCAD.CommonTypes;

/// <summary>
/// Which of the viewport's optional effects are running. Every one of them is a
/// cost the user may decide not to pay; none of them is what a material *is*.
/// </summary>
/// <remarks>
/// The line these three sit on the right side of: an effect here may be switched
/// off, and the surface still says what it is. Colour, gloss, metal and opacity are
/// therefore not here and never will be — a mirror that stopped being a mirror to
/// save a millisecond would be the editor lying about the document. What can go is
/// the shading around a material: the light it does not receive in a crease, the
/// shadow it casts, and the relief its facture pretends to.
/// </remarks>
public readonly record struct ViewportEffects(
    bool Occlusion,
    bool Shadows,
    bool Factures)
{
    /// <summary>
    /// Everything on — the material as the document describes it, which is what a
    /// preview shows whatever this viewport has been asked to skip.
    /// </summary>
    public static readonly ViewportEffects Full = new(true, true, true);
}
