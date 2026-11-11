using System;
using System.Collections.Generic;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>Which snapshots the launch offers, in what order, and what it says about the original.</summary>
public sealed class RecoveryOfferTests
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OnlyRecoverableEntriesAreOfferedNewestFirst()
    {
        var entries = new List<AutosaveEntry>
        {
            Entry(Noon, AutosaveEntryState.Recoverable, "older"),
            Entry(Noon.AddMinutes(5), AutosaveEntryState.InUse, "held"),
            Entry(Noon.AddMinutes(10), AutosaveEntryState.Recoverable, "newest"),
            Entry(Noon.AddMinutes(1), AutosaveEntryState.Unreadable, "broken")
        };

        IReadOnlyList<AutosaveEntry> offered = RecoveryOffer.Order(entries);

        Assert.Collection(
            offered,
            entry => Assert.Equal("newest", entry.OriginalPath),
            entry => Assert.Equal("older", entry.OriginalPath));
    }

    [Fact]
    public void AnUntitledDocumentIsUntitled()
    {
        Assert.Equal(
            RecoveryOriginal.Untitled,
            RecoveryOffer.ClassifyOriginal(null, exists: false, modifiedAt: null, recordedModifiedAt: null));
        Assert.Equal(
            RecoveryOriginal.Untitled,
            RecoveryOffer.ClassifyOriginal(string.Empty, exists: true, Noon, Noon));
    }

    [Fact]
    public void AFileThatIsGoneIsMissing()
    {
        Assert.Equal(
            RecoveryOriginal.Missing,
            RecoveryOffer.ClassifyOriginal("/x/part.vcad", exists: false, modifiedAt: null, Noon));
    }

    [Fact]
    public void AFileWithTheStampTheSnapshotRecordedIsPresent()
    {
        Assert.Equal(
            RecoveryOriginal.Present,
            RecoveryOffer.ClassifyOriginal("/x/part.vcad", exists: true, Noon, Noon));
    }

    /// <summary>
    /// Any other stamp means the file was written since — later, or earlier, as a
    /// backup restored over it would be — and a save must ask where.
    /// </summary>
    [Fact]
    public void AFileWithAnyOtherStampHasChangedSince()
    {
        Assert.Equal(
            RecoveryOriginal.ChangedSinceSnapshot,
            RecoveryOffer.ClassifyOriginal("/x/part.vcad", exists: true, Noon.AddSeconds(30), Noon));
        Assert.Equal(
            RecoveryOriginal.ChangedSinceSnapshot,
            RecoveryOffer.ClassifyOriginal("/x/part.vcad", exists: true, Noon.AddSeconds(-30), Noon));
        Assert.Equal(
            RecoveryOriginal.ChangedSinceSnapshot,
            RecoveryOffer.ClassifyOriginal("/x/part.vcad", exists: true, Noon.AddTicks(1), Noon));
    }

    /// <summary>A file that was not there when the snapshot was taken and is now was written since.</summary>
    [Fact]
    public void AFileThatAppearedSinceHasChanged()
    {
        Assert.Equal(
            RecoveryOriginal.ChangedSinceSnapshot,
            RecoveryOffer.ClassifyOriginal("/x/part.vcad", exists: true, Noon, recordedModifiedAt: null));
    }

    [Fact]
    public void MoreThanThreeNeedsASummary()
    {
        Assert.False(RecoveryOffer.NeedsSummary(3));
        Assert.True(RecoveryOffer.NeedsSummary(4));
    }

    private static AutosaveEntry Entry(DateTime writtenAt, AutosaveEntryState state, string path) =>
        new(
            Guid.NewGuid(),
            "/store/x.vcad",
            path,
            writtenAt,
            1,
            RecoveryOriginal.Present,
            state,
            string.Empty);
}
