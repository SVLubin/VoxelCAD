namespace VoxelCAD.CommonTypes;

/// <summary>
/// The film and the light: what the render form asks for. The renderer's own
/// settings hold more and know how to clamp themselves; these are the few a person
/// turns, and they cross the view seam — which is why they live here rather than
/// in the render layer, the same reason <see cref="PreferenceValues"/> does.
/// </summary>
/// <remarks>
/// What is deliberately <em>not</em> here is the lens: the projection, the blur
/// and the focus belong to the camera the picture is taken through
/// (<see cref="CameraLens"/>), because they are part of the shot rather than
/// settings of the application. A lens kept here would make the same camera in
/// the same document give a different picture depending on what somebody had left
/// a dial at — including on another machine, where the file is the only thing
/// that travelled.
/// </remarks>
/// <param name="Width">
/// Image width in pixels. Chosen rather than fitted to the card: the picture is a
/// deliverable, and one whose size depended on how big the window happened to be
/// would be a different file on every machine.
/// </param>
/// <param name="Height">Image height in pixels.</param>
/// <param name="Samples">
/// Paths traced per pixel. What time buys: noise falls as its square root, so
/// four times the samples is half the noise.
/// </param>
/// <param name="Exposure">Multiplies the light before the film curve.</param>
/// <param name="SunElevationDegrees">Degrees above the horizon.</param>
/// <param name="SunAzimuthDegrees">Degrees around the horizon.</param>
/// <param name="SunSizeDegrees">
/// The sun's angular radius. The one dial that decides how soft every shadow in
/// the picture is, and it changes nothing else — not even how bright it is.
/// </param>
/// <param name="Glare">
/// How much of the picture the lens scatters, so a bright thing spreads into what
/// is beside it. Nought is a lens that does not, which no real one is.
/// </param>
/// <param name="Backdrop">What a camera ray that reaches nothing shows.</param>
/// <param name="Ground">Whether a floor stands at the room's own to catch a shadow.</param>
public readonly record struct RenderDials(
    int Width,
    int Height,
    int Samples,
    float Exposure,
    float SunElevationDegrees,
    float SunAzimuthDegrees,
    float SunSizeDegrees,
    float Glare,
    RenderBackdrop Backdrop,
    bool Ground)
{
    /// <summary>
    /// What the form offers before anybody has turned anything — the same picture
    /// the renderer's own defaults describe, said in the smaller vocabulary the
    /// card has.
    /// </summary>
    /// <remarks>
    /// Here rather than as initialisers on the stored document, so that the fields
    /// which have to be able to <em>express</em> these values can be held to it by
    /// a test. They could not: the exposure box's lattice ran from its minimum, so
    /// a stored 1.0 came back 1.01 and was written to the file that way.
    /// </remarks>
    public static RenderDials Default => new(
        Width: 1280,
        Height: 720,
        Samples: 256,
        Exposure: 1f,
        SunElevationDegrees: 70f,
        SunAzimuthDegrees: 120f,
        SunSizeDegrees: 2f,
        Glare: RenderLimits.DefaultGlare,
        Backdrop: RenderBackdrop.Environment,
        Ground: true);
}
