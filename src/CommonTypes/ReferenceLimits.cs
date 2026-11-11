namespace VoxelCAD.CommonTypes;

/// <summary>
/// What a reference's numbers may be. The same bargain every other settable value
/// in this document strikes: out of range is clamped rather than refused, so a
/// form and a command cannot raise an exception between them.
/// </summary>
public static class ReferenceLimits
{
    /// <summary>
    /// Small enough for a reference to be faded almost away and still be there.
    /// Nought is deliberately reachable: it is "keep it, stop drawing it", which
    /// is a different sentence from hiding the element.
    /// </summary>
    public const float MinimumOpacity = 0f;

    public const float MaximumOpacity = 1f;

    /// <summary>
    /// A tenth of a cell, matching the smallest facture a material may wear. Below
    /// this a plane is a line and the pixels-per-cell readout stops meaning
    /// anything.
    /// </summary>
    public const float MinimumSizeInCells = 0.1f;

    /// <summary>
    /// Four thousand cells a side, which is past any buildable region and well past
    /// what a reference is for. It exists so a damaged file cannot hand the view an
    /// infinity to build a quad from.
    /// </summary>
    public const float MaximumSizeInCells = 4096f;

    /// <summary>What a plane is before anything has told it how big the picture is.</summary>
    public const float DefaultSizeInCells = 16f;
}
