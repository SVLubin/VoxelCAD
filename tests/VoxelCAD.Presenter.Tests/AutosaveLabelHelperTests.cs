using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The words the recovery feature says. Held here so the card, the status line
/// and the reports cannot come to call one state two things.
/// </summary>
public sealed class AutosaveLabelHelperTests
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 3, 7, DateTimeKind.Utc);

    [Fact]
    public void AnOfferForAPresentFileNamesItTheTimeAndTheEdits()
    {
        ConfirmRequest request = AutosaveLabelHelper.DescribeOffer(
            Entry("/models/part.vcad", RecoveryOriginal.Present, 17), 1, 1);

        Assert.Equal("Unsaved work found", request.Title);
        Assert.StartsWith("part.vcad has unsaved changes from ", request.Message);
        Assert.Contains("17 edits kept in a recovery snapshot", request.Message);
        Assert.Contains("under the same path", request.Message);
        Assert.Contains("File → Recover unsaved work…", request.Message);
        Assert.Equal("Recover", request.ConfirmLabel);
        Assert.Equal("Discard", request.DenyLabel);
        Assert.Equal("Later", request.CancelLabel);
    }

    [Fact]
    public void AnOfferForAnUntitledDocumentSaysSoAndSkipsThePath()
    {
        ConfirmRequest request = AutosaveLabelHelper.DescribeOffer(
            Entry(null, RecoveryOriginal.Untitled, 1), 1, 1);

        Assert.StartsWith("An untitled document has unsaved changes", request.Message);
        Assert.Contains("1 edit kept", request.Message);
        Assert.DoesNotContain("same path", request.Message);
    }

    [Fact]
    public void AnOfferForAMissingFileNamesWhereItWas()
    {
        ConfirmRequest request = AutosaveLabelHelper.DescribeOffer(
            Entry("/gone/part.vcad", RecoveryOriginal.Missing, 3), 2, 3);

        Assert.Equal("Unsaved work found (2 of 3)", request.Title);
        Assert.Contains("no longer at /gone/part.vcad", request.Message);
    }

    [Fact]
    public void AnOfferOverANewerFileSaysSaveWillAsk()
    {
        ConfirmRequest request = AutosaveLabelHelper.DescribeOffer(
            Entry("/models/part.vcad", RecoveryOriginal.ChangedSinceSnapshot, 0), 1, 1);

        Assert.Contains("has been written since", request.Message);
        Assert.Contains("untitled document", request.Message);
        Assert.DoesNotContain("kept in a recovery snapshot", request.Message);
    }

    [Fact]
    public void TheSummaryCountsAndOffersReviewDiscardAllLater()
    {
        ConfirmRequest request = AutosaveLabelHelper.DescribeSummary(5);

        Assert.Contains("5 recovery snapshots", request.Message);
        Assert.Equal("Review", request.ConfirmLabel);
        Assert.Equal("Discard all", request.DenyLabel);
        Assert.Equal("Later", request.CancelLabel);
    }

    [Fact]
    public void EveryOriginalStateHasANote()
    {
        foreach (RecoveryOriginal original in Enum.GetValues<RecoveryOriginal>())
            Assert.NotEmpty(AutosaveLabelHelper.DescribeRecoveryNote(original));
    }

    [Fact]
    public void TheStatusLineSaysWhatIsGoingOn()
    {
        AutosaveStatus quiet = Status();

        Assert.Equal("Off.", AutosaveLabelHelper.DescribeStatus(quiet with { Enabled = false }, 0));
        Assert.Equal(
            "Nothing to snapshot — the document matches its file.",
            AutosaveLabelHelper.DescribeStatus(quiet, 0));
        Assert.Equal(
            "Next snapshot in 1:40.",
            AutosaveLabelHelper.DescribeStatus(
                quiet with { PendingChanges = true, SecondsUntilNextWrite = 100.0 }, 0));
        Assert.Equal(
            "Waiting for the current gesture or dialog to end.",
            AutosaveLabelHelper.DescribeStatus(quiet with { PendingChanges = true, Deferred = true }, 0));

        // A snapshot that was dropped is not "the last snapshot": the sentence
        // follows what is on disk, not what once was.
        Assert.Equal(
            "Nothing to snapshot — the document matches its file.",
            AutosaveLabelHelper.DescribeStatus(quiet with { LastWrittenAt = Noon }, 0));
        Assert.Equal(
            "Writing a snapshot.",
            AutosaveLabelHelper.DescribeStatus(quiet with { WriteInFlight = true }, 0));
        Assert.StartsWith(
            "Last snapshot at ",
            AutosaveLabelHelper.DescribeStatus(
                quiet with { HasSnapshot = true, LastWrittenAt = Noon, LastWriteMilliseconds = 41.4 }, 0));
        Assert.EndsWith(
            "(41 ms).",
            AutosaveLabelHelper.DescribeStatus(
                quiet with { HasSnapshot = true, LastWrittenAt = Noon, LastWriteMilliseconds = 41.4 }, 0));
        Assert.Equal(
            "Could not write a snapshot: disk full. Trying again at the next interval.",
            AutosaveLabelHelper.DescribeStatus(quiet with { LastFailure = "disk full" }, 0));

        // A recovered document's snapshot was written by an earlier session: a
        // time and no cost, rather than a countdown to a write nothing asks for.
        Assert.Equal(
            $"Last snapshot at {AutosaveLabelHelper.FormatTime(Noon)}.",
            AutosaveLabelHelper.DescribeStatus(quiet with { HasSnapshot = true, LastWrittenAt = Noon }, 0));
    }

    [Fact]
    public void PendingSnapshotsAreAppendedWithTheirRoute()
    {
        string one = AutosaveLabelHelper.DescribeStatus(Status(), 1);
        string two = AutosaveLabelHelper.DescribeStatus(Status(), 2);

        Assert.EndsWith(
            " 1 snapshot from an earlier session is waiting — File → Recover unsaved work….", one);
        Assert.EndsWith(
            " 2 snapshots from earlier sessions are waiting — File → Recover unsaved work….", two);
    }

    /// <summary>Absolute and local: no clock reaches presenter logic, so no "3 minutes ago".</summary>
    [Fact]
    public void TimesAreAbsoluteAndLocal()
    {
        DateTime local = Noon.ToLocalTime();

        Assert.Equal(
            local.ToString("d MMMM yyyy 'at' HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            AutosaveLabelHelper.FormatWhen(Noon));
        Assert.Equal(
            local.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            AutosaveLabelHelper.FormatTime(Noon));
    }

    [Fact]
    public void EditsAreCountedInWordsAndUnknownIsSilent()
    {
        Assert.Equal(string.Empty, AutosaveLabelHelper.FormatEdits(0));
        Assert.Equal("1 edit", AutosaveLabelHelper.FormatEdits(1));
        Assert.Equal("17 edits", AutosaveLabelHelper.FormatEdits(17));
    }

    private static AutosaveEntry Entry(string? path, RecoveryOriginal original, int records) =>
        new(Guid.NewGuid(), "/store/x.vcad", path, Noon, records, original, AutosaveEntryState.Recoverable, string.Empty);

    private static AutosaveStatus Status() =>
        new(
            Enabled: true,
            IntervalSeconds: 120.0,
            Directory: "/store",
            Title: "Untitled",
            OriginalPath: null,
            SnapshotId: Guid.NewGuid(),
            HasSnapshot: false,
            PendingChanges: false,
            Deferred: false,
            WriteInFlight: false,
            SecondsUntilNextWrite: 120.0,
            LastWrittenAt: null,
            LastWriteMilliseconds: null,
            LastFailure: null,
            SessionOrphans: 0);
}
