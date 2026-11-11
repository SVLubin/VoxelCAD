using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Export;

/// <summary>The read file, and the measurements a caller needs before applying it.</summary>
internal sealed class ImportPlan : IImportPlan
{
    public ImportPlan(ImportedFile file, string name, string format)
    {
        File = file;
        Name = name;
        Format = format;
        Extent = ImportExtent.Measure(file.Roots, out Vector3I minimum);
        Landing = new CellTransform(Basis.Identity, -minimum);
    }

    public ImportedFile File { get; }

    public string Name { get; }

    public string Format { get; }

    public Vector3I Extent { get; }

    /// <summary>
    /// The one translation that brings the whole import's corner to the origin.
    /// Measured here rather than while applying, so that what a caller sizes a
    /// room by and what the cells actually land on are the same answer.
    /// </summary>
    public CellTransform Landing { get; }

    public IReadOnlyList<string> Notes => File.Notes;
}
