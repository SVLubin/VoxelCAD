using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// Where a named target turned out to be, or why it is not somewhere to aim.
/// </summary>
/// <remarks>
/// A targeted command refuses rather than guesses: an unknown reference, a
/// control that is invisible, disabled or has no size, or one with something
/// drawn over the point it resolved to, are all a refusal naming the reason —
/// never a click into empty space that reports success and changes nothing.
/// </remarks>
/// <param name="Refusal">Empty when <see cref="Ok"/>; otherwise why not, in words.</param>
/// <param name="Point">Where the gesture would land, in input coordinates.</param>
/// <param name="Control">Which control it resolved to, so a failure can be read off the transcript.</param>
public readonly record struct InputTargetResolution(
    bool Ok,
    string Refusal,
    Vector2 Point,
    UiControl Control);
