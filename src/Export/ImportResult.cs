using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// What an import left in the document: the element it put there, how much of it
/// arrived, and what the file said that this document could not hold.
/// </summary>
/// <param name="RootId">The one element the import created at the top level.</param>
/// <param name="Extent">
/// The box the imported cells occupy, in the scene's own cells and counted from
/// the origin. What a caller opening a fresh document sizes its region by.
/// </param>
/// <param name="Notes">
/// Losses, in the reader's own words. Empty when the file crossed whole.
/// </param>
public readonly record struct ImportResult(
    System.Guid RootId,
    string RootName,
    string Format,
    int Elements,
    int Cells,
    int Materials,
    Vector3I Extent,
    IReadOnlyList<string> Notes);
