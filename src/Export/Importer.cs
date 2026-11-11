using System;
using System.Collections.Generic;
using System.IO;
using VoxelCAD.Model;

namespace VoxelCAD.Export;

/// <summary>
/// Reads a file and puts what it holds in the document. The mirror of
/// <see cref="Exporter"/>, and as thin: the path picks the reader, the reader
/// produces the IR, and one projection applies it.
/// </summary>
internal sealed class Importer : IImporter
{
    private readonly ImportRegistry _registry = new();

    public IReadOnlyList<ImportFormatInfo> Formats => _registry.Formats;

    public bool CanRead(string path) => !string.IsNullOrEmpty(path) && _registry.Knows(path);

    public IImportPlan Read(string path)
    {
        IImportFormat format = _registry.Resolve(Path.GetExtension(path));

        // The whole file at once. These are voxel files rather than streams, every
        // reader walks back and forth across one, and the largest anyone has is a
        // few megabytes.
        byte[] bytes = File.ReadAllBytes(path);
        var name = Path.GetFileNameWithoutExtension(path);

        return new ImportPlan(format.Read(bytes, name), name, format.Extension);
    }

    public ImportResult Apply(IImportPlan plan, IProject project, IScene scene, Guid parentId) =>
        ImportProjection.Apply(project, scene, (ImportPlan)plan, parentId);

    public ImportResult Import(IProject project, IScene scene, string path, Guid parentId) =>
        Apply(Read(path), project, scene, parentId);
}
