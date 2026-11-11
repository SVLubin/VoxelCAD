namespace VoxelCAD.CommonTypes;

/// <summary>
/// Which corner of the 3D viewport the navigation cube sits in. A display
/// preference, so it is one of these four and never a pair of coordinates —
/// a widget the user cannot find is worse than one they cannot place exactly.
/// </summary>
public enum ViewCubeCorner
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft
}
