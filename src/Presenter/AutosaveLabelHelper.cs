using System;
using System.Globalization;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Presenter;

/// <summary>
/// Every sentence the recovery feature says, as pure functions, so the card, the
/// status line and the reports cannot disagree about what a state is called and
/// the wording is held by a test. English literals, like every other
/// <see cref="ConfirmRequest"/> the shell composes.
/// </summary>
/// <remarks>
/// Times are absolute and local — "8 September 2026 at 12:03" — never relative
/// ages: a relative age needs a clock, and no clock reaches presenter logic.
/// </remarks>
internal static class AutosaveLabelHelper
{
    public const string RecoverLabel = "Recover";
    public const string DiscardLabel = "Discard";
    public const string LaterLabel = "Later";
    public const string ReviewLabel = "Review";
    public const string DiscardAllLabel = "Discard all";

    private const string OfferTitle = "Unsaved work found";
    private const string MenuRoute = "File → Recover unsaved work…";

    /// <summary>The card offered for one snapshot, with its place in the run when there are several.</summary>
    public static ConfirmRequest DescribeOffer(AutosaveEntry entry, int position, int count)
    {
        string title = count > 1 ? $"{OfferTitle} ({position} of {count})" : OfferTitle;
        string when = FormatWhen(entry.WrittenAt);
        string edits = FormatEdits(entry.Records);
        string kept = edits.Length > 0 ? $" — {edits} kept in a recovery snapshot" : string.Empty;
        string subject = entry.OriginalPath is null
            ? "An untitled document"
            : entry.Title;

        string first = entry.Original switch
        {
            RecoveryOriginal.Missing =>
                $"{subject} has unsaved changes from {when}{kept}. "
                + $"The file is no longer at {entry.OriginalPath}.",

            RecoveryOriginal.ChangedSinceSnapshot =>
                $"{subject} has unsaved changes from {when}{kept}, but the file itself has "
                + "been written since — it is not the one the snapshot was taken over. Recover "
                + "opens the snapshot as a new untitled document, so Save will ask where to put it.",

            _ => $"{subject} has unsaved changes from {when}{kept}."
        };

        string opens = entry.Original is RecoveryOriginal.Present or RecoveryOriginal.Missing
            ? "Recover opens it as an unsaved document under the same path."
            : "Recover opens it as an unsaved document.";

        string message = $"{first}\n\n{opens} Discard deletes the snapshot. Later keeps it and "
            + $"asks again next time — it is also under {MenuRoute}";

        return new ConfirmRequest(title, message, RecoverLabel, DiscardLabel, LaterLabel);
    }

    /// <summary>The one question asked first when there are more snapshots than a run of cards should be.</summary>
    public static ConfirmRequest DescribeSummary(int count) =>
        new(
            OfferTitle,
            $"{count} recovery snapshots are waiting from earlier sessions. Review offers them "
            + "one at a time, newest first. Discard all deletes every one of them. Later keeps them.",
            ReviewLabel,
            DiscardAllLabel,
            LaterLabel);

    /// <summary>The sentence <c>autosave-recover</c> answers with about the original file.</summary>
    public static string DescribeRecoveryNote(RecoveryOriginal original) =>
        original switch
        {
            RecoveryOriginal.Untitled =>
                "The document had never been saved; 'save' needs a path.",
            RecoveryOriginal.Present =>
                "Opened unsaved under the original path; 'save' writes it there.",
            RecoveryOriginal.Missing =>
                "The original file is no longer at its path; the path is carried, and a save "
                + "into a missing directory fails by name — pass another path to 'save'.",
            RecoveryOriginal.ChangedSinceSnapshot =>
                "The original file has been written since this snapshot was taken — its "
                + "modification stamp is not the one recorded — so it opened untitled: 'save' "
                + "needs a path rather than aiming at a file that moved on.",
            _ => throw new ArgumentOutOfRangeException(nameof(original), original, null)
        };

    /// <summary>The line on the Files page, and what <c>autosave-status</c> puts in words.</summary>
    public static string DescribeStatus(AutosaveStatus status, int pending)
    {
        string line;

        if (!status.Enabled)
            line = "Off.";
        else if (status.LastFailure is { } failure)
            line = $"Could not write a snapshot: {failure}. Trying again at the next interval.";
        else if (status.WriteInFlight)
            line = "Writing a snapshot.";
        else if (status.Deferred)
            line = "Waiting for the current gesture or dialog to end.";
        else if (status.HasSnapshot && status.LastWrittenAt is { } written && !status.PendingChanges)
        {
            line = $"Last snapshot at {FormatTime(written)}"
                + (status.LastWriteMilliseconds is { } milliseconds
                    ? $" ({Math.Round(milliseconds)} ms)."
                    : ".");
        }
        else if (!status.PendingChanges && !status.HasSnapshot)
            line = "Nothing to snapshot — the document matches its file.";
        else
            line = $"Next snapshot in {ElapsedTimeHelper.Format(status.SecondsUntilNextWrite)}.";

        if (pending > 0)
        {
            line += pending == 1
                ? $" 1 snapshot from an earlier session is waiting — {MenuRoute}."
                : $" {pending} snapshots from earlier sessions are waiting — {MenuRoute}.";
        }

        return line;
    }

    /// <summary>"8 September 2026 at 12:03", in local time.</summary>
    public static string FormatWhen(DateTime utc) =>
        ToLocal(utc).ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"12:03:07", in local time.</summary>
    public static string FormatTime(DateTime utc) =>
        ToLocal(utc).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>"1 edit", "17 edits", or nothing at all when the count is unknown.</summary>
    public static string FormatEdits(int records) =>
        records switch
        {
            <= 0 => string.Empty,
            1 => "1 edit",
            _ => $"{records} edits"
        };

    private static DateTime ToLocal(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}
