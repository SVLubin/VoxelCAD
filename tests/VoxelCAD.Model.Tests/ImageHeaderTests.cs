using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// How big a picture is, read from the file's own header. The fixtures are built
/// byte by byte rather than checked in: what is being tested is the reading of a
/// layout, and a layout written out in the test is the specification the reader is
/// held to.
/// </summary>
public sealed class ImageHeaderTests : IDisposable
{
    private readonly List<string> _pathsList = new();

    public void Dispose()
    {
        foreach (string path in _pathsList)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void APngIsMeasuredFromItsFirstChunk()
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        bytes.AddRange(new byte[] { 0, 0, 0, 13 });
        bytes.AddRange(new byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' });
        bytes.AddRange(BigEndian(1920));
        bytes.AddRange(BigEndian(1080));
        bytes.AddRange(new byte[] { 8, 6, 0, 0, 0, 0, 0, 0, 0 });

        Assert.True(ImageHeader.TryRead(Write(".png", bytes.ToArray()), out Vector2I size));
        Assert.Equal(new Vector2I(1920, 1080), size);
    }

    /// <summary>
    /// The frame header may stand behind any number of other segments, so this is
    /// the one format where the reader walks rather than seeks — and the one where
    /// height comes before width, which is what the fixture pins.
    /// </summary>
    [Fact]
    public void AJpegIsFoundBehindTheSegmentsInFrontOfIt()
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        // An APP0 segment of eleven bytes, standing between the start of the file
        // and the frame header the size lives in.
        bytes.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x0B });
        bytes.AddRange(new byte[] { (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0 });

        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 8 });
        bytes.AddRange(BigEndian16(480));
        bytes.AddRange(BigEndian16(640));
        bytes.AddRange(new byte[] { 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1 });

        Assert.True(ImageHeader.TryRead(Write(".jpg", bytes.ToArray()), out Vector2I size));
        Assert.Equal(new Vector2I(640, 480), size);
    }

    /// <summary>
    /// A Huffman table wears a marker number inside the frame-header run without
    /// being one, so a reader that took every 0xC-something for a frame would read
    /// the size out of a table.
    /// </summary>
    [Fact]
    public void AHuffmanTableIsNotMistakenForAFrameHeader()
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        bytes.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x06, 0x00, 0x01, 0x02, 0x03 });
        bytes.AddRange(new byte[] { 0xFF, 0xC2, 0x00, 0x11, 8 });
        bytes.AddRange(BigEndian16(200));
        bytes.AddRange(BigEndian16(100));
        bytes.AddRange(new byte[] { 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1 });

        Assert.True(ImageHeader.TryRead(Write(".jpg", bytes.ToArray()), out Vector2I size));
        Assert.Equal(new Vector2I(100, 200), size);
    }

    [Fact]
    public void ALossyWebpIsMeasuredFromItsFrameHeader()
    {
        var bytes = new List<byte>();

        bytes.AddRange(Ascii("RIFF"));
        bytes.AddRange(LittleEndian(1024));
        bytes.AddRange(Ascii("WEBP"));
        bytes.AddRange(Ascii("VP8 "));
        bytes.AddRange(LittleEndian(512));
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00 });
        bytes.AddRange(new byte[] { 0x9D, 0x01, 0x2A });
        bytes.AddRange(LittleEndian16(320));
        bytes.AddRange(LittleEndian16(240));

        Assert.True(ImageHeader.TryRead(Write(".webp", bytes.ToArray()), out Vector2I size));
        Assert.Equal(new Vector2I(320, 240), size);
    }

    /// <summary>
    /// Fourteen bits each, packed across four bytes and stored one less than the
    /// real thing — so an off-by-one here reads as a picture a pixel short.
    /// </summary>
    [Fact]
    public void ALosslessWebpUnpacksItsFourteenBitFields()
    {
        var bytes = new List<byte>();

        bytes.AddRange(Ascii("RIFF"));
        bytes.AddRange(LittleEndian(1024));
        bytes.AddRange(Ascii("WEBP"));
        bytes.AddRange(Ascii("VP8L"));
        bytes.AddRange(LittleEndian(512));
        bytes.Add(0x2F);
        bytes.AddRange(LittleEndian(((150 - 1) << 14) | (300 - 1)));
        bytes.AddRange(new byte[] { 0, 0, 0, 0, 0 });

        Assert.True(ImageHeader.TryRead(Write(".webp", bytes.ToArray()), out Vector2I size));
        Assert.Equal(new Vector2I(300, 150), size);
    }

    /// <summary>
    /// A negative height is a top-down bitmap — a fact about which row comes first,
    /// not about how tall the picture is.
    /// </summary>
    [Fact]
    public void ATopDownBitmapIsNotMinusTwoHundredTall()
    {
        var bytes = new List<byte> { 0x42, 0x4D };

        bytes.AddRange(new byte[16]);
        bytes.AddRange(LittleEndian(100));
        bytes.AddRange(LittleEndian(-200));
        bytes.AddRange(new byte[28]);

        Assert.True(ImageHeader.TryRead(Write(".bmp", bytes.ToArray()), out Vector2I size));
        Assert.Equal(new Vector2I(100, 200), size);
    }

    /// <summary>
    /// Targa has no signature, so it is recognised by its name — which means the
    /// same bytes under another name must not be measured as one.
    /// </summary>
    [Fact]
    public void ATargaIsRecognisedByItsNameAndOnlyByIt()
    {
        var bytes = new byte[18];

        bytes[12] = 0x40;
        bytes[13] = 0x01;
        bytes[14] = 0xC8;

        Assert.True(ImageHeader.TryRead(Write(".tga", bytes), out Vector2I size));
        Assert.Equal(new Vector2I(320, 200), size);

        Assert.False(ImageHeader.TryRead(Write(".png", bytes), out _));
    }

    [Fact]
    public void AFileThatIsNotThereIsNotAnError()
    {
        Assert.False(ImageHeader.TryRead(
            Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.png"),
            out Vector2I size));

        Assert.Equal(Vector2I.Zero, size);
        Assert.False(ImageHeader.TryRead(string.Empty, out _));
    }

    /// <summary>
    /// A size read out of the wrong bytes is what the plausibility bound exists to
    /// catch: without it, any file at all would be measured as something.
    /// </summary>
    [Fact]
    public void RubbishIsNotMeasured()
    {
        Assert.False(ImageHeader.TryRead(Write(".png", new byte[64]), out _));
        Assert.False(ImageHeader.TryRead(Write(".dat", new byte[64]), out _));
    }

    private string Write(string extension, byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"voxelcad-{Guid.NewGuid():N}{extension}");

        File.WriteAllBytes(path, bytes);
        _pathsList.Add(path);

        return path;
    }

    private static byte[] Ascii(string text)
    {
        var bytes = new byte[text.Length];

        for (var index = 0; index < text.Length; index++)
            bytes[index] = (byte)text[index];

        return bytes;
    }

    private static byte[] BigEndian(int value) => new[]
    {
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
    };

    private static byte[] BigEndian16(int value) => new[] { (byte)(value >> 8), (byte)value };

    private static byte[] LittleEndian(int value) => new[]
    {
        (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)
    };

    private static byte[] LittleEndian16(int value) => new[] { (byte)value, (byte)(value >> 8) };
}
