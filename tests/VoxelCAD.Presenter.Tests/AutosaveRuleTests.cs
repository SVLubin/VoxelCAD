using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The trigger as a table: the three rules the user set — an interval, only
/// when something changed, never mid-gesture — and the two that follow from
/// them, each a row rather than a timer.
/// </summary>
public sealed class AutosaveRuleTests
{
    [Fact]
    public void DisabledRearmsAndWritesNothing()
    {
        Assert.Equal(
            AutosaveAction.Rearm,
            AutosaveRule.Decide(
                enabled: false, due: true, isModified: true, recordMoved: true,
                blocked: false, overdue: true, hasSnapshot: true));
    }

    [Fact]
    public void ANotDueIntervalWaits()
    {
        Assert.Equal(
            AutosaveAction.Wait,
            AutosaveRule.Decide(
                enabled: true, due: false, isModified: true, recordMoved: true,
                blocked: false, overdue: false, hasSnapshot: false));
    }

    /// <summary>
    /// The document equals its file — undone back to the saved point — so a
    /// snapshot would only put undone work back.
    /// </summary>
    [Fact]
    public void UnmodifiedWithASnapshotDrops()
    {
        Assert.Equal(
            AutosaveAction.Drop,
            AutosaveRule.Decide(
                enabled: true, due: true, isModified: false, recordMoved: true,
                blocked: false, overdue: false, hasSnapshot: true));
    }

    [Fact]
    public void UnmodifiedWithoutOneRearms()
    {
        Assert.Equal(
            AutosaveAction.Rearm,
            AutosaveRule.Decide(
                enabled: true, due: true, isModified: false, recordMoved: false,
                blocked: false, overdue: false, hasSnapshot: false));
    }

    /// <summary>An undo and a redo landing on the snapshot's own record is not a move.</summary>
    [Fact]
    public void ModifiedButUnmovedRearms()
    {
        Assert.Equal(
            AutosaveAction.Rearm,
            AutosaveRule.Decide(
                enabled: true, due: true, isModified: true, recordMoved: false,
                blocked: false, overdue: false, hasSnapshot: true));
    }

    [Fact]
    public void AGestureInFlightWaits()
    {
        Assert.Equal(
            AutosaveAction.Wait,
            AutosaveRule.Decide(
                enabled: true, due: true, isModified: true, recordMoved: true,
                blocked: true, overdue: false, hasSnapshot: false));
    }

    /// <summary>
    /// The deferral is bounded: a picker left open for an hour must not leave an
    /// hour unprotected, and every edit already went through the model.
    /// </summary>
    [Fact]
    public void AnOverdueWriteGoesThroughAGesture()
    {
        Assert.Equal(
            AutosaveAction.Write,
            AutosaveRule.Decide(
                enabled: true, due: true, isModified: true, recordMoved: true,
                blocked: true, overdue: true, hasSnapshot: false));
    }

    [Fact]
    public void MovedAndFreeWrites()
    {
        Assert.Equal(
            AutosaveAction.Write,
            AutosaveRule.Decide(
                enabled: true, due: true, isModified: true, recordMoved: true,
                blocked: false, overdue: false, hasSnapshot: true));
    }

    /// <summary>Not modified wins over blocked: there is nothing a gesture could be protecting.</summary>
    [Fact]
    public void AnUnmodifiedDocumentDropsEvenMidGesture()
    {
        Assert.Equal(
            AutosaveAction.Drop,
            AutosaveRule.Decide(
                enabled: true, due: true, isModified: false, recordMoved: true,
                blocked: true, overdue: false, hasSnapshot: true));
    }
}
