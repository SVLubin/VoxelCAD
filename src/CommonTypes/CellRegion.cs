using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// A box of cells, inclusive at both ends. What an edit touched, so a consumer
/// can act on that much of a model instead of on all of it.
/// </summary>
/// <remarks>
/// Data and nothing else, on this layer's own terms — whoever needs the box grown
/// by a cell, or turned into chunk keys, does that where the arithmetic belongs.
/// It crosses the seam because the one thing a view cannot work out for itself is
/// <em>which</em> cells changed: it is handed the model's whole cell dictionary and
/// has no memory of the previous one.
/// </remarks>
public readonly record struct CellRegion(Vector3I Min, Vector3I Max);
