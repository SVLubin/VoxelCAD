using System.Collections.Generic;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// A file dialog as a presenter describes it. Everything here is data: which way
/// the dialog runs and what to call things, never how to draw it.
/// </summary>
/// <param name="Filters">
/// The globs the dialog offers, each with a label. They come from the layer that
/// knows what it can store or write — the model for a project, the export registry
/// for a model export — never from the shell guessing.
/// </param>
/// <param name="SuggestedPath">
/// Where to start, and what to prefill when saving. Empty when there is no
/// suggestion — a document that has never been written to disk.
/// </param>
public readonly record struct FileDialogRequest(
    FileDialogMode Mode,
    string Title,
    IReadOnlyList<FileDialogFilter> Filters,
    string SuggestedPath);
