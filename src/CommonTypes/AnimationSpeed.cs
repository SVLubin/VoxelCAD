namespace VoxelCAD.CommonTypes;

/// <summary>
/// How long the editor's transitions take, as a multiple of the pace it ships
/// with. Three positions rather than a number: what a good multiplier is is a
/// matter of taste, and a free one is a field nobody can fill in well.
/// </summary>
/// <remarks>
/// <see cref="Fast"/> is first so that a missing key and a value outside the
/// enum both land on what the editor ships with — the rule
/// <see cref="ViewCubeCorner"/> keeps, for the same reason.
/// </remarks>
public enum AnimationSpeed
{
    Fast,
    Medium,
    Slow
}
