namespace VoxelCAD.CommonTypes;

/// <summary>
/// One mirror-plane slot of a voxel model: whether it is on, and where it sits
/// as <b>twice</b> the plane coordinate in the model's own cells — an even value
/// lies on a cell boundary, an odd one runs through cell centres, and a plane
/// off the half-cell lattice is unrepresentable. A disabled slot keeps its
/// position, so switching a plane back on finds it where it was left.
/// </summary>
public readonly record struct SymmetryPlane(bool Enabled, int PlaneDoubled);
