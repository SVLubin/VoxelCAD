using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Godot;

namespace VoxelCAD.Model;

/// <summary>
/// Packs a voxel model's cells into one text-safe string, so an asset stays a
/// single readable file without a YAML node per cell — millions of them would
/// bury both the parser and the reader.
/// </summary>
/// <remarks>
/// Layout, after gzip and Base64: a version byte, a palette of the material ids
/// this model actually uses, then one record per cell. The palette exists so a
/// cell costs a two-byte index instead of a sixteen-byte <see cref="Guid"/>;
/// gzip then removes most of what is left, since neighbouring cells repeat.
/// The version byte is what allows a denser layout later without a migration.
/// </remarks>
internal static class VoxelBlobCodec
{
    private const byte CurrentVersion = 1;
    private const int LineLength = 96;

    public static string Encode(IReadOnlyDictionary<Vector3I, Guid> voxels)
    {
        var paletteList = new List<Guid>();
        var indicesByMaterialDictionary = new Dictionary<Guid, ushort>();

        foreach (Guid materialId in voxels.Values)
        {
            if (indicesByMaterialDictionary.ContainsKey(materialId))
                continue;

            indicesByMaterialDictionary[materialId] = (ushort)paletteList.Count;
            paletteList.Add(materialId);
        }

        using var buffer = new MemoryStream();

        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip))
        {
            writer.Write(CurrentVersion);
            writer.Write(paletteList.Count);

            foreach (Guid materialId in paletteList)
                writer.Write(materialId.ToByteArray());

            writer.Write(voxels.Count);

            foreach (KeyValuePair<Vector3I, Guid> voxel in voxels)
            {
                writer.Write(voxel.Key.X);
                writer.Write(voxel.Key.Y);
                writer.Write(voxel.Key.Z);
                writer.Write(indicesByMaterialDictionary[voxel.Value]);
            }
        }

        // Wrapped by hand rather than with InsertLineBreaks, which emits CRLF:
        // a YAML literal block cannot carry a carriage return, so the emitter
        // would fall back to one enormous quoted line. Convert.FromBase64String
        // ignores whitespace on the way back.
        return Wrap(Convert.ToBase64String(buffer.ToArray()));
    }

    /// <summary>Shared with the other blob codecs so every blob wraps the same way.</summary>
    internal static string Wrap(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + value.Length / LineLength + 1);

        for (var offset = 0; offset < value.Length; offset += LineLength)
        {
            if (offset > 0)
                builder.Append('\n');

            builder.Append(value, offset, Math.Min(LineLength, value.Length - offset));
        }

        return builder.ToString();
    }

    public static Dictionary<Vector3I, Guid> Decode(string blob)
    {
        var voxelsDictionary = new Dictionary<Vector3I, Guid>();

        if (string.IsNullOrWhiteSpace(blob))
            return voxelsDictionary;

        using var buffer = new MemoryStream(Convert.FromBase64String(blob));
        using var gzip = new GZipStream(buffer, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip);

        var version = reader.ReadByte();

        if (version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Voxel blob version {version} is not supported; this build writes {CurrentVersion}.");
        }

        var paletteCount = reader.ReadInt32();
        var palette = new Guid[paletteCount];

        for (var i = 0; i < paletteCount; i++)
            palette[i] = new Guid(reader.ReadBytes(16));

        var cellCount = reader.ReadInt32();

        for (var i = 0; i < cellCount; i++)
        {
            var cell = new Vector3I(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            voxelsDictionary[cell] = palette[reader.ReadUInt16()];
        }

        return voxelsDictionary;
    }
}
