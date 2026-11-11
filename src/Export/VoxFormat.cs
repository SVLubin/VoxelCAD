using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// MagicaVoxel <c>.vox</c>, version 150: a RIFF-like <c>VOX </c> file whose
/// <c>MAIN</c> chunk holds a <c>SIZE</c>/<c>XYZI</c> model and a 256-colour
/// <c>RGBA</c> palette. Voxels only — a rotated or scaled element cannot land on an
/// integer grid, so it is refused by name rather than resampled.
/// </summary>
/// <remarks>
/// MagicaVoxel is Z-up; the IR's world cells are Y-up, so each cell is rotated
/// Y-up to Z-up by <see cref="VoxAxis"/> — a proper rotation, so the model stays
/// upright and unmirrored, and the reader inverts the very same answer. The
/// bricks are merged into one model translated to the origin: splitting a scene
/// past MagicaVoxel's 256-per-model limit into several placed models is not built
/// yet, so a larger scene is refused with its size named. Colour index 0 is empty,
/// so a material at palette index <c>p</c> is written as index <c>p + 1</c> and its
/// colour lands at <c>RGBA[p]</c>, which is where the reader looks for index
/// <c>p + 1</c>.
/// </remarks>
internal sealed class VoxFormat : IExportFormat
{
    private const int Version = 150;
    private const int PaletteEntries = 256;
    private const int MaximumModelSize = 256;
    private const int MaximumMaterials = 255;

    public string Extension => ".vox";

    public string Label => "MagicaVoxel";

    public ExportFacet Facet => ExportFacet.Volume;

    public CullingPolicy DefaultCulling => CullingPolicy.Solid;

    public void Write(ExportModel model, IExportSink sink, ExportOptions options)
    {
        ExportVolume volume = model.Volume!;

        if (model.UnbakeableElements.Count > 0)
            throw new InvalidOperationException(
                "MagicaVoxel needs axis-aligned voxels, but these elements are rotated or scaled: "
                + string.Join(", ", model.UnbakeableElements)
                + ". Set them to integer position and Snap90 rotation, or export a mesh format instead.");

        if (volume.Palette.Materials.Count > MaximumMaterials)
            throw new InvalidOperationException(
                $"MagicaVoxel holds at most {MaximumMaterials} colours, but the project has "
                + $"{volume.Palette.Materials.Count} materials.");

        (IReadOnlyList<VoxCell> cells, Vector3I size) = Merge(volume);

        if (size.X > MaximumModelSize || size.Y > MaximumModelSize || size.Z > MaximumModelSize)
            throw new InvalidOperationException(
                $"The scene's voxels span {size.X} by {size.Y} by {size.Z} cells, over MagicaVoxel's "
                + $"{MaximumModelSize} per model. Splitting into several placed models is not built yet.");

        byte[] children = BuildChildren(cells, size, volume.Palette);

        using var writer = new BinaryWriter(sink.Open(sink.BaseName + ".vox"));

        writer.Write(Encoding.ASCII.GetBytes("VOX "));
        writer.Write(Version);
        WriteChunkHeader(writer, "MAIN", contentBytes: 0, childrenBytes: children.Length);
        writer.Write(children);
    }

    /// <summary>
    /// Every filled cell across every brick, rotated Z-up and translated so the
    /// model starts at the origin, with the grid size that results. Overlapping
    /// cells from different elements collapse — the later one wins.
    /// </summary>
    private static (IReadOnlyList<VoxCell> Cells, Vector3I Size) Merge(ExportVolume volume)
    {
        var rawList = new List<(Vector3I Cell, byte Color)>();

        foreach (ExportBrick brick in volume.Bricks)
        {
            for (var z = 0; z < brick.Size.Z; z++)
            {
                for (var y = 0; y < brick.Size.Y; y++)
                {
                    for (var x = 0; x < brick.Size.X; x++)
                    {
                        byte color = brick.PaletteIndices[brick.IndexOf(x, y, z)];

                        if (color == 0)
                            continue;

                        rawList.Add((VoxAxis.ToMagica(brick.Origin + new Vector3I(x, y, z)), color));
                    }
                }
            }
        }

        if (rawList.Count == 0)
            return (Array.Empty<VoxCell>(), Vector3I.One);

        Vector3I min = rawList[0].Cell;
        Vector3I max = rawList[0].Cell;

        foreach ((Vector3I cell, byte _) in rawList)
        {
            min = new Vector3I(Math.Min(min.X, cell.X), Math.Min(min.Y, cell.Y), Math.Min(min.Z, cell.Z));
            max = new Vector3I(Math.Max(max.X, cell.X), Math.Max(max.Y, cell.Y), Math.Max(max.Z, cell.Z));
        }

        var byCellDictionary = new Dictionary<Vector3I, byte>(rawList.Count);

        foreach ((Vector3I cell, byte color) in rawList)
            byCellDictionary[cell - min] = color;

        IReadOnlyList<VoxCell> cellsList = byCellDictionary
            .Select(entry => new VoxCell((byte)entry.Key.X, (byte)entry.Key.Y, (byte)entry.Key.Z, entry.Value))
            .OrderBy(cell => cell.Z)
            .ThenBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToList();

        return (cellsList, max - min + Vector3I.One);
    }

    private static byte[] BuildChildren(IReadOnlyList<VoxCell> cells, Vector3I size, ExportPalette palette)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        WriteChunk(writer, "SIZE", SizeContent(size));
        WriteChunk(writer, "XYZI", VoxelContent(cells));
        WriteChunk(writer, "RGBA", PaletteContent(palette));

        writer.Flush();

        return buffer.ToArray();
    }

    private static byte[] SizeContent(Vector3I size)
    {
        using var content = new MemoryStream();
        using var writer = new BinaryWriter(content);

        writer.Write(size.X);
        writer.Write(size.Y);
        writer.Write(size.Z);
        writer.Flush();

        return content.ToArray();
    }

    private static byte[] VoxelContent(IReadOnlyList<VoxCell> cells)
    {
        using var content = new MemoryStream();
        using var writer = new BinaryWriter(content);

        writer.Write(cells.Count);

        foreach (VoxCell cell in cells)
        {
            writer.Write(cell.X);
            writer.Write(cell.Y);
            writer.Write(cell.Z);
            writer.Write(cell.ColorIndex);
        }

        writer.Flush();

        return content.ToArray();
    }

    private static byte[] PaletteContent(ExportPalette palette)
    {
        var content = new byte[PaletteEntries * 4];

        for (var i = 0; i < palette.Materials.Count && i < PaletteEntries; i++)
        {
            Color colour = palette.Materials[i].Rgba;
            var offset = i * 4;

            content[offset] = ToByte(colour.R);
            content[offset + 1] = ToByte(colour.G);
            content[offset + 2] = ToByte(colour.B);
            content[offset + 3] = ToByte(colour.A);
        }

        return content;
    }

    private static void WriteChunk(BinaryWriter writer, string id, byte[] content)
    {
        WriteChunkHeader(writer, id, content.Length, childrenBytes: 0);
        writer.Write(content);
    }

    private static void WriteChunkHeader(BinaryWriter writer, string id, int contentBytes, int childrenBytes)
    {
        writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write(contentBytes);
        writer.Write(childrenBytes);
    }

    private static byte ToByte(float channel) =>
        (byte)Math.Clamp((int)MathF.Round(channel * 255f, MidpointRounding.AwayFromZero), 0, 255);

    private readonly record struct VoxCell(byte X, byte Y, byte Z, byte ColorIndex);
}
