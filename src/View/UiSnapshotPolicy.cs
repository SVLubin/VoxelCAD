using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What belongs in a reading of the control tree, and how much of it one answer
/// carries. Pure, and out of the walker for the standing reason: the walk needs
/// the node tree, the decisions do not, and only the decisions are worth a test.
/// </summary>
internal static class UiSnapshotPolicy
{
    /// <summary>
    /// A cap on one answer, in controls. The whole editor chrome measures ~460
    /// with the hidden dialogs included — the panel rework alone added ~45 —
    /// so the old 500 sat one open colour picker away from silent truncation.
    /// Past the cap an answer stops being something to read and becomes
    /// something to window into, exactly as a slice does.
    /// </summary>
    public const int MaximumControls = 1200;

    /// <summary>Whether one control belongs in the answer the options asked for.</summary>
    public static bool ShouldReport(string reference, bool visible, UiSnapshotOptions options) =>
        (visible || options.IncludeInvisible)
        && UiReference.IsUnder(reference, options.Root)
        && UiReference.DepthUnder(reference, options.Root) <= options.MaxDepth;

    /// <summary>
    /// Whether one control answers the search. Its reference, its words and its
    /// type are all things a reader looks for, so any of the three is a match.
    /// </summary>
    /// <remarks>
    /// A search of its own rather than a clause of <see cref="ShouldReport"/>,
    /// because the two questions have different scopes: what is out of scope is
    /// not walked, and what merely fails the search still has to be walked
    /// through — nearly everything worth finding sits under containers that
    /// answer nothing.
    /// </remarks>
    public static bool MatchesSearch(
        string reference,
        string text,
        string type,
        UiSnapshotOptions options) =>
        options.Contains.Length == 0
        || reference.Contains(options.Contains, StringComparison.OrdinalIgnoreCase)
        || text.Contains(options.Contains, StringComparison.OrdinalIgnoreCase)
        || type.Contains(options.Contains, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a subtree is worth entering at all. A branch that is neither under
    /// the root nor on the way to it cannot hold anything the answer wants.
    /// </summary>
    public static bool ShouldDescend(string reference, UiSnapshotOptions options) =>
        options.Root.Length == 0
        || UiReference.IsUnder(reference, options.Root)
        || UiReference.IsUnder(options.Root, reference);
}
