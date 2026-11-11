namespace VoxelCAD.CommonTypes;

/// <summary>
/// How a transform channel stores and accepts its value. This is document
/// data, not a display preference: a stored value always conforms to its mode.
/// </summary>
public enum FieldMode
{
    Float,
    Integer,

    /// <summary>Multiples of 90°. Valid for rotation only.</summary>
    Snap90
}
