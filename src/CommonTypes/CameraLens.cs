using System;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// What a camera sees through: how the drawing converges, how wide it is, and
/// what the lens does with what is not in focus. Everything about a camera except
/// where it stands, which is the element's own transform.
/// </summary>
/// <remarks>
/// <para>
/// It lives on the camera rather than on the render form because it is part of
/// the shot. A picture taken through the same camera has to be the same picture,
/// and a lens kept in the application's preferences would make it depend on what
/// somebody left a dial at — including on another machine, where the file is the
/// only thing that travelled.
/// </para>
/// <para>
/// Two of the five read nought as "work it out", which is what makes a fresh
/// camera useful before anything has been typed into it: an orthographic window
/// of nought is whatever height keeps the shot the perspective lens was framing,
/// and a focus distance of nought is whatever the camera is pointed at.
/// </para>
/// </remarks>
/// <param name="Projection">Whether the drawing converges.</param>
/// <param name="FovDegrees">
/// Vertical field of view, for a converging lens. Ignored by a parallel one,
/// which has no cone to open — kept rather than cleared, so switching the
/// projection back and forth does not lose it.
/// </param>
/// <param name="OrthographicHeight">
/// How much of the scene a parallel lens takes in, measured up the picture in
/// cells. Nought keeps the framing the perspective lens had, so changing the
/// projection changes the drawing rather than what is in it.
/// </param>
/// <param name="Aperture">
/// The lens's radius in cells. Nought is a pinhole and everything is sharp.
/// </param>
/// <param name="FocusDistance">
/// How far away the lens is focused, in cells, or nought for whatever the camera
/// is pointed at. Only a lens with a radius has anything to do with it.
/// </param>
public readonly record struct CameraLens(
    RenderProjection Projection,
    float FovDegrees,
    float OrthographicHeight,
    float Aperture,
    float FocusDistance)
{
    /// <summary>
    /// A pinhole at the viewport's own angle: what a camera is before anybody has
    /// turned anything, and what makes one placed from the view a camera pointed
    /// at exactly what was on screen.
    /// </summary>
    public static CameraLens Default => new(
        Projection: RenderProjection.Perspective,
        FovDegrees: CameraLimits.DefaultFov,
        OrthographicHeight: 0f,
        Aperture: 0f,
        FocusDistance: 0f);

    /// <summary>
    /// The same lens with every value inside its bounds. Called on the way in from
    /// anywhere a person, an agent or a stored file could have named one — the
    /// model's own bargain with a transform, and for the same reason: a form and a
    /// command must not be able to raise an exception between them.
    /// </summary>
    public CameraLens Clamped() => this with
    {
        FovDegrees = Math.Clamp(FovDegrees, CameraLimits.MinimumFov, CameraLimits.MaximumFov),
        OrthographicHeight = Math.Clamp(
            OrthographicHeight,
            0f,
            CameraLimits.MaximumOrthographicHeight),
        Aperture = Math.Clamp(Aperture, 0f, CameraLimits.MaximumAperture),
        FocusDistance = Math.Clamp(FocusDistance, 0f, CameraLimits.MaximumFocusDistance)
    };
}
