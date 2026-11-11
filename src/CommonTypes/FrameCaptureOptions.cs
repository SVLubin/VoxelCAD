using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// The dials on a frame capture: whether the editor chrome is in it, an optional
/// crop, how far to downscale it, in what format, and — for the lossy formats —
/// at what quality. A full-resolution lossless PNG of the whole viewport is the
/// heaviest a look can be; these are how an agent asks for a lighter, or a
/// closer, one.
/// </summary>
/// <remarks>
/// Scale and quality are already clamped to sane ranges by whoever built this —
/// the same bargain the model makes with a transform, so the capture path never
/// has to refuse. The region is in viewport pixels and clamped to the frame's
/// bounds where it is applied, since only there is the frame's real size known.
/// The crop happens first, then the scale, then the encode; quality is ignored
/// for PNG. A null region is the whole frame.
/// </remarks>
/// <param name="IncludeChrome">
/// Whether the docks, dimmer and plaque are in the picture. False is what the
/// model looks like, true is what the interface looks like — one question each,
/// which is why they are two commands rather than one flag on one.
/// </param>
public readonly record struct FrameCaptureOptions(
    float Scale,
    FrameFormat Format,
    float Quality,
    Rect2I? Region,
    bool IncludeChrome = false);
