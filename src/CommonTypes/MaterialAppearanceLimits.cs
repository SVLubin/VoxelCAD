namespace VoxelCAD.CommonTypes;

/// <summary>
/// The bounds every appearance value is held to. Shared so the model's clamp, the
/// inspector's sliders and the command surface's parsing cannot come to disagree
/// about what is legal — the same bargain <c>PanelWidths</c> makes for a panel.
/// </summary>
public static class MaterialAppearanceLimits
{
    /// <summary>
    /// The finest a unit of a facture may be cut, in cells. A tenth rather than the
    /// quarter it was: the dial measures a unit now, not the whole tile, and a
    /// knurl is a fifth of a voxel where the tile it belongs to is a whole one.
    /// </summary>
    public const float MinimumScale = 0.1f;

    /// <summary>The coarsest, also in cells to one unit of the pattern.</summary>
    public const float MaximumScale = 16f;

    /// <summary>Above one the facture is exaggerated past what its height field describes.</summary>
    public const float MaximumRelief = 2f;
}
