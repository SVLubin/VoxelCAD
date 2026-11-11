using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Render;

/// <summary>
/// The dials a render is asked for: how big, how long, and what light it stands
/// in. Every value is clamped rather than refused — the model's own bargain with
/// a transform, and for the same reason: a form and a command must not be able
/// to raise an exception between them.
/// </summary>
/// <remarks>
/// <see cref="Aperture"/> is the lens's radius in cells and <see cref="FocusDistance"/>
/// how far away it is focused, nought meaning "on whatever is being looked at" —
/// the middle of what the scene holds, which is what the camera was framed on.
/// <see cref="OrthographicHeight"/> follows the same convention: nought is
/// whatever height keeps the shot the perspective camera was framing, so
/// switching projection changes the drawing and not the framing.
/// <para>
/// The sun's default aim is the viewport's own key light, restated here in the
/// angles a person reads rather than as the Euler triple a
/// <see cref="DirectionalLight3D"/> takes: 70 degrees above the horizon from the
/// same quarter, which is Goxel's default carried into this frame. A render lit
/// from somewhere else than the editor would be a picture of a different scene.
/// </remarks>
public readonly record struct RenderSettings(
    int Width,
    int Height,
    int Samples,
    int Bounces,
    float SunElevationDegrees,
    float SunAzimuthDegrees,
    float SunAngularRadiusDegrees,
    Color SunColor,
    float SunIntensity,
    Color SkyZenith,
    Color SkyHorizon,
    Color SkyGround,
    float SkyIntensity,
    float Exposure,
    float Aperture,
    float FocusDistance,
    float Glare,
    RenderProjection Projection,
    float OrthographicHeight,
    RenderBackdrop Backdrop,
    Color BackdropColor,
    bool GroundEnabled,
    Color GroundColor)
{
    // Forwarded rather than restated: the bounds live where every surface that
    // has to obey them can reach them.
    public const int MinimumSize = RenderLimits.MinimumSize;
    public const int MaximumSize = RenderLimits.MaximumSize;
    public const int MaximumSamples = RenderLimits.MaximumSamples;
    public const int MaximumBounces = RenderLimits.MaximumBounces;

    /// <summary>
    /// A studio rather than a landscape: a neutral cool sky, one soft sun where
    /// the editor's own light stands, and a pale floor to catch a contact shadow.
    /// It is what a part looks like on a bench, which is the picture a CAD editor
    /// is actually asked for.
    /// </summary>
    public static RenderSettings Default => new(
        Width: 1280,
        Height: 720,
        Samples: 256,
        Bounces: 4,
        SunElevationDegrees: 70f,
        SunAzimuthDegrees: 120f,

        // Wider than the real sun's 0.27 degrees, deliberately: a hard shadow
        // edge reads as a bug in a picture of one small object, and this is the
        // one number that turns a render from a diagram into a photograph.
        SunAngularRadiusDegrees: 2f,
        SunColor: new Color(1f, 0.97f, 0.92f),

        // Irradiance, not radiance: what the sun delivers to a surface facing it,
        // so widening it above softens its shadow without brightening the scene.
        SunIntensity: 2f,
        SkyZenith: new Color(0.38f, 0.44f, 0.56f),
        SkyHorizon: new Color(0.62f, 0.64f, 0.68f),
        SkyGround: new Color(0.26f, 0.26f, 0.28f),
        SkyIntensity: 1f,
        Exposure: 1f,

        // A pinhole by default: a CAD picture is usually one where everything is
        // legible, and a blur nobody asked for is a picture of a mistake.
        Aperture: 0f,
        FocusDistance: 0f,
        Glare: RenderLimits.DefaultGlare,

        // The viewport's own projection, so a render is the shot that was framed.
        Projection: RenderProjection.Perspective,
        OrthographicHeight: 0f,
        Backdrop: RenderBackdrop.Environment,

        // A neutral grey rather than white: a part photographed against white has
        // no edge where it is pale, which is most parts.
        BackdropColor: new Color(0.72f, 0.72f, 0.73f),
        GroundEnabled: true,
        GroundColor: new Color(0.62f, 0.62f, 0.63f));

    /// <summary>The unit vector pointing from the scene towards the sun.</summary>
    public Vector3 SunDirection
    {
        get
        {
            float elevation = Mathf.DegToRad(SunElevationDegrees);
            float azimuth = Mathf.DegToRad(SunAzimuthDegrees);
            float horizontal = Mathf.Cos(elevation);

            return new Vector3(
                horizontal * Mathf.Sin(azimuth),
                Mathf.Sin(elevation),
                horizontal * Mathf.Cos(azimuth));
        }
    }

    /// <summary>
    /// The solid angle the sun covers, in steradians. What the shadow's softness
    /// is a function of, and what the intensity has to be divided by.
    /// </summary>
    public float SunSolidAngle =>
        Mathf.Max(Mathf.Tau * (1f - Mathf.Cos(Mathf.DegToRad(SunAngularRadiusDegrees))), 1e-6f);

    /// <summary>
    /// The radiance of the sun's disc, in linear light — the intensity spread over
    /// the cone it subtends.
    /// </summary>
    /// <remarks>
    /// <see cref="SunIntensity"/> is <em>irradiance</em>: what arrives on a surface
    /// facing the sun. The tracer samples the disc and divides by the density it
    /// sampled with, which multiplies the radiance back by this solid angle — so
    /// the two cancel and widening the sun softens its shadow without touching how
    /// bright anything is. Handing the tracer the intensity as radiance instead is
    /// how the first render came out lit by nothing but its own sky.
    /// </remarks>
    public Vector3 SunRadiance
    {
        get
        {
            Color linear = SunColor.SrgbToLinear();

            return new Vector3(linear.R, linear.G, linear.B) * (SunIntensity / SunSolidAngle);
        }
    }

    /// <summary>
    /// The same settings with every value inside its bounds. Called on the way in
    /// from anywhere a person or an agent could have typed one.
    /// </summary>
    public RenderSettings Clamped() => this with
    {
        Width = Math.Clamp(Width, RenderLimits.MinimumSize, RenderLimits.MaximumSize),
        Height = Math.Clamp(Height, RenderLimits.MinimumSize, RenderLimits.MaximumSize),
        Samples = Math.Clamp(Samples, RenderLimits.MinimumSamples, RenderLimits.MaximumSamples),
        Bounces = Math.Clamp(Bounces, RenderLimits.MinimumBounces, RenderLimits.MaximumBounces),
        SunElevationDegrees = Math.Clamp(
            SunElevationDegrees,
            -RenderLimits.MaximumElevation,
            RenderLimits.MaximumElevation),
        SunAngularRadiusDegrees = Math.Clamp(
            SunAngularRadiusDegrees,
            RenderLimits.MinimumSunRadius,
            RenderLimits.MaximumSunRadius),
        SunIntensity = Math.Clamp(SunIntensity, 0f, RenderLimits.MaximumIntensity),
        SkyIntensity = Math.Clamp(SkyIntensity, 0f, RenderLimits.MaximumIntensity),
        Exposure = Math.Clamp(Exposure, RenderLimits.MinimumExposure, RenderLimits.MaximumExposure),
        Aperture = Math.Clamp(Aperture, 0f, RenderLimits.MaximumAperture),
        FocusDistance = Math.Clamp(FocusDistance, 0f, RenderLimits.MaximumFocusDistance),
        Glare = Math.Clamp(Glare, 0f, RenderLimits.MaximumGlare),
        OrthographicHeight = Math.Clamp(
            OrthographicHeight,
            0f,
            RenderLimits.MaximumOrthographicHeight)
    };
}
