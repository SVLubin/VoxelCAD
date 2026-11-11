using System;
using System.IO;
using Godot;

namespace VoxelCAD.Model;

/// <summary>
/// How big a picture is, read from the file's own header and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The engine has an image loader and the model may not use it: the headless CLI
/// has no engine, and a plane whose size nobody could compute would arrive at a
/// number nobody chose. A header is a few dozen bytes at a fixed place in almost
/// every format here, so reading it costs a file handle and no decoding at all —
/// which also means a hundred-megabyte photograph is measured as cheaply as a
/// thumbnail.
/// </para>
/// <para>
/// The formats are exactly those the viewport can go on to draw. Reading a header
/// this editor could not then display would be a size promised for a picture
/// nobody can see.
/// </para>
/// </remarks>
public static class ImageHeader
{
    private const int HeaderBytes = 64;

    /// <summary>
    /// The picture's resolution, or false where the file is missing, unreadable or
    /// not one of the formats named above. A false answer is not an error: a
    /// reference may legitimately name a file that is not there.
    /// </summary>
    public static bool TryRead(string path, out Vector2I size)
    {
        size = Vector2I.Zero;

        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            using FileStream stream = File.OpenRead(path);

            var header = new byte[HeaderBytes];
            int read = stream.Read(header, 0, HeaderBytes);

            if (read < 18)
                return false;

            if (read < HeaderBytes)
                Array.Resize(ref header, read);

            return TryReadPng(header, out size)
                || TryReadWebp(header, out size)
                || TryReadBmp(header, out size)
                || TryReadJpeg(stream, header, out size)
                || TryReadTga(path, header, out size);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryReadPng(byte[] header, out Vector2I size)
    {
        size = Vector2I.Zero;

        // The signature, then IHDR, which the format requires to come first.
        if (header.Length < 24 || !Matches(header, 0, 0x89, 0x50, 0x4E, 0x47))
            return false;

        size = new Vector2I(ReadBigEndian32(header, 16), ReadBigEndian32(header, 20));

        return IsPlausible(size);
    }

    private static bool TryReadBmp(byte[] header, out Vector2I size)
    {
        size = Vector2I.Zero;

        if (header.Length < 26 || header[0] != 0x42 || header[1] != 0x4D)
            return false;

        // A negative height is a top-down bitmap, which is a fact about row order
        // rather than about how big the picture is.
        size = new Vector2I(
            Math.Abs(ReadLittleEndian32(header, 18)),
            Math.Abs(ReadLittleEndian32(header, 22)));

        return IsPlausible(size);
    }

    private static bool TryReadWebp(byte[] header, out Vector2I size)
    {
        size = Vector2I.Zero;

        if (header.Length < 30 || !Matches(header, 0, 0x52, 0x49, 0x46, 0x46))
            return false;

        if (!Matches(header, 8, 0x57, 0x45, 0x42, 0x50))
            return false;

        // Three containers, and the dimensions sit somewhere different in each.
        if (Matches(header, 12, 0x56, 0x50, 0x38, 0x20) && Matches(header, 23, 0x9D, 0x01, 0x2A))
        {
            size = new Vector2I(
                ReadLittleEndian16(header, 26) & 0x3FFF,
                ReadLittleEndian16(header, 28) & 0x3FFF);
        }
        else if (Matches(header, 12, 0x56, 0x50, 0x38, 0x4C) && header[20] == 0x2F)
        {
            // Fourteen bits each, packed across four bytes, and one less than the
            // real thing.
            int packed = ReadLittleEndian32(header, 21);

            size = new Vector2I((packed & 0x3FFF) + 1, ((packed >> 14) & 0x3FFF) + 1);
        }
        else if (Matches(header, 12, 0x56, 0x50, 0x38, 0x58))
        {
            size = new Vector2I(
                ReadLittleEndian24(header, 24) + 1,
                ReadLittleEndian24(header, 27) + 1);
        }
        else
        {
            return false;
        }

        return IsPlausible(size);
    }

    /// <summary>
    /// The one format whose size is not at a fixed offset: the frame header may
    /// stand behind any number of other segments, so the markers are walked.
    /// </summary>
    private static bool TryReadJpeg(Stream stream, byte[] header, out Vector2I size)
    {
        size = Vector2I.Zero;

        if (header.Length < 4 || header[0] != 0xFF || header[1] != 0xD8)
            return false;

        stream.Seek(2, SeekOrigin.Begin);

        var segment = new byte[5];

        while (true)
        {
            int marker = stream.ReadByte();

            // Padding between segments is legal and is any number of 0xFF bytes.
            while (marker == 0xFF)
                marker = stream.ReadByte();

            if (marker < 0)
                return false;

            // The scan has begun, or the picture has ended: there is no frame
            // header left to find.
            if (marker == 0xDA || marker == 0xD9)
                return false;

            if (stream.Read(segment, 0, 2) < 2)
                return false;

            int length = (segment[0] << 8) | segment[1];

            if (length < 2)
                return false;

            if (IsFrameHeader(marker))
            {
                if (stream.Read(segment, 0, 5) < 5)
                    return false;

                // Precision, then height, then width — that order, which is the
                // one thing about this format that catches everybody out.
                size = new Vector2I(
                    (segment[3] << 8) | segment[4],
                    (segment[1] << 8) | segment[2]);

                return IsPlausible(size);
            }

            stream.Seek(length - 2, SeekOrigin.Current);
        }
    }

    /// <summary>
    /// Targa has no signature at all, so it is recognised by its name and then
    /// sanity-checked. Everything before the dimensions is fixed-width, which is
    /// what makes that safe enough.
    /// </summary>
    private static bool TryReadTga(string path, byte[] header, out Vector2I size)
    {
        size = Vector2I.Zero;

        if (!path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) || header.Length < 18)
            return false;

        size = new Vector2I(ReadLittleEndian16(header, 12), ReadLittleEndian16(header, 14));

        return IsPlausible(size);
    }

    private static bool IsFrameHeader(int marker) =>
        marker >= 0xC0
        && marker <= 0xCF
        // Huffman tables, arithmetic conditioning and the reserved marker wear
        // numbers inside the frame-header run without being one.
        && marker != 0xC4
        && marker != 0xC8
        && marker != 0xCC;

    /// <summary>
    /// A size read out of the wrong bytes is usually nought or enormous, so the
    /// bound is what tells a header apart from a coincidence.
    /// </summary>
    private static bool IsPlausible(Vector2I size) =>
        size.X > 0 && size.Y > 0 && size.X <= 65535 && size.Y <= 65535;

    private static bool Matches(byte[] bytes, int offset, params byte[] expected)
    {
        if (offset + expected.Length > bytes.Length)
            return false;

        for (var index = 0; index < expected.Length; index++)
        {
            if (bytes[offset + index] != expected[index])
                return false;
        }

        return true;
    }

    private static int ReadBigEndian32(byte[] bytes, int offset) =>
        (bytes[offset] << 24)
        | (bytes[offset + 1] << 16)
        | (bytes[offset + 2] << 8)
        | bytes[offset + 3];

    private static int ReadLittleEndian32(byte[] bytes, int offset) =>
        bytes[offset]
        | (bytes[offset + 1] << 8)
        | (bytes[offset + 2] << 16)
        | (bytes[offset + 3] << 24);

    private static int ReadLittleEndian24(byte[] bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);

    private static int ReadLittleEndian16(byte[] bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8);
}
