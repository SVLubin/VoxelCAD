using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What every dial on the render card offers. One table, out of the node, so that
/// what the form can say is a thing a test can ask about.
/// </summary>
/// <remarks>
/// <para>
/// The lens is not here, and that is the same rule read from the other side: the
/// projection, the blur and the focus are the camera's, so what holds them to
/// their bounds is <see cref="CameraLimits"/> and the field that shows them is the
/// inspector's. This table is the film and the light.
/// </para>
/// <para>
/// The bounds are the renderer's own (<see cref="RenderLimits"/>) rather than
/// numbers written down beside it — the form must not be able to ask for
/// something it will then be shown not to have got. That was already the comment
/// over these lines and it was not true of three of them: exposure stopped at 8
/// where the renderer takes 100, focus at 999 where the stored file allowed
/// 100000, and the azimuth's full turn was a literal in two layers at once.
/// </para>
/// <para>
/// The steps are chosen against <see cref="RenderDialRange"/>'s rule: the minimum
/// is a whole number of steps so the lattice runs from zero, and a dial holding
/// whole numbers steps by one so that every value the model accepts can be
/// reached. Both of those cost something small and obvious — the size arrows walk
/// a pixel at a time — and what they buy is that 1080 and an exposure of 1 exist.
/// </para>
/// </remarks>
internal static class RenderDialRanges
{
    public static RenderDialRange Width { get; } =
        new(RenderLimits.MinimumSize, RenderLimits.MaximumSize, 1);

    public static RenderDialRange Height { get; } =
        new(RenderLimits.MinimumSize, RenderLimits.MaximumSize, 1);

    public static RenderDialRange Samples { get; } =
        new(RenderLimits.MinimumSamples, RenderLimits.MaximumSamples, 1);

    /// <summary>
    /// A hundredth, which is both fine enough to read as continuous and the one
    /// step the minimum is a whole number of. At the 0.05 it shipped with, the
    /// lattice ran 0.01, 0.06, 0.11 … and an exposure of 1 was unsayable.
    /// </summary>
    public static RenderDialRange Exposure { get; } =
        new(RenderLimits.MinimumExposure, RenderLimits.MaximumExposure, 0.01);

    public static RenderDialRange SunElevation { get; } =
        new(-RenderLimits.MaximumElevation, RenderLimits.MaximumElevation, 1);

    /// <summary>
    /// A degree, not five: the sun is aimed at a direction, and a direction the
    /// model accepts and the form cannot say is the same defect as 1080.
    /// </summary>
    public static RenderDialRange SunAzimuth { get; } =
        new(-RenderLimits.MaximumAzimuth, RenderLimits.MaximumAzimuth, 1);

    public static RenderDialRange SunSize { get; } =
        new(RenderLimits.MinimumSunRadius, RenderLimits.MaximumSunRadius, 0.05);

    public static RenderDialRange Glare { get; } =
        new(0d, RenderLimits.MaximumGlare, 0.01);

    /// <summary>
    /// Every dial with the default it has to be able to express, so a test can
    /// walk the lot rather than the two that were noticed. A range added here
    /// without an entry is a dial nothing holds to anything.
    /// </summary>
    public static IReadOnlyList<(string Name, RenderDialRange Range, double Default)> All { get; } =
        new (string, RenderDialRange, double)[]
        {
            ("width", Width, RenderDials.Default.Width),
            ("height", Height, RenderDials.Default.Height),
            ("samples", Samples, RenderDials.Default.Samples),
            ("exposure", Exposure, RenderDials.Default.Exposure),
            ("sun elevation", SunElevation, RenderDials.Default.SunElevationDegrees),
            ("sun azimuth", SunAzimuth, RenderDials.Default.SunAzimuthDegrees),
            ("sun size", SunSize, RenderDials.Default.SunSizeDegrees),
            ("glare", Glare, RenderDials.Default.Glare)
        };
}
