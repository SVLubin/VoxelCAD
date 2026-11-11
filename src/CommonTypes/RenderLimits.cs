namespace VoxelCAD.CommonTypes;

/// <summary>
/// The bounds every render dial is held to. Shared so the renderer's own clamp,
/// the card's fields, the command's parsing and what a stored file may say cannot
/// come to disagree about what is legal — the same bargain
/// <see cref="MaterialAppearanceLimits"/> and <see cref="PanelWidths"/> make.
/// </summary>
public static class RenderLimits
{
    public const int MinimumSize = 64;

    public const int MaximumSize = 4096;

    public const int MinimumSamples = 1;

    public const int MaximumSamples = 8192;

    public const int MinimumBounces = 1;

    public const int MaximumBounces = 16;

    /// <summary>Ninety degrees is straight up, where the azimuth stops meaning anything.</summary>
    public const float MaximumElevation = 89f;

    public const float MinimumSunRadius = 0.05f;

    public const float MaximumSunRadius = 30f;

    public const float MaximumIntensity = 100f;

    public const float MinimumExposure = 0.01f;

    public const float MaximumExposure = 100f;

    public const float MaximumAperture = 8f;

    /// <summary>
    /// How far the lens may be focused, in cells. Nothing about the scene decides
    /// it — a lens focused past everything is simply sharp — so what this is for is
    /// that the form, the stored file and the renderer name one number instead of
    /// three: the card said 999, the preferences said 100000 and the renderer said
    /// nothing at all.
    /// </summary>
    public const float MaximumFocusDistance = 100000f;

    /// <summary>
    /// How far round the horizon the sun may be aimed. A full turn either way,
    /// because an angle wraps and refusing -10 in favour of 350 would be arithmetic
    /// the user has to do.
    /// </summary>
    public const float MaximumAzimuth = 360f;

    /// <summary>
    /// How much of the picture a lens scatters. Past a third it stops reading as
    /// glare and starts reading as fog, and nought is a lens that does not
    /// scatter — which no real one is, but which is what a measurement wants.
    /// </summary>
    public const float MaximumGlare = 0.35f;

    /// <summary>
    /// Enough that a lamp spreads into what is beside it, little enough that
    /// nothing else in the picture moves. A render with none reads as arithmetic.
    /// </summary>
    public const float DefaultGlare = 0.06f;

    public const float MaximumOrthographicHeight = 10000f;

    /// <summary>
    /// The render card's own size on screen, in canvas units. Nothing to do with
    /// how big the picture is — the card is how much of it can be seen at once,
    /// and the picture's size is chosen because it is a deliverable.
    /// </summary>
    public const float MinimumCardWidth = 520f;

    public const float MinimumCardHeight = 420f;

    public const float MaximumCardSide = 4000f;

    /// <summary>
    /// Large enough that the picture is the card rather than a strip above the
    /// dials — which is what it was at 720 by 620, with a third of the height
    /// spent on fields nobody moves twice.
    /// </summary>
    public const float DefaultCardWidth = 940f;

    public const float DefaultCardHeight = 700f;
}
