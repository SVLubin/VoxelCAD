using System.Collections.Generic;

namespace VoxelCAD.Export;

/// <summary>
/// The projection's output: a scene reduced to one export facet plus the shared
/// palette. Exactly one of <see cref="Mesh"/> and <see cref="Volume"/> is set,
/// matching the facet asked for; the other is null.
/// </summary>
public sealed class ExportModel
{
    public ExportModel(
        string sceneName,
        ExportPalette palette,
        ExportMesh? mesh,
        ExportVolume? volume,
        IReadOnlyList<string> unbakeableElementsList)
    {
        SceneName = sceneName;
        Palette = palette;
        Mesh = mesh;
        Volume = volume;
        UnbakeableElements = unbakeableElementsList;
    }

    public string SceneName { get; }

    public ExportPalette Palette { get; }

    /// <summary>Set when the facet is <see cref="ExportFacet.Mesh"/>, else null.</summary>
    public ExportMesh? Mesh { get; }

    /// <summary>Set when the facet is <see cref="ExportFacet.Volume"/>, else null.</summary>
    public ExportVolume? Volume { get; }

    /// <summary>
    /// Names of voxel elements a volume export could not bake exactly because they
    /// are not axis-aligned. Empty for a mesh export, which bakes any placement.
    /// A voxel writer turns a non-empty list into a refusal that names them.
    /// </summary>
    public IReadOnlyList<string> UnbakeableElements { get; }
}
