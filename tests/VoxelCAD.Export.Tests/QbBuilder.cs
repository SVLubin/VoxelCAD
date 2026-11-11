using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// Builds Qubicle Binary bytes by hand, both the plain and the run-length stream,
/// so a reader test states the file it is about.
/// </summary>
internal sealed class QbBuilder
{
    private const uint CodeFlag = 2;
    private const uint NextSliceFlag = 6;

    private readonly List<Action<BinaryWriter>> _matricesList = new();

    public uint ColorFormat { get; set; }

    /// <summary>0 is left handed, 1 is right handed.</summary>
    public uint ZAxisOrientation { get; set; }

    public uint Compressed { get; set; }

    public uint VisibilityMaskEncoded { get; set; }

    public static QbBuilder File() => new();

    /// <summary>
    /// One matrix. <paramref name="cells"/> is indexed x fastest, then y, then z —
    /// the order the format stores them in — and holds the packed value per cell.
    /// </summary>
    public QbBuilder Matrix(
        string name,
        int sizeX,
        int sizeY,
        int sizeZ,
        (int X, int Y, int Z) position,
        uint[] cells)
    {
        _matricesList.Add(writer =>
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);

            writer.Write((byte)nameBytes.Length);
            writer.Write(nameBytes);
            writer.Write((uint)sizeX);
            writer.Write((uint)sizeY);
            writer.Write((uint)sizeZ);
            writer.Write(position.X);
            writer.Write(position.Y);
            writer.Write(position.Z);

            if (Compressed == 0)
            {
                foreach (uint cell in cells)
                    writer.Write(cell);

                return;
            }

            for (var z = 0; z < sizeZ; z++)
            {
                WriteSlice(writer, cells, sizeX * sizeY, z);
                writer.Write(NextSliceFlag);
            }
        });

        return this;
    }

    /// <summary>
    /// A slice as runs, using the repeat code wherever a value appears more than
    /// twice in a row — so a test exercises both the plain and the coded branch of
    /// the same stream.
    /// </summary>
    private static void WriteSlice(BinaryWriter writer, uint[] cells, int perSlice, int z)
    {
        var at = z * perSlice;
        var end = at + perSlice;

        while (at < end)
        {
            uint value = cells[at];
            var run = 1;

            while (at + run < end && cells[at + run] == value)
                run++;

            if (run > 2)
            {
                writer.Write(CodeFlag);
                writer.Write((uint)run);
                writer.Write(value);
            }
            else
            {
                for (var i = 0; i < run; i++)
                    writer.Write(value);
            }

            at += run;
        }
    }

    /// <summary>A packed cell in the file's own channel order.</summary>
    public uint Cell(byte first, byte second, byte third, byte alpha) =>
        (uint)(first | (second << 8) | (third << 16) | (alpha << 24));

    public byte[] Build(uint version = 0x0000_0101)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        writer.Write(version);
        writer.Write(ColorFormat);
        writer.Write(ZAxisOrientation);
        writer.Write(Compressed);
        writer.Write(VisibilityMaskEncoded);
        writer.Write((uint)_matricesList.Count);

        foreach (Action<BinaryWriter> matrix in _matricesList)
            matrix(writer);

        writer.Flush();

        return buffer.ToArray();
    }
}
