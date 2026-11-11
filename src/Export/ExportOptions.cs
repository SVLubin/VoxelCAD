namespace VoxelCAD.Export;

/// <summary>
/// The knobs an export offers, all with defaults. <see cref="Scale"/> and
/// <see cref="Encoding"/> are output concerns a writer applies; the IR itself is
/// always canonical (1 voxel = 1 unit, Y-up, CCW).
/// </summary>
/// <param name="Scale">
/// Output units per voxel. 1 leaves 1 voxel = 1 unit; STL treats the unit as a
/// millimetre for printing. Applied by the writer, not baked into the IR.
/// </param>
/// <param name="CullingOverride">
/// Forces a culling policy on a mesh export; null uses the format's default
/// (Visual for OBJ/FBX, Solid for STL). Ignored for a volume export.
/// </param>
/// <param name="Encoding">
/// Text or binary where a format offers both; null uses the format's only or
/// natural encoding.
/// </param>
public sealed record ExportOptions(
    float Scale = 1f,
    CullingPolicy? CullingOverride = null,
    ExportEncoding? Encoding = null);
