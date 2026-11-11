namespace VoxelCAD.Export;

/// <summary>
/// One file an export produced. A format may write several — OBJ leaves both a
/// <c>.obj</c> and its <c>.mtl</c> — so an export answers with the set.
/// </summary>
public readonly record struct ExportArtifact(string Path, int Bytes);
