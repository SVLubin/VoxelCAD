namespace VoxelCAD.CommonTypes;

/// <summary>
/// What a synthesized key or mouse button does. <see cref="Tap"/> is the ordinary
/// case; the other two exist because parts of the editor are driven by something
/// being <em>held</em>, which cannot be expressed as a press and a release in the
/// same breath.
/// </summary>
/// <remarks>
/// For keys it is the camera, which turns for as long as an arrow is down. For the
/// mouse it is any gesture with a state of its own halfway through: a transform drag
/// shows a ghost and a readout while a handle is held, and a press and a release in
/// one command leave nothing to look at.
/// </remarks>
public enum HoldAction
{
    Tap,
    Press,
    Release
}
