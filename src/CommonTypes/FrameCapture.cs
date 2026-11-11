using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// A captured frame: the encoded bytes, the pixel size they decode to after any
/// crop and downscale, and the mapping back to the coordinate space input is
/// aimed in.
/// </summary>
/// <remarks>
/// The mapping is carried rather than left to be assumed, because it is not
/// always one to one: a crop moves the origin, a downscale changes the ratio,
/// and the window's pixels and the canvas units a control's rect is measured in
/// differ by a factor that has been seen at both 1.002 and 2.58 in this project,
/// depending only on how big the window was. An agent converts a pixel it read
/// off the image with <c>input = Origin + pixel / Scale</c>.
/// </remarks>
/// <param name="WindowSize">
/// The whole window in input coordinates — the space <c>ui</c> reports rects in,
/// and the space the injection commands take points in.
/// </param>
/// <param name="Origin">
/// Where the image's top-left corner sits in input coordinates. Zero unless a
/// region was cropped out.
/// </param>
/// <param name="Scale">
/// Image pixels per input-coordinate unit, per axis. Two numbers rather than one
/// because the window's stretch is not exactly uniform — measured 2.579861
/// against 2.580318 — and a single figure would be a rounding nobody asked for.
/// </param>
public readonly record struct FrameCapture(
    byte[] Bytes,
    int Width,
    int Height,
    Vector2I WindowSize,
    Vector2 Origin,
    Vector2 Scale);
