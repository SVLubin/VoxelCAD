using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The maps of the materials the editor ships with, read out of one file that
/// travels with it.
/// </summary>
/// <remarks>
/// One pack rather than fifteen hundred loose images, and the reason is Godot's
/// import pipeline: a <c>.png</c> under <c>res://</c> is imported into a
/// <c>.ctex</c> and the original is <b>not</b> in an exported build, so anything
/// reading the file itself works in the editor and fails in the thing that ships.
/// An extension the importer does not know is copied through untouched.
/// <para>
/// Read once and held: the whole pack is 2.7 MB, which is less than a single
/// uncompressed 1024-square texture, and a material asked for twice must be the
/// same material both times.
/// </para>
/// </remarks>
internal static class StandardMaterialPack
{
    private const string Path = "res://assets/materials/standard.vcpack";
    private const string Magic = "VCMAT1";

    /// <summary>
    /// The maps each material carries, in the order the pack holds them. No
    /// normal map: the relief is the displacement, and a normal derived from it
    /// is what the pack's own normals turn out to be — fitted against the two
    /// sets that ship one, a central difference at 1.007 reproduces them.
    /// </summary>
    private static readonly string[] MapNames = { "color", "rough", "ao", "metal", "height" };

    private static Dictionary<string, byte[][]>? _mapsByName;

    /// <summary>
    /// The slope one full black-to-white step of the height map produces across
    /// one texel, and then multiplied. At the pack's own strength the slopes come
    /// out between 2° and 23°, because it is written for a renderer that carries
    /// the shape in tessellated geometry and uses the normal for fine detail only.
    /// </summary>
    private const float ReliefSlope = 4f;

    /// <summary>How many texels a shipped map is across; every one of them is square.</summary>
    public const int TexelsPerSide = 64;

    /// <summary>
    /// Units of the pattern in one tile of a scan, which is one: the pack's own
    /// texels <em>are</em> the pattern, so "cells to a unit" and "cells to a tile"
    /// are the same number and the scale dial means both. Named because three
    /// consumers form a period out of it — the tracer as a product, the preview
    /// and the atlas as the dial itself — and a number they only agree on by
    /// coincidence is how they came to tile one material two ways.
    /// </summary>
    public const float ScanUnitsPerTile = 1f;

    public static bool Has(string name) => Maps().ContainsKey(name);

    public static IReadOnlyCollection<string> Names => Maps().Keys;

    public static FinishTexturePair Load(string name)
    {
        byte[][] maps = Maps()[name];

        return new FinishTexturePair(
            Texture(maps[0]),
            NormalFromHeight(maps[4]),
            Texture(maps[1]),
            Texture(maps[2]),
            Texture(maps[3]));
    }

    /// <summary>
    /// One material as the two layers the atlas holds: colour with the gloss in
    /// its fourth channel, and two channels of the normal with the occlusion and
    /// the metal beside them.
    /// </summary>
    /// <remarks>
    /// The normal is differentiated from the displacement here as it is
    /// everywhere else, and only two of its channels are kept: it is a unit
    /// vector, so the third is what is left, and recovering it in the shader buys
    /// the room the occlusion and the metal need.
    /// </remarks>
    public static void WriteLayers(string name, out Image surface, out Image relief)
    {
        byte[][] maps = Maps()[name];

        Image? color = Decode(maps[0]);
        Image? rough = Decode(maps[1]);
        Image? ao = Decode(maps[2]);
        Image? metal = Decode(maps[3]);
        Image? height = Decode(maps[4]);

        int size = height?.GetWidth() ?? TexelsPerSide;

        surface = Image.CreateEmpty(size, size, true, Image.Format.Rgba8);
        relief = Image.CreateEmpty(size, size, true, Image.Format.Rgba8);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                Color tint = color?.GetPixel(x, y) ?? Colors.White;

                surface.SetPixel(x, y, new Color(
                    tint.R, tint.G, tint.B, rough?.GetPixel(x, y).R ?? 1f));

                Vector3 slope = height is null
                    ? new Vector3(0f, 0f, 1f)
                    : SlopeAt(height, x, y, size);

                relief.SetPixel(x, y, new Color(
                    slope.X * 0.5f + 0.5f,
                    slope.Y * 0.5f + 0.5f,
                    ao?.GetPixel(x, y).R ?? 1f,
                    metal?.GetPixel(x, y).R ?? 0f));
            }
        }

        surface.GenerateMipmaps();
        relief.GenerateMipmaps();
    }

    /// <summary>
    /// The surface normal at one texel, wrapped because the map tiles: a
    /// difference taken against a clamped edge would put a seam exactly where the
    /// repeat joins.
    /// </summary>
    private static Vector3 SlopeAt(Image height, int x, int y, int size)
    {
        float left = height.GetPixel((x + size - 1) % size, y).R;
        float right = height.GetPixel((x + 1) % size, y).R;
        float up = height.GetPixel(x, (y + size - 1) % size).R;
        float down = height.GetPixel(x, (y + 1) % size).R;

        return new Vector3(
            -(right - left) * 0.5f * ReliefSlope,
            (down - up) * 0.5f * ReliefSlope,
            1f).Normalized();
    }

    /// <summary>
    /// The same set as numbers, for the tracer, which marches on a device of its
    /// own and has no sampler to hand a texture to.
    /// </summary>
    public static FinishHeightField LoadField(string name)
    {
        byte[][] maps = Maps()[name];

        Image? height = Decode(maps[4]);

        if (height is null)
            return FinishHeightField.None;

        int size = height.GetWidth();
        float[] heights = new float[size * size];
        float[] colors = new float[size * size * 3];
        float[] gloss = new float[size * size];
        float[] occlusion = new float[size * size];
        float[] metal = new float[size * size];

        Image? color = Decode(maps[0]);
        Image? rough = Decode(maps[1]);
        Image? ao = Decode(maps[2]);
        Image? metallic = Decode(maps[3]);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                int texel = y * size + x;
                heights[texel] = height.GetPixel(x, y).R;

                // Linear, because that is what the tracer works in and what the
                // viewport's own sampler converts to on its behalf.
                Color linear = (color?.GetPixel(x, y) ?? Colors.White).SrgbToLinear();
                colors[texel * 3 + 0] = linear.R;
                colors[texel * 3 + 1] = linear.G;
                colors[texel * 3 + 2] = linear.B;

                gloss[texel] = rough?.GetPixel(x, y).R ?? 1f;
                occlusion[texel] = ao?.GetPixel(x, y).R ?? 1f;
                metal[texel] = metallic?.GetPixel(x, y).R ?? 0f;
            }
        }

        return new FinishHeightField(
            heights,
            size,

            // Unused where a colour is supplied: the colour replaces the
            // material's rather than dimming it, so there is no floor.
            AlbedoFloor: 1f,
            NormalScale: 1f,

            // Half of ReliefSlope, because the tracer differences across two
            // texels where the derivation here takes a central difference and
            // halves it.
            BumpStrength: ReliefSlope * 0.5f,

            // One tile to a unit, so the scale dial means cells to the tile.
            UnitsPerTile: ScanUnitsPerTile,
            Colors: colors,
            Roughness: gloss,
            Occlusion: occlusion,
            Metal: metal);
    }

    private static Dictionary<string, byte[][]> Maps()
    {
        if (_mapsByName is not null)
            return _mapsByName;

        _mapsByName = new Dictionary<string, byte[][]>();

        byte[] bytes = Godot.FileAccess.GetFileAsBytes(Path);

        if (bytes.Length < Magic.Length)
        {
            GD.PushError($"[{nameof(StandardMaterialPack)}] {Path} is missing or empty.");
            return _mapsByName;
        }

        var reader = new PackReader(bytes);

        if (reader.ReadAscii(Magic.Length) != Magic)
        {
            GD.PushError($"[{nameof(StandardMaterialPack)}] {Path} does not start with {Magic}.");
            return _mapsByName;
        }

        int count = reader.ReadInt();

        for (var i = 0; i < count; i++)
        {
            string name = reader.ReadAscii(reader.ReadInt());
            byte[][] maps = new byte[MapNames.Length][];

            for (var map = 0; map < MapNames.Length; map++)
                maps[map] = reader.ReadBytes(reader.ReadInt());

            _mapsByName[name] = maps;
        }

        return _mapsByName;
    }

    private static Image? Decode(byte[] png)
    {
        var image = new Image();

        return image.LoadPngFromBuffer(png) == Error.Ok ? image : null;
    }

    private static Texture2D? Texture(byte[] png)
    {
        Image? image = Decode(png);

        if (image is null)
            return null;

        image.GenerateMipmaps();

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// The normal, differentiated from the displacement rather than read. Three
    /// of every five sets ship a <b>flat</b> normal with the whole of their relief
    /// in the displacement, because the pack is written for a renderer that
    /// tessellates — and Godot does not under triplanar, measured: those arrive as
    /// printed patterns with no shading at all. Deriving it also makes this the
    /// same path a drawn height field takes, rather than a second one beside it.
    /// </summary>
    private static Texture2D? NormalFromHeight(byte[] png)
    {
        Image? height = Decode(png);

        if (height is null)
            return null;

        int width = height.GetWidth();
        int rows = height.GetHeight();
        Image normal = Image.CreateEmpty(width, rows, false, Image.Format.Rgb8);

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Wrapped, because the map tiles: a difference taken against a
                // clamped edge would put a seam where the repeat joins.
                float left = height.GetPixel((x + width - 1) % width, y).R;
                float right = height.GetPixel((x + 1) % width, y).R;
                float up = height.GetPixel(x, (y + rows - 1) % rows).R;
                float down = height.GetPixel(x, (y + 1) % rows).R;

                var slope = new Vector3(
                    -(right - left) * 0.5f * ReliefSlope,
                    (down - up) * 0.5f * ReliefSlope,
                    1f);

                Vector3 unit = slope.Normalized();

                normal.SetPixel(x, y, new Color(
                    unit.X * 0.5f + 0.5f,
                    unit.Y * 0.5f + 0.5f,
                    unit.Z * 0.5f + 0.5f));
            }
        }

        normal.GenerateMipmaps(renormalize: true);

        return ImageTexture.CreateFromImage(normal);
    }

    private sealed class PackReader
    {
        private readonly byte[] _bytes;
        private int _at;

        public PackReader(byte[] bytes) => _bytes = bytes;

        public int ReadInt()
        {
            int value = BitConverter.ToInt32(_bytes, _at);
            _at += 4;

            return value;
        }

        public string ReadAscii(int length)
        {
            string value = System.Text.Encoding.UTF8.GetString(_bytes, _at, length);
            _at += length;

            return value;
        }

        public byte[] ReadBytes(int length)
        {
            byte[] value = new byte[length];
            Array.Copy(_bytes, _at, value, 0, length);
            _at += length;

            return value;
        }
    }
}
