using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// Builds <c>.vox</c> bytes by hand, so a reader test states the file it is about
/// rather than depending on one somebody has to ship.
/// </summary>
internal sealed class VoxBuilder
{
    private readonly List<byte[]> _chunksList = new();

    public static VoxBuilder File() => new();

    public VoxBuilder Size(int x, int y, int z) =>
        Chunk("SIZE", Content(writer =>
        {
            writer.Write(x);
            writer.Write(y);
            writer.Write(z);
        }));

    /// <summary>Voxels as the file states them: cell and colour index.</summary>
    public VoxBuilder Voxels(params (int X, int Y, int Z, byte ColorIndex)[] cells) =>
        Chunk("XYZI", Content(writer =>
        {
            writer.Write(cells.Length);

            foreach ((int x, int y, int z, byte colorIndex) in cells)
            {
                writer.Write((byte)x);
                writer.Write((byte)y);
                writer.Write((byte)z);
                writer.Write(colorIndex);
            }
        }));

    /// <summary>
    /// A palette written the way the format asks: the entry for colour index
    /// <c>c</c> goes at <c>RGBA[c - 1]</c>.
    /// </summary>
    public VoxBuilder Palette(params (int ColorIndex, byte R, byte G, byte B, byte A)[] entries)
    {
        var palette = new byte[256 * 4];

        foreach ((int colorIndex, byte r, byte g, byte b, byte a) in entries)
        {
            var offset = (colorIndex - 1) * 4;

            palette[offset] = r;
            palette[offset + 1] = g;
            palette[offset + 2] = b;
            palette[offset + 3] = a;
        }

        return Chunk("RGBA", palette);
    }

    public VoxBuilder Material(int colorIndex, params (string Key, string Value)[] properties) =>
        Chunk("MATL", Content(writer =>
        {
            writer.Write(colorIndex);
            WriteDictionary(writer, properties);
        }));

    public VoxBuilder Transform(
        int id,
        int child,
        Vector3I translation,
        byte rotation = VoxRotationTests.IdentityByte,
        string? name = null,
        int frames = 1) =>
        Chunk("nTRN", Content(writer =>
        {
            writer.Write(id);
            WriteDictionary(writer, name == null
                ? Array.Empty<(string, string)>()
                : new[] { ("_name", name) });
            writer.Write(child);
            writer.Write(-1);
            writer.Write(0);
            writer.Write(frames);

            for (var frame = 0; frame < frames; frame++)
            {
                WriteDictionary(writer, new[]
                {
                    ("_t", $"{translation.X} {translation.Y} {translation.Z}"),
                    ("_r", rotation.ToString())
                });
            }
        }));

    public VoxBuilder Group(int id, params int[] children) =>
        Chunk("nGRP", Content(writer =>
        {
            writer.Write(id);
            WriteDictionary(writer, Array.Empty<(string, string)>());
            writer.Write(children.Length);

            foreach (int child in children)
                writer.Write(child);
        }));

    public VoxBuilder Shape(int id, params int[] models) =>
        Chunk("nSHP", Content(writer =>
        {
            writer.Write(id);
            WriteDictionary(writer, Array.Empty<(string, string)>());
            writer.Write(models.Length);

            foreach (int model in models)
            {
                writer.Write(model);
                WriteDictionary(writer, Array.Empty<(string, string)>());
            }
        }));

    /// <summary>A chunk this reader has no case for, to check it is stepped over.</summary>
    public VoxBuilder Unknown(string id, int contentBytes) =>
        Chunk(id, new byte[contentBytes]);

    public VoxBuilder Chunk(string id, byte[] content)
    {
        _chunksList.Add(Content(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes(id));
            writer.Write(content.Length);
            writer.Write(0);
            writer.Write(content);
        }));

        return this;
    }

    /// <summary>
    /// Wraps everything built so far inside <paramref name="depth"/> layers of a
    /// container chunk, so a test can state a file whose chunks nest.
    /// </summary>
    public VoxBuilder Nest(int depth)
    {
        var children = new List<byte>();

        foreach (byte[] chunk in _chunksList)
            children.AddRange(chunk);

        _chunksList.Clear();

        byte[] inner = children.ToArray();

        for (var i = 0; i < depth; i++)
        {
            inner = Content(writer =>
            {
                writer.Write(Encoding.ASCII.GetBytes("LAYR"));
                writer.Write(0);
                writer.Write(inner.Length);
                writer.Write(inner);
            });
        }

        _chunksList.Add(inner);

        return this;
    }

    /// <summary>
    /// A chain of <paramref name="depth"/> transform-and-group pairs ending in a
    /// shape, so a test can state a scene graph that nests.
    /// </summary>
    public VoxBuilder NestedGraph(int depth)
    {
        var id = 0;

        for (var i = 0; i < depth; i++)
        {
            Transform(id, id + 1, Vector3I.Zero);
            Group(id + 1, id + 2);
            id += 2;
        }

        Transform(id, id + 1, Vector3I.Zero);
        Shape(id + 1, 0);

        return this;
    }

    public byte[] Build(int version = 150)
    {
        var children = new List<byte>();

        foreach (byte[] chunk in _chunksList)
            children.AddRange(chunk);

        return Content(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("VOX "));
            writer.Write(version);
            writer.Write(Encoding.ASCII.GetBytes("MAIN"));
            writer.Write(0);
            writer.Write(children.Count);
            writer.Write(children.ToArray());
        });
    }

    private static void WriteDictionary(BinaryWriter writer, (string Key, string Value)[] entries)
    {
        writer.Write(entries.Length);

        foreach ((string key, string value) in entries)
        {
            WriteString(writer, key);
            WriteString(writer, value);
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);

        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static byte[] Content(Action<BinaryWriter> write)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        write(writer);
        writer.Flush();

        return buffer.ToArray();
    }
}
