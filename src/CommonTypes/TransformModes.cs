namespace VoxelCAD.CommonTypes;

/// <summary>
/// The <see cref="FieldMode"/> of each transform channel. Kept separate from
/// <see cref="TransformValues"/> because values change constantly and modes
/// almost never, so views can refresh them independently.
/// </summary>
public readonly record struct TransformModes(
    FieldMode Position,
    FieldMode Rotation,
    FieldMode Scale);
