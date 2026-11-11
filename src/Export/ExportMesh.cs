using System.Collections.Generic;

namespace VoxelCAD.Export;

/// <summary>
/// Facet A: world-space welded geometry for the mesh formats (OBJ, STL, FBX),
/// grouped into one <see cref="ExportSurface"/> per element and material.
/// </summary>
public sealed class ExportMesh
{
    public ExportMesh(ExportPalette palette, IReadOnlyList<ExportSurface> surfacesList)
    {
        Palette = palette;
        Surfaces = surfacesList;
    }

    public ExportPalette Palette { get; }

    public IReadOnlyList<ExportSurface> Surfaces { get; }
}
