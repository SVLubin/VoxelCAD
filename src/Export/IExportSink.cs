using System.IO;

namespace VoxelCAD.Export;

/// <summary>
/// Where a writer puts its output. A single stream would do for STL or
/// MagicaVoxel, but OBJ writes a companion <c>.mtl</c> beside the geometry, so a
/// format produces a small named set of files rather than one stream.
/// </summary>
public interface IExportSink
{
    /// <summary>
    /// The primary file's name without directory or extension, so a writer can
    /// name a companion after it and reference it — <c>mtllib part.mtl</c> beside
    /// <c>part.obj</c>.
    /// </summary>
    string BaseName { get; }

    /// <summary>
    /// Opens a named output for writing. The name is a bare file name (no
    /// directory); the sink decides where it lands.
    /// </summary>
    Stream Open(string fileName);
}
