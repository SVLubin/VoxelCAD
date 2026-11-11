namespace VoxelCAD.CommonTypes;

/// <summary>
/// Which way round the editor's chrome is drawn: light text on dark surfaces, or
/// the other way about.
/// </summary>
/// <remarks>
/// <see cref="Dark"/> is first so that a missing key and a value outside the enum
/// both land on what the editor ships with — the rule
/// <see cref="ViewCubeCorner"/> and <see cref="AnimationSpeed"/> already keep.
/// <para>
/// It is a pair rather than a list of presets because the one thing anybody
/// wants to choose beyond the two — the accent — is a colour of its own, and a
/// colour is better asked for as a colour than as the name of a theme that
/// happens to carry it.
/// </para>
/// </remarks>
public enum EditorTheme
{
    Dark,
    Light
}
