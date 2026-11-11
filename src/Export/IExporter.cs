using System.Collections.Generic;
using VoxelCAD.Model;

namespace VoxelCAD.Export;

/// <summary>
/// The export assembly's root: the projection and the format registry behind one
/// entry point. Building the IR is exposed alongside writing it, so a caller can
/// inspect what an export would contain, and the projection stays testable without
/// a format standing in front of it.
/// </summary>
public interface IExporter
{
    /// <summary>Every registered format, for a menu, a file dialog, or an agent's query.</summary>
    IReadOnlyList<ExportFormatInfo> Formats { get; }

    /// <summary>Projects a scene of a project into the IR for the requested facet.</summary>
    ExportModel BuildModel(IProject project, IScene scene, ExportFacet facet, CullingPolicy culling);

    /// <summary>
    /// Writes a scene to <paramref name="path"/> in the format its extension names,
    /// and answers with every file produced (OBJ leaves a companion <c>.mtl</c>).
    /// Nothing is written to disk until the whole file set is built, so a refusal
    /// leaves no partial output. Throws <see cref="System.ArgumentException"/> for
    /// an unknown extension and <see cref="System.InvalidOperationException"/> when
    /// the format cannot represent the scene (a voxel format meeting a rotated
    /// element).
    /// </summary>
    IReadOnlyList<ExportArtifact> Export(
        IProject project,
        IScene scene,
        string path,
        ExportOptions options);
}
