namespace VoxelCAD.CommonTypes;

/// <summary>
/// A voxel model's three mirror-plane slots, one per local axis. Fixed at three
/// on purpose: one plane per axis is what keeps a cell's orbit finite — two
/// parallel mirrors would compose into a translation — so the configuration a
/// list would have to refuse is simply unrepresentable. The default value is
/// the default state: every plane off, standing at zero.
/// </summary>
public readonly record struct SymmetryValues(
    SymmetryPlane X,
    SymmetryPlane Y,
    SymmetryPlane Z);
