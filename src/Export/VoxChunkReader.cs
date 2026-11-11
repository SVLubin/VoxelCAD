using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// Walks a <c>.vox</c> file's chunk tree into a <see cref="VoxDocument"/>. It
/// reads and refuses; it decides nothing.
/// </summary>
/// <remarks>
/// Every read is bounded by the chunk header that announced it, so a damaged file
/// is refused by name rather than throwing whatever the arithmetic happens to
/// throw — and a chunk this reader has never heard of is stepped over by its
/// declared length, which is what the format's own layout exists for. The one
/// thing that must never happen is a partial document: this either returns a
/// whole one or throws before anything downstream has seen it.
/// </remarks>
internal static class VoxChunkReader
{
    private const string Magic = "VOX ";
    private const int HeaderBytes = 8;
    private const int ChunkHeaderBytes = 12;
    private const int PaletteBytes = 256 * 4;

    /// <summary>
    /// The least a dictionary entry can occupy: two lengths, and empty strings.
    /// What a declared entry count is checked against before it is believed.
    /// </summary>
    private const int MinimumDictionaryEntryBytes = 8;

    /// <summary>
    /// How deep the chunk tree may nest. The format allows a chunk to hold
    /// chunks, and in practice only <c>MAIN</c> ever does — so anything past a
    /// handful is a file built to break a reader rather than a model. Bounded
    /// because the walk is recursive: without this, fifty thousand nested chunks
    /// take the process down with a stack overflow, which is not a refusal.
    /// </summary>
    private const int MaximumChunkDepth = 64;

    public static VoxDocument Read(byte[] bytes)
    {
        if (bytes.Length < HeaderBytes + ChunkHeaderBytes)
            throw Damaged("it is too short to hold a header");

        if (Encoding.ASCII.GetString(bytes, 0, 4) != Magic)
        {
            throw Damaged(
                "it does not begin with 'VOX ' — this is not a MagicaVoxel file, whatever it "
                + "is called");
        }

        var document = new VoxDocument(BitConverter.ToInt32(bytes, 4));

        // MAIN carries no content of its own; everything is in its children.
        (string id, int contentBytes, int childrenBytes) = ChunkHeader(bytes, HeaderBytes);

        if (id != "MAIN")
            throw Damaged($"its first chunk is '{Readable(id)}' rather than 'MAIN'");

        var childrenAt = HeaderBytes + ChunkHeaderBytes + contentBytes;

        Walk(bytes, document, childrenAt, End(bytes, childrenAt, childrenBytes), depth: 0);

        if (document.Models.Count == 0)
            throw Damaged("it holds no model");

        return document;
    }

    private static void Walk(byte[] bytes, VoxDocument document, int at, int end, int depth)
    {
        if (depth > MaximumChunkDepth)
            throw Damaged($"its chunks nest more than {MaximumChunkDepth} deep");

        while (at < end)
        {
            if (at + ChunkHeaderBytes > end)
                throw Damaged("a chunk header runs past the end of the file");

            (string id, int contentBytes, int childrenBytes) = ChunkHeader(bytes, at);
            var contentAt = at + ChunkHeaderBytes;

            // Widened deliberately. A chunk may declare any 32-bit length it likes,
            // and a length near int.MaxValue added to an offset wraps negative —
            // which slips past a comparison written in int and leaves the walk
            // reading from a negative index. In long it cannot wrap.
            if (contentBytes < 0 || childrenBytes < 0
                || (long)contentAt + contentBytes + childrenBytes > end)
            {
                throw Damaged($"chunk '{Readable(id)}' declares more content than the file holds");
            }

            var content = new VoxSpan(bytes, contentAt, contentBytes);

            switch (id)
            {
                case "SIZE":
                    document.Models.Add(new VoxDocument.VoxModel(ReadSize(content)));
                    break;

                case "XYZI":
                    ReadVoxels(content, document);
                    break;

                case "RGBA":
                    document.Palette = ReadPalette(content, out int paletteBytes);
                    document.PaletteBytes = paletteBytes;
                    break;

                case "MATL":
                    ReadMaterial(content, document);
                    break;

                case "nTRN":
                    ReadTransform(content, document);
                    break;

                case "nGRP":
                    ReadGroup(content, document);
                    break;

                case "nSHP":
                    ReadShape(content, document);
                    break;

                // PACK counts the models a pre-scene-graph file holds, which the
                // SIZE chunks already say; LAYR, rOBJ, rCAM, NOTE, IMAP and META
                // are about how MagicaVoxel presents the file rather than what is
                // in it. Named here so the next reader knows they were considered
                // rather than missed — but the default below would step over them
                // anyway, which is the point of a length-prefixed format.
                default:
                    break;
            }

            var childrenAt = contentAt + contentBytes;

            if (childrenBytes > 0)
                Walk(bytes, document, childrenAt, End(bytes, childrenAt, childrenBytes), depth + 1);

            at = childrenAt + childrenBytes;
        }
    }

    private static Vector3I ReadSize(VoxSpan content)
    {
        var size = new Vector3I(content.Int32(), content.Int32(), content.Int32());

        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            throw Damaged($"a model declares a size of {size.X} by {size.Y} by {size.Z}");

        return size;
    }

    private static void ReadVoxels(VoxSpan content, VoxDocument document)
    {
        if (document.Models.Count == 0)
            throw Damaged("it carries voxels before it says how big the model is");

        VoxDocument.VoxModel model = document.Models[^1];
        int count = content.Int32();

        if (count < 0)
            throw Damaged("a model declares a negative voxel count");

        for (var i = 0; i < count; i++)
        {
            var cell = new Vector3I(content.Byte(), content.Byte(), content.Byte());
            byte colorIndex = content.Byte();

            // Index 0 is the format's own word for "empty"; a voxel wearing it is
            // not a voxel, and carrying it forward would put a hole in the palette.
            if (colorIndex == 0)
                continue;

            // Counted across every model in the file, not per model: a file may
            // hold as many as it likes, and one import is what has to fit in a
            // machine.
            if (++document.Cells > ImportLimits.MaximumCells)
                throw new InvalidOperationException(ImportLimits.TooManyCells(".vox", document.Cells));

            model.Cells.Add((cell, colorIndex));
        }
    }

    private static byte[] ReadPalette(VoxSpan content, out int present)
    {
        var palette = new byte[PaletteBytes];

        // Short palettes turn up in the wild. How much of it was actually there is
        // carried out rather than left to be inferred from zeroes: an entry the
        // chunk did not cover has to fall back to the format's own default
        // palette, and a run of zeroes is a colour — transparent black — not an
        // absence.
        present = Math.Min(PaletteBytes, content.Remaining);
        content.CopyTo(palette, present);

        return palette;
    }

    private static void ReadMaterial(VoxSpan content, VoxDocument document)
    {
        int id = content.Int32();

        // Later wins: 0.99.6 and 0.99.7 both write a MATL per entry, and a file
        // saved by one and re-saved by the other can carry two.
        document.Materials[id] = content.Dictionary();
    }

    private static void ReadTransform(VoxSpan content, VoxDocument document)
    {
        int id = content.Int32();
        IReadOnlyDictionary<string, string> attributes = content.Dictionary();
        var node = new VoxDocument.VoxNode(VoxNodeKind.Transform, Name(attributes))
        {
            Child = content.Int32()
        };

        content.Int32();
        content.Int32();

        int frames = content.Int32();

        if (frames < 0)
            throw Damaged("a transform node declares a negative frame count");

        node.Frames = frames;

        for (var i = 0; i < frames; i++)
        {
            IReadOnlyDictionary<string, string> frame = content.Dictionary();

            // Frame zero is the pose the scene stands in; the rest is animation,
            // which this document has no notion of.
            if (i > 0)
                continue;

            node.Translation = ReadTranslation(frame);
            node.Rotation = ReadRotation(frame);
        }

        document.Nodes[id] = node;
    }

    private static Vector3I ReadTranslation(IReadOnlyDictionary<string, string> frame)
    {
        if (!frame.TryGetValue("_t", out string? text))
            return Vector3I.Zero;

        string[] partsList = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (partsList.Length != 3
            || !int.TryParse(partsList[0], out int x)
            || !int.TryParse(partsList[1], out int y)
            || !int.TryParse(partsList[2], out int z))
        {
            throw Damaged($"a transform node's translation reads '{text}'");
        }

        return new Vector3I(x, y, z);
    }

    private static Basis ReadRotation(IReadOnlyDictionary<string, string> frame)
    {
        if (!frame.TryGetValue("_r", out string? text))
            return VoxRotation.Identity;

        if (!byte.TryParse(text, out byte packed) || !VoxRotation.TryDecode(packed, out Basis rotation))
            throw Damaged($"a transform node's rotation reads '{text}', which names no rotation");

        return rotation;
    }

    private static void ReadGroup(VoxSpan content, VoxDocument document)
    {
        int id = content.Int32();
        IReadOnlyDictionary<string, string> attributes = content.Dictionary();
        var node = new VoxDocument.VoxNode(VoxNodeKind.Group, Name(attributes));
        int children = content.Int32();

        if (children < 0)
            throw Damaged("a group node declares a negative child count");

        for (var i = 0; i < children; i++)
            node.Children.Add(content.Int32());

        document.Nodes[id] = node;
    }

    private static void ReadShape(VoxSpan content, VoxDocument document)
    {
        int id = content.Int32();
        IReadOnlyDictionary<string, string> attributes = content.Dictionary();
        var node = new VoxDocument.VoxNode(VoxNodeKind.Shape, Name(attributes));
        int models = content.Int32();

        if (models < 0)
            throw Damaged("a shape node declares a negative model count");

        for (var i = 0; i < models; i++)
        {
            node.Models.Add(content.Int32());
            content.Dictionary();
        }

        document.Nodes[id] = node;
    }

    private static string? Name(IReadOnlyDictionary<string, string> attributes) =>
        attributes.TryGetValue("_name", out string? name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;

    private static (string Id, int ContentBytes, int ChildrenBytes) ChunkHeader(byte[] bytes, int at) => (
        Encoding.ASCII.GetString(bytes, at, 4),
        BitConverter.ToInt32(bytes, at + 4),
        BitConverter.ToInt32(bytes, at + 8));

    private static int End(byte[] bytes, int at, int length)
    {
        long end = (long)at + length;

        if (at < 0 || length < 0 || end > bytes.Length)
            throw Damaged("a chunk's children run past the end of the file");

        return (int)end;
    }

    /// <summary>A chunk id with anything unprintable shown as a dot.</summary>
    private static string Readable(string id)
    {
        var builder = new StringBuilder(id.Length);

        foreach (char character in id)
            builder.Append(character is >= ' ' and <= '~' ? character : '.');

        return builder.ToString();
    }

    private static InvalidOperationException Damaged(string reason) =>
        new($"This .vox file cannot be read: {reason}.");

    /// <summary>
    /// A cursor over one chunk's content that cannot read past it. Every field a
    /// chunk declares is read through this, so a truncated chunk is a named
    /// refusal rather than an <see cref="IndexOutOfRangeException"/> from
    /// somewhere in the middle of a parse.
    /// </summary>
    private sealed class VoxSpan
    {
        private readonly byte[] _bytes;
        private readonly int _end;

        private int _at;

        public VoxSpan(byte[] bytes, int at, int length)
        {
            _bytes = bytes;
            _at = at;
            _end = at + length;
        }

        public int Remaining => _end - _at;

        public int Int32()
        {
            Require(4);

            int value = BitConverter.ToInt32(_bytes, _at);
            _at += 4;

            return value;
        }

        public byte Byte()
        {
            Require(1);

            return _bytes[_at++];
        }

        public void CopyTo(byte[] destination, int count)
        {
            Require(count);
            Array.Copy(_bytes, _at, destination, 0, count);
            _at += count;
        }

        /// <summary>
        /// MagicaVoxel's DICT: a count, then that many length-prefixed key and
        /// value pairs. The strings are bytes rather than characters — read as
        /// UTF-8, which is what the files carry and what ASCII is a subset of.
        /// </summary>
        public IReadOnlyDictionary<string, string> Dictionary()
        {
            int count = Int32();

            // Bounded before it is used as a capacity. An entry is two
            // length-prefixed strings, so it cannot be shorter than eight bytes —
            // and without this a forty-byte file could ask for a dictionary of two
            // billion entries and be answered with an allocation rather than a
            // refusal.
            if (count < 0 || (long)count * MinimumDictionaryEntryBytes > Remaining)
                throw Damaged("a dictionary declares more entries than the chunk holds");

            var entriesDictionary = new Dictionary<string, string>(count);

            for (var i = 0; i < count; i++)
            {
                string key = String();
                entriesDictionary[key] = String();
            }

            return entriesDictionary;
        }

        private string String()
        {
            int length = Int32();

            if (length < 0)
                throw Damaged("a string declares a negative length");

            Require(length);

            string value = Encoding.UTF8.GetString(_bytes, _at, length);
            _at += length;

            return value;
        }

        private void Require(int count)
        {
            if (count < 0 || _at + count > _end)
                throw Damaged("a chunk ends in the middle of a value");
        }
    }
}
