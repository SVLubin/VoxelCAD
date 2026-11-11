using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// A file read and found sound, waiting to be put into a document. Everything
/// that could refuse the file has already happened.
/// </summary>
/// <remarks>
/// Reading and applying are two steps because two callers need them apart.
/// Opening a file as a new document has to size the room before there is a scene
/// to import into, and it cannot size it without knowing how big the model is; and
/// separating them is what makes the safety property visible in the types — by the
/// time a document is touched, nothing is left that can fail.
/// </remarks>
public interface IImportPlan
{
    /// <summary>The file's own name, without directory or extension.</summary>
    string Name { get; }

    /// <summary>The extension whose reader produced this.</summary>
    string Format { get; }

    /// <summary>
    /// The box the cells will occupy, counted from the origin — an import lands
    /// with its own corner there.
    /// </summary>
    Vector3I Extent { get; }

    /// <summary>What the file said that this document cannot hold.</summary>
    IReadOnlyList<string> Notes { get; }
}
