using System.Collections.Generic;

namespace VoxelCAD.Export;

/// <summary>
/// The palette entries a file's cells actually wear, gathered as they turn up and
/// numbered densely.
/// </summary>
/// <remarks>
/// A palette is not a scene. MagicaVoxel stores 256 colours whether or not
/// anything wears them and writes a material chunk for every one, so importing the
/// palette wholesale would put 256 materials in the project tree for a model with
/// fourteen colours in it — measured on real files: fourteen for a tank, thirteen
/// for a sheet of unit sketches, a hundred and nineteen for a heavily painted MV
/// sample, never 256.
/// <para>
/// They come out in the order the cells first ask for them, which is the order the
/// parts of the model use them in. Each carries its own palette index in its name,
/// so nothing about where MagicaVoxel kept it is lost.
/// </para>
/// </remarks>
internal sealed class VoxPaletteMap
{
    private readonly VoxDocument _document;
    private readonly Dictionary<int, int> _indexByColorDictionary = new();
    private readonly List<ImportedMaterial> _materialsList = new();
    private readonly bool _fileHasMaterials;

    public VoxPaletteMap(VoxDocument document)
    {
        _document = document;

        // Whether the file describes its materials at all, which decides how the
        // colour's own alpha is read — see VoxMaterialReader.
        _fileHasMaterials = document.Materials.Count > 0;
    }

    public IReadOnlyList<ImportedMaterial> Materials => _materialsList;

    /// <summary>
    /// Where this colour index sits among the materials the import will create,
    /// adding it the first time anything wears it.
    /// </summary>
    public int IndexOf(int colorIndex)
    {
        if (_indexByColorDictionary.TryGetValue(colorIndex, out int index))
            return index;

        index = _materialsList.Count;
        _indexByColorDictionary[colorIndex] = index;
        _materialsList.Add(VoxMaterialReader.Read(
            colorIndex,
            Entry(colorIndex),
            _document.Materials.TryGetValue(colorIndex, out IReadOnlyDictionary<string, string>? properties)
                ? properties
                : null,
            _fileHasMaterials));

        return index;
    }

    /// <summary>
    /// The four bytes of a colour index. The chunk is a <em>list</em> and the
    /// index is one-based over it — the format's own note says colours 0..254 map
    /// to indices 1..255 — so entry <c>c</c> lives at <c>RGBA[c - 1]</c>, which is
    /// exactly where this editor's writer puts it.
    /// </summary>
    private (byte R, byte G, byte B, byte A) Entry(int colorIndex)
    {
        if (_document.Palette == null)
            return VoxDefaultPalette.Entry(colorIndex);

        var offset = (colorIndex - 1) * 4;

        if (offset < 0 || offset + 4 > _document.PaletteBytes)
            return VoxDefaultPalette.Entry(colorIndex);

        return (
            _document.Palette[offset],
            _document.Palette[offset + 1],
            _document.Palette[offset + 2],
            _document.Palette[offset + 3]);
    }
}
