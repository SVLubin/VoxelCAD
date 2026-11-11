using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// Where a click landed in the scene, reported rather than interpreted: the
/// viewport says which cell and face were hit and whether the modifier was held,
/// and a presenter decides what that means.
/// </summary>
/// <param name="Cell">
/// The cell that was hit, in the target element's own coordinates. For a click on
/// the scene box this is the cell just inside that face.
/// </param>
/// <param name="Face">
/// Outward unit normal of the face that was hit, so <c>Cell + Face</c> is the
/// empty cell in front of it.
/// </param>
/// <param name="HitVoxel">False when the scene box was hit rather than a voxel.</param>
/// <param name="Alternate">The alternate-action modifier was held.</param>
/// <param name="Layers">
/// How far an extrude gesture pulled, in whole layers along the face — positive
/// outward, negative in. The viewport measures it the way it measures a handle
/// drag's delta, and a plain click arrives as ±1: the degenerate gesture. Zero on
/// every pick that is not an extrusion, which is also what the extrude presenter
/// refuses to act on.
/// </param>
public readonly record struct ScenePick(
    Vector3I Cell,
    Vector3I Face,
    bool HitVoxel,
    bool Alternate,
    int Layers = 0);
