namespace VoxelCAD.CommonTypes;

/// <summary>
/// The bounds a camera's lens is held to. Shared so the inspector's fields, the
/// command's parsing, what a stored file may say and the renderer's own clamp
/// cannot come to disagree about what is legal — the same bargain
/// <see cref="RenderLimits"/> makes for the film.
/// </summary>
/// <remarks>
/// Three of the five are <see cref="RenderLimits"/>' own, forwarded rather than
/// restated: they were the render card's numbers before the lens moved onto the
/// camera, and a second copy of a bound is how a form comes to refuse what the
/// renderer accepts.
/// </remarks>
public static class CameraLimits
{
    /// <summary>
    /// Narrow enough for a long lens and no narrower: below this the near plane
    /// and the far plane stop being tellable apart in a viewport frustum, and the
    /// picture is a projection nobody reads as a photograph.
    /// </summary>
    public const float MinimumFov = 1f;

    /// <summary>
    /// A fisheye, and the widest angle a pinhole projection stays sane at: the
    /// image plane's tangent runs away at 180.
    /// </summary>
    public const float MaximumFov = 170f;

    /// <summary>
    /// What the viewport's own camera sees, so a camera placed from the view is a
    /// camera pointed at exactly what was on screen.
    /// </summary>
    public const float DefaultFov = 50f;

    public const float MaximumOrthographicHeight = RenderLimits.MaximumOrthographicHeight;

    public const float MaximumAperture = RenderLimits.MaximumAperture;

    public const float MaximumFocusDistance = RenderLimits.MaximumFocusDistance;
}
