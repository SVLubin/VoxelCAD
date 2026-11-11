using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// An STL file as triangles, read without an engine.
/// </summary>
/// <remarks>
/// <para>
/// STL is the one model format with nothing in it but geometry: no materials, no
/// texture coordinates, no names, and no shared vertices. That is why it is here
/// and why it is short.
/// </para>
/// <para>
/// Its triangles are wound counter-clockwise by the right-hand rule, and Godot's
/// front faces are clockwise, so every triangle is reversed on the way in. Getting
/// that backwards draws a model inside out, which on a reference nobody would
/// question — it would simply look wrong in a way nobody could name.
/// </para>
/// </remarks>
internal static class StlGeometry
{
    private const int BinaryHeaderBytes = 80;
    private const int BinaryTriangleBytes = 50;

    /// <summary>
    /// The triangles, or false where the bytes are not an STL at all. Positions
    /// only: normals are recomputed from the winding, because a file's own normals
    /// are famously unreliable and disagreeing with the geometry is worse than
    /// having none.
    /// </summary>
    public static bool TryRead(byte[] bytes, out Vector3[] positions)
    {
        positions = Array.Empty<Vector3>();

        if (bytes.Length < 15)
            return false;

        return IsBinary(bytes) ? TryReadBinary(bytes, out positions) : TryReadText(bytes, out positions);
    }

    /// <summary>
    /// A binary file may begin with the word "solid" — plenty of exporters write
    /// one — so what tells the two apart is arithmetic rather than a signature: a
    /// binary file is exactly its header plus fifty bytes a triangle.
    /// </summary>
    private static bool IsBinary(byte[] bytes)
    {
        if (bytes.Length < BinaryHeaderBytes + 4)
            return false;

        uint count = BitConverter.ToUInt32(bytes, BinaryHeaderBytes);

        return bytes.Length == BinaryHeaderBytes + 4 + ((long)count * BinaryTriangleBytes);
    }

    private static bool TryReadBinary(byte[] bytes, out Vector3[] positions)
    {
        positions = Array.Empty<Vector3>();

        uint count = BitConverter.ToUInt32(bytes, BinaryHeaderBytes);
        var read = new Vector3[count * 3];
        int offset = BinaryHeaderBytes + 4;

        for (uint triangle = 0; triangle < count; triangle++)
        {
            // Twelve bytes of the file's own normal, skipped: the winding says it.
            int at = offset + (int)(triangle * BinaryTriangleBytes) + 12;

            for (var corner = 0; corner < 3; corner++)
            {
                read[(triangle * 3) + corner] = new Vector3(
                    BitConverter.ToSingle(bytes, at + (corner * 12)),
                    BitConverter.ToSingle(bytes, at + (corner * 12) + 4),
                    BitConverter.ToSingle(bytes, at + (corner * 12) + 8));
            }
        }

        positions = Reverse(read);

        return positions.Length > 0;
    }

    private static bool TryReadText(byte[] bytes, out Vector3[] positions)
    {
        positions = Array.Empty<Vector3>();

        var read = new List<Vector3>();
        string text = Encoding.UTF8.GetString(bytes);

        foreach (string line in text.Split('\n'))
        {
            string[] words = line.Split(
                new[] { ' ', '\t', '\r' },
                StringSplitOptions.RemoveEmptyEntries);

            if (words.Length < 4 || !string.Equals(words[0], "vertex", StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryNumber(words[1], out float x)
                && TryNumber(words[2], out float y)
                && TryNumber(words[3], out float z))
            {
                read.Add(new Vector3(x, y, z));
            }
        }

        // A trailing partial triangle is a truncated file; what is whole is kept.
        int whole = read.Count - (read.Count % 3);

        if (whole == 0)
            return false;

        positions = Reverse(read.GetRange(0, whole).ToArray());

        return true;
    }

    /// <summary>Every triangle's last two corners swapped: counter-clockwise to clockwise.</summary>
    private static Vector3[] Reverse(Vector3[] positions)
    {
        for (var triangle = 0; triangle + 2 < positions.Length; triangle += 3)
            (positions[triangle + 1], positions[triangle + 2]) =
                (positions[triangle + 2], positions[triangle + 1]);

        return positions;
    }

    private static bool TryNumber(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
