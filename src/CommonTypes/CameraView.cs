using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// Where the viewport's camera actually stands and how wide it sees. The pose
/// beside it says what the turntable is set to; this says what came of it.
/// </summary>
/// <remarks>
/// The renderer needs the second, not the first: a turntable pose has to be run
/// through the orbit's own arithmetic to become a transform, and a second copy of
/// that arithmetic is a picture that quietly disagrees with the viewport it was
/// framed in. So the view is asked for the answer rather than for the inputs.
/// </remarks>
/// <param name="Transform">The camera's global transform, looking down its own -Z.</param>
/// <param name="FovDegrees">Vertical field of view, in degrees.</param>
public readonly record struct CameraView(Transform3D Transform, float FovDegrees);
