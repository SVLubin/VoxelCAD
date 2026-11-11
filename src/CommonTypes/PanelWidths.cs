namespace VoxelCAD.CommonTypes;

/// <summary>
/// How wide each side's panel bodies are, in canvas units. One number per side,
/// not per panel: every panel of a column — pinned, grown from its tab, or
/// carried — wears its side's width, which is what keeps one panel one width in
/// every state it has.
/// </summary>
/// <remarks>
/// A display preference on the placements' own terms, stored and clamped beside
/// them. The bounds live here because both sides of the view seam need the same
/// pair: the preferences clamp what a file says, the chrome clamps what a drag
/// asks for, and two copies of the numbers is how the two come to disagree.
/// </remarks>
public readonly record struct PanelWidths(double Left, double Right)
{
    /// <summary>
    /// Below this the transform rows start crowding their own captions; the
    /// shipped 320 was already the tightest thing the chrome carried.
    /// </summary>
    public const double Minimum = 280.0;

    /// <summary>
    /// Wide enough to read any name; past it a column is a second window
    /// rather than a dock.
    /// </summary>
    public const double Maximum = 560.0;

    /// <summary>The shipped body width — what a fresh editor opens at.</summary>
    public const double Default = 320.0;
}
