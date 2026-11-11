using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// The turntable camera's whole state as it crosses a layer boundary: the focus
/// point it turns around, the two angles it is turned to, and how far back it
/// sits. It is what an agent's aim commands set and read, and what a handover
/// remembers so the user's view can be put back on release.
/// </summary>
/// <remarks>
/// Angles are in degrees, to match the rest of the command surface — rotation is
/// reported in degrees everywhere else — and because they are the one part a
/// person reads. The view converts to and from its own radians on the way in and
/// out. The values are the same fields the mouse gestures move, so aiming,
/// saving and restoring all speak one language.
/// </remarks>
public readonly record struct CameraPose(
    Vector3 Pivot,
    float YawDegrees,
    float PitchDegrees,
    float Distance);
