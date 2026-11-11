using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Export;

/// <summary>
/// Reads Qubicle Binary <c>.qb</c>: a header, then a named matrix per part, each
/// carrying a colour per voxel and no palette at all.
/// </summary>
/// <remarks>
/// Qubicle is Y-up like this document, so nothing turns — a matrix's cells are
/// ours with only its position added. The one axis question is the header's
/// handedness flag: a left-handed file measures depth the other way, so its Z is
/// mirrored on the way in. That is what Goxel's reader does and what the
/// specification's wording implies; it is the one thing here not settled against a
/// file whose contents are known independently, which is why the cross-format test
/// exists.
/// <para>
/// The alpha byte is never an opacity. With the visibility mask off it is 0 or
/// 255; with it on it is a bitmask of which sides Qubicle would draw. The
/// specification's own test is <c>mask == 0</c> for an empty voxel, so anything
/// non-zero is a filled cell and nothing else — every material a <c>.qb</c>
/// produces is opaque.
/// </para>
/// </remarks>
internal sealed class QbImportFormat : IImportFormat
{
    private const uint CodeFlag = 2;
    private const uint NextSliceFlag = 6;

    /// <summary>
    /// Colours are deduplicated into materials, and a file whose colours came from
    /// a photograph or a mesh could name millions. Past this it is refused with the
    /// count rather than filling the project tree with a document nobody can work
    /// in. Measured on real files for scale: a Qubicle knight of 2,721 voxels wears
    /// 17 colours, and a four-million-voxel terrain chunk wears 27.
    /// </summary>
    private const int MaximumMaterials = 1024;

    /// <summary>
    /// A matrix is a dense grid, so its own declared size can be refused before a
    /// byte of it is read. One matrix alone may not ask for more than the whole
    /// import may — see <see cref="ImportLimits.MaximumCells"/>, which is also
    /// counted across every matrix as they are read.
    /// </summary>
    private const long MaximumCells = ImportLimits.MaximumCells;

    /// <summary>
    /// The least a matrix can occupy: an empty name, three sizes and three
    /// positions. What a declared matrix count is checked against before it is
    /// believed.
    /// </summary>
    private const int MinimumMatrixBytes = 1 + 12 + 12;

    public string Extension => ".qb";

    public string Label => "Qubicle Binary";

    public ImportedFile Read(byte[] bytes, string name)
    {
        var cursor = new QbCursor(bytes);

        cursor.UInt32();

        uint colorFormat = cursor.UInt32();
        uint zAxisOrientation = cursor.UInt32();
        uint compressed = cursor.UInt32();

        cursor.UInt32();

        uint matrices = cursor.UInt32();

        // Bounded against the file's own size before it is used as a capacity. A
        // matrix cannot be shorter than its own header, so a twenty-four byte file
        // declaring four billion of them is answered with a refusal rather than
        // with an allocation.
        if (matrices > int.MaxValue || (long)matrices * MinimumMatrixBytes > bytes.Length)
            throw Damaged($"it declares {matrices} matrices");

        var palette = new QbPalette();
        var rootsList = new List<ImportedNode>((int)matrices);
        var notesList = new List<string>();
        bool leftHanded = zAxisOrientation == 0;
        var budget = new CellBudget();

        for (var i = 0; i < matrices; i++)
        {
            rootsList.Add(ReadMatrix(
                cursor, palette, colorFormat == 1, leftHanded, compressed != 0, name, budget));
        }

        if (palette.Materials.Count > MaximumMaterials)
        {
            throw new InvalidOperationException(
                $"This .qb file holds {palette.Materials.Count} different colours, over the "
                + $"{MaximumMaterials} an import will make materials of. Qubicle stores a colour "
                + "per voxel rather than a palette, so a file painted from a photograph or a mesh "
                + "has no small set of materials to make.");
        }

        return new ImportedFile(rootsList, palette.Materials, notesList);
    }

    private static ImportedNode ReadMatrix(
        QbCursor cursor,
        QbPalette palette,
        bool bgra,
        bool leftHanded,
        bool compressed,
        string fileName,
        CellBudget budget)
    {
        string name = cursor.Name();
        var size = new Vector3I(cursor.Size(), cursor.Size(), cursor.Size());
        var position = new Vector3I(cursor.Int32(), cursor.Int32(), cursor.Int32());

        // Step by step, because the product of three int.MaxValues overflows even
        // a long — so a single multiplication could wrap past the bound it is
        // being compared against and let an impossible matrix through.
        if (size.X > MaximumCells
            || (long)size.X * size.Y > MaximumCells
            || (long)size.X * size.Y * size.Z > MaximumCells)
        {
            throw Damaged(
                $"a matrix declares {size.X} by {size.Y} by {size.Z} cells, which is not a model");
        }

        var cellsList = new List<ImportedCell>();

        if (compressed)
            ReadCompressed(cursor, size, palette, bgra, leftHanded, cellsList, budget);
        else
            ReadUncompressed(cursor, size, palette, bgra, leftHanded, cellsList, budget);

        // A left-handed file measures depth the other way, so its cells were
        // mirrored on the way in and the matrix's position has to be mirrored with
        // them — the far edge of the matrix is where its near edge was.
        var offset = leftHanded
            ? new Vector3I(position.X, position.Y, -position.Z - size.Z)
            : position;

        return new ImportedNode(
            string.IsNullOrWhiteSpace(name) ? fileName : name,
            new CellTransform(Basis.Identity, offset),
            new ImportedVolume(size, cellsList, Pivot(size)),
            Array.Empty<ImportedNode>());
    }

    /// <summary>
    /// The middle of the matrix, on the half-cell lattice. Qubicle states no pivot
    /// of its own in this format, and the middle of the grid is what MagicaVoxel
    /// turns a model about — so the two formats arrive with handles in the same
    /// place rather than in two different ones.
    /// </summary>
    private static Vector3 Pivot(Vector3I size) => new(
        size.X / 2 + 0.5f,
        size.Y / 2 + 0.5f,
        size.Z / 2 + 0.5f);

    private static void ReadUncompressed(
        QbCursor cursor,
        Vector3I size,
        QbPalette palette,
        bool bgra,
        bool leftHanded,
        List<ImportedCell> cellsList,
        CellBudget budget)
    {
        for (var z = 0; z < size.Z; z++)
        {
            for (var y = 0; y < size.Y; y++)
            {
                for (var x = 0; x < size.X; x++)
                    Add(cellsList, palette, cursor.UInt32(), bgra, leftHanded, size, x, y, z, budget);
            }
        }
    }

    /// <summary>
    /// The run-length stream: one slice per Z, each a sequence of values in which
    /// <see cref="CodeFlag"/> introduces a repeat count and
    /// <see cref="NextSliceFlag"/> ends the slice. Within a slice the index runs
    /// along X first, then Y — the same order the uncompressed form uses.
    /// </summary>
    private static void ReadCompressed(
        QbCursor cursor,
        Vector3I size,
        QbPalette palette,
        bool bgra,
        bool leftHanded,
        List<ImportedCell> cellsList,
        CellBudget budget)
    {
        long cells = (long)size.X * size.Y;

        for (var z = 0; z < size.Z; z++)
        {
            long index = 0;

            while (true)
            {
                uint value = cursor.UInt32();

                if (value == NextSliceFlag)
                    break;

                long count = 1;

                if (value == CodeFlag)
                {
                    count = cursor.UInt32();
                    value = cursor.UInt32();
                }

                if (index + count > cells)
                    throw Damaged("a compressed slice holds more cells than the matrix has room for");

                for (var i = 0; i < count; i++, index++)
                {
                    Add(
                        cellsList, palette, value, bgra, leftHanded, size,
                        (int)(index % size.X), (int)(index / size.X), z, budget);
                }
            }
        }
    }

    private static void Add(
        List<ImportedCell> cellsList,
        QbPalette palette,
        uint packed,
        bool bgra,
        bool leftHanded,
        Vector3I size,
        int x,
        int y,
        int z,
        CellBudget budget)
    {
        var alpha = (byte)((packed >> 24) & 0xFF);

        if (alpha == 0)
            return;

        budget.Take(".qb");

        var first = (byte)(packed & 0xFF);
        var second = (byte)((packed >> 8) & 0xFF);
        var third = (byte)((packed >> 16) & 0xFF);
        (byte red, byte green, byte blue) = bgra ? (third, second, first) : (first, second, third);

        cellsList.Add(new ImportedCell(
            new Vector3I(x, y, leftHanded ? size.Z - 1 - z : z),
            palette.IndexOf(red, green, blue)));
    }

    private static InvalidOperationException Damaged(string reason) =>
        new($"This .qb file cannot be read: {reason}.");

    /// <summary>
    /// How many cells the whole import has asked for so far, counted across every
    /// matrix and refused at the ceiling.
    /// </summary>
    private sealed class CellBudget
    {
        private long _taken;

        public void Take(string extension)
        {
            if (++_taken > ImportLimits.MaximumCells)
                throw new InvalidOperationException(ImportLimits.TooManyCells(extension, _taken));
        }
    }

    /// <summary>
    /// The colours a file wears, deduplicated exactly and named by their own hex.
    /// </summary>
    /// <remarks>
    /// Across the whole file rather than per matrix: a Qubicle model is one object
    /// split into named parts, and deduplicating per matrix would make seventeen
    /// copies of one skin tone. The name is the identity — a <c>.qb</c> colour has
    /// no index and no name of its own — and it is unique by construction, which
    /// matters because two materials sharing a name would make either of them
    /// unaddressable by name for the life of the document.
    /// </remarks>
    private sealed class QbPalette
    {
        private readonly Dictionary<int, int> _indexByColorDictionary = new();
        private readonly List<ImportedMaterial> _materialsList = new();

        public IReadOnlyList<ImportedMaterial> Materials => _materialsList;

        public int IndexOf(byte red, byte green, byte blue)
        {
            int key = (red << 16) | (green << 8) | blue;

            if (_indexByColorDictionary.TryGetValue(key, out int index))
                return index;

            index = _materialsList.Count;
            _indexByColorDictionary[key] = index;
            _materialsList.Add(new ImportedMaterial(
                $"#{key:X6}",
                new Color(red / 255f, green / 255f, blue / 255f),
                MaterialAppearance.Default));

            return index;
        }
    }

    /// <summary>
    /// A cursor that cannot read past the file. Every field is read through it, so
    /// a truncated file is a named refusal rather than whatever the arithmetic
    /// happens to throw.
    /// </summary>
    private sealed class QbCursor
    {
        private readonly byte[] _bytes;

        private int _at;

        public QbCursor(byte[] bytes)
        {
            _bytes = bytes;
        }

        public uint UInt32()
        {
            Require(4);

            uint value = BitConverter.ToUInt32(_bytes, _at);
            _at += 4;

            return value;
        }

        public int Int32()
        {
            Require(4);

            int value = BitConverter.ToInt32(_bytes, _at);
            _at += 4;

            return value;
        }

        /// <summary>A matrix dimension, refused where it could not be one.</summary>
        public int Size()
        {
            uint value = UInt32();

            return value is 0 or > int.MaxValue
                ? throw Damaged($"a matrix declares a side of {value} cells")
                : (int)value;
        }

        /// <summary>A length-prefixed name, one byte of length.</summary>
        public string Name()
        {
            Require(1);

            int length = _bytes[_at++];

            Require(length);

            string value = System.Text.Encoding.UTF8.GetString(_bytes, _at, length);
            _at += length;

            return value;
        }

        private void Require(int count)
        {
            if (count < 0 || _at + count > _bytes.Length)
                throw Damaged("it ends in the middle of a value");
        }
    }
}
