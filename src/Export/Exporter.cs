using System.Collections.Generic;
using System.IO;
using VoxelCAD.Model;

namespace VoxelCAD.Export;

/// <inheritdoc cref="IExporter"/>
internal sealed class Exporter : IExporter
{
    private readonly ExportRegistry _registry = new();

    public IReadOnlyList<ExportFormatInfo> Formats => _registry.Formats;

    public ExportModel BuildModel(IProject project, IScene scene, ExportFacet facet, CullingPolicy culling) =>
        ExportProjection.Build(project, scene, facet, culling);

    public IReadOnlyList<ExportArtifact> Export(
        IProject project,
        IScene scene,
        string path,
        ExportOptions options)
    {
        IExportFormat format = _registry.Resolve(Path.GetExtension(path));
        CullingPolicy culling = options.CullingOverride ?? format.DefaultCulling;
        ExportModel model = ExportProjection.Build(project, scene, format.Facet, culling);

        // The writer fills the sink in memory; only once it returns without
        // throwing does anything reach disk, so a refusal leaves no partial output.
        var sink = new BufferingExportSink(Path.GetFileNameWithoutExtension(path));
        format.Write(model, sink, options);

        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var artifactsList = new List<ExportArtifact>();

        foreach ((string fileName, byte[] bytes) in sink.Collect())
        {
            string filePath = Path.Combine(directory, fileName);
            File.WriteAllBytes(filePath, bytes);
            artifactsList.Add(new ExportArtifact(filePath, bytes.Length));
        }

        return artifactsList;
    }
}
