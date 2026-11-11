using System;
using System.Collections.Generic;
using VoxelCAD.Model;

namespace VoxelCAD.Export;

/// <summary>
/// Reads somebody else's voxel file into this document. The mirror of
/// <see cref="IExporter"/>, and deliberately its shape: a path picks the format,
/// the reader produces the IR, and one projection turns the IR into elements.
/// </summary>
public interface IImporter
{
    /// <summary>Every format that can be read, for a dialog or an agent to list.</summary>
    IReadOnlyList<ImportFormatInfo> Formats { get; }

    /// <summary>
    /// Whether this path's extension names a readable format. Asked by a surface
    /// deciding what to offer — never by guessing at the extension itself.
    /// </summary>
    bool CanRead(string path);

    /// <summary>
    /// Reads <paramref name="path"/> and checks it over, without touching any
    /// document. Every way a file can be refused happens here.
    /// </summary>
    /// <exception cref="ArgumentException">The extension names no reader.</exception>
    /// <exception cref="InvalidOperationException">
    /// The file is damaged, is not what its extension claims, or asks for more
    /// than this document will hold.
    /// </exception>
    IImportPlan Read(string path);

    /// <summary>
    /// Puts a read file under <paramref name="parentId"/> as <b>one undoable
    /// record</b>, materials included.
    /// </summary>
    ImportResult Apply(IImportPlan plan, IProject project, IScene scene, Guid parentId);

    /// <summary>Both steps, for a caller that has a document already.</summary>
    ImportResult Import(IProject project, IScene scene, string path, Guid parentId);
}
