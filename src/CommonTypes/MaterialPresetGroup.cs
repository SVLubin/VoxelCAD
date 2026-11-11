namespace VoxelCAD.CommonTypes;

/// <summary>
/// How the preset list is divided for reading. Four dozen names in one column is
/// a list nobody scans; the groups are what make it a menu.
/// </summary>
public enum MaterialPresetGroup
{
    Mineral,
    Timber,
    Metal,
    Synthetic,
    Glass
}
