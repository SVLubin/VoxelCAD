namespace VoxelCAD.CommonTypes;

/// <summary>
/// How much of the control tree to report. The whole editor is several hundred
/// controls, most of them containers nobody will ever aim at, so an answer is
/// windowed the way a slice is.
/// </summary>
/// <param name="Root">
/// A reference to start from; empty is the whole shell. Everything at or under it
/// is reported, and depth is counted from it rather than from the shell.
/// </param>
/// <param name="MaxDepth">How far below the root to go. Unbounded by default.</param>
/// <param name="IncludeInvisible">
/// Whether controls that are not on screen are reported. Off by default: an
/// invisible control cannot be clicked, so listing it is noise in the common case
/// and the answer to "why did nothing happen" in the rare one.
/// </param>
/// <param name="Contains">
/// Only the controls whose reference, words or type hold this, ignoring case;
/// empty is all of them. Unlike <paramref name="Root"/> it narrows what is
/// <em>reported</em> and never what is walked — a control worth finding is
/// usually under several containers that are not.
/// </param>
public readonly record struct UiSnapshotOptions(
    string Root,
    int MaxDepth,
    bool IncludeInvisible,
    string Contains = "");
