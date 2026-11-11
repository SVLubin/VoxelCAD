using System.Collections.Generic;

namespace VoxelCAD.Export;

/// <summary>
/// A file read into this document's own vocabulary, before anything of it has
/// been created. Everything a reader can refuse, it refuses while building one of
/// these, so applying it to a document cannot fail half way.
/// </summary>
/// <remarks>
/// The mirror of <see cref="ExportModel"/>, and it earns the symmetry the same
/// way: readers know their format and nothing about the document, one projection
/// knows the document and nothing about any format.
/// </remarks>
internal sealed class ImportedFile
{
    public ImportedFile(
        IReadOnlyList<ImportedNode> rootsList,
        IReadOnlyList<ImportedMaterial> materialsList,
        IReadOnlyList<string> notesList)
    {
        Roots = rootsList;
        Materials = materialsList;
        Notes = notesList;
    }

    /// <summary>
    /// What the file holds at its top level. Several roots become one grouping
    /// element named after the file; a single root stands on its own.
    /// </summary>
    public IReadOnlyList<ImportedNode> Roots { get; }

    /// <summary>
    /// The materials the cells wear, in the order <see cref="ImportedCell.PaletteIndex"/>
    /// names them. Only the ones something actually wears: a palette is not a
    /// scene, and a file's unused colours are nobody's decision.
    /// </summary>
    public IReadOnlyList<ImportedMaterial> Materials { get; }

    /// <summary>
    /// What the file said that this document cannot hold — an animation past its
    /// first frame, a material property with no counterpart. Reported rather than
    /// dropped in silence, because the alternative is a user comparing two windows
    /// and guessing.
    /// </summary>
    public IReadOnlyList<string> Notes { get; }
}
