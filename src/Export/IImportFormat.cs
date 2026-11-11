namespace VoxelCAD.Export;

/// <summary>
/// A single input format. A reader sees bytes and produces the IR — never the
/// model — which is the other half of the projection's decoupling: adding a
/// format touches nothing but its own reader.
/// </summary>
internal interface IImportFormat
{
    /// <summary>The file extension it owns, lower-case and dotted, e.g. <c>.vox</c>.</summary>
    string Extension { get; }

    string Label { get; }

    /// <summary>
    /// Reads the whole file. A file that is damaged, truncated, or not this format
    /// at all is refused with <see cref="System.InvalidOperationException"/> naming
    /// the reason — never half read, because the caller has a document to protect
    /// and half an import is worse than none.
    /// </summary>
    /// <param name="name">
    /// What to call anything the file leaves unnamed — the file's own name without
    /// its extension. A reader has no business knowing where the bytes came from,
    /// so it is told rather than asked to work it out.
    /// </param>
    ImportedFile Read(byte[] bytes, string name);
}
