using System;
using System.Collections.Generic;

namespace VoxelCAD.Presenter;

/// <summary>
/// Which snapshots are offered, in what order, what state an original is in, and
/// whether there are enough to ask a summary question first. Pure, so the launch
/// card's decisions are held by a test rather than by a launch.
/// </summary>
internal static class RecoveryOffer
{
    /// <summary>More pending snapshots than this and the launch asks one summary question first.</summary>
    public const int SummaryThreshold = 3;

    /// <summary>Recoverable entries only, newest first. In-use and unreadable ones are never offered.</summary>
    public static IReadOnlyList<AutosaveEntry> Order(IReadOnlyList<AutosaveEntry> entries)
    {
        var orderedList = new List<AutosaveEntry>();

        foreach (AutosaveEntry entry in entries)
        {
            if (entry.IsRecoverable)
                orderedList.Add(entry);
        }

        orderedList.Sort((left, right) => right.WrittenAt.CompareTo(left.WrittenAt));

        return orderedList;
    }

    /// <summary>
    /// What the original is now, against what the snapshot recorded of it. An
    /// exact comparison of modification times rather than "newer than the
    /// snapshot" with a tolerance: the snapshot wrote down the file's own stamp
    /// at the moment it was taken, so any other stamp means the file was written
    /// since — whichever way the clocks and the file system's granularity fall.
    /// A file that did not exist when the snapshot was taken and does now was
    /// written since as well.
    /// </summary>
    /// <param name="modifiedAt">The file's stamp now, when it exists.</param>
    /// <param name="recordedModifiedAt">The stamp the sidecar recorded; null when the file did not exist then.</param>
    public static RecoveryOriginal ClassifyOriginal(
        string? path,
        bool exists,
        DateTime? modifiedAt,
        DateTime? recordedModifiedAt)
    {
        if (string.IsNullOrEmpty(path))
            return RecoveryOriginal.Untitled;

        if (!exists || modifiedAt is not { } modified)
            return RecoveryOriginal.Missing;

        return recordedModifiedAt is { } recorded && recorded.Ticks == modified.Ticks
            ? RecoveryOriginal.Present
            : RecoveryOriginal.ChangedSinceSnapshot;
    }

    public static bool NeedsSummary(int count) => count > SummaryThreshold;
}
