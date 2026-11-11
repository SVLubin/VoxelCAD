namespace VoxelCAD.Export;

/// <summary>
/// A single output format. A writer sees only the IR and the options — never the
/// model — which is the whole point of the projection: adding a format touches
/// nothing but its own writer.
/// </summary>
internal interface IExportFormat
{
    /// <summary>The file extension it owns, lower-case and dotted, e.g. <c>.obj</c>.</summary>
    string Extension { get; }

    string Label { get; }

    ExportFacet Facet { get; }

    /// <summary>
    /// The culling the format wants when nothing overrides it. Meaningful only for
    /// <see cref="ExportFacet.Mesh"/>; a volume format returns a value that is
    /// never read.
    /// </summary>
    CullingPolicy DefaultCulling { get; }

    /// <summary>
    /// Writes the format's file (or files) to <paramref name="sink"/>. A format
    /// that cannot represent the model — a voxel format meeting a rotated element —
    /// throws <see cref="System.InvalidOperationException"/> before opening
    /// anything, so a refusal leaves no output behind.
    /// </summary>
    void Write(ExportModel model, IExportSink sink, ExportOptions options);
}
