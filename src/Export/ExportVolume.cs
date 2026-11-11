using System.Collections.Generic;

namespace VoxelCAD.Export;

/// <summary>
/// Facet B: an integer voxel grid for the voxel formats (MagicaVoxel). Only
/// axis-aligned elements can be baked exactly; the projection reports the rest on
/// <see cref="ExportModel.UnbakeableElements"/> rather than resampling them.
/// </summary>
public sealed class ExportVolume
{
    public ExportVolume(ExportPalette palette, IReadOnlyList<ExportBrick> bricksList)
    {
        Palette = palette;
        Bricks = bricksList;
    }

    public ExportPalette Palette { get; }

    public IReadOnlyList<ExportBrick> Bricks { get; }
}
