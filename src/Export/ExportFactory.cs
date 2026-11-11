namespace VoxelCAD.Export;

/// <summary>
/// The file-format assembly's only public entry point. It hands out the two roots
/// — an <see cref="IExporter"/> and an <see cref="IImporter"/>, each over its own
/// registry and projection — and nothing else, per the canon's
/// one-factory-per-assembly rule.
/// </summary>
/// <remarks>
/// The name is the exporter's because the exporter came first. What the assembly
/// holds is both directions of the same seam: a model turned into somebody else's
/// file, and somebody else's file turned into a model.
/// </remarks>
public static class ExportFactory
{
    public static IExporter CreateExporter() => new Exporter();

    public static IImporter CreateImporter() => new Importer();
}
