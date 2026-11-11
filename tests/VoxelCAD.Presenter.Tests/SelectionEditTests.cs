using System;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// What a click does to the selection. One function answers it for every surface
/// that can change one, so this is where the modifiers' meaning is pinned down —
/// the viewport and the project tree only report which rows a gesture named.
/// </summary>
public sealed class SelectionEditTests
{
    private static readonly SelectionTarget First = Element();
    private static readonly SelectionTarget Second = Element();
    private static readonly SelectionTarget Third = Element();

    [Fact]
    public void APlainClickIsTheWholeSelection()
    {
        SelectionState selection = Apply(
            State(First, Second), SelectionIntent.Replace, Third);

        Assert.Equal(new[] { Third }, selection.Targets);
        Assert.Equal(Third, selection.Active);
    }

    [Fact]
    public void ToggleAddsWhatIsNotInAndMakesItActive()
    {
        SelectionState selection = Apply(State(First), SelectionIntent.Toggle, Second);

        Assert.Equal(new[] { First, Second }, selection.Targets);
        Assert.Equal(Second, selection.Active);
    }

    [Fact]
    public void ToggleTakesOutWhatIsIn()
    {
        SelectionState selection = Apply(
            State(First, Second), SelectionIntent.Toggle, Second);

        Assert.Equal(new[] { First }, selection.Targets);
    }

    /// <summary>
    /// Toggling the active target out has to hand the role on, or the inspector and
    /// the handles would be left naming something no longer selected.
    /// </summary>
    [Fact]
    public void ToggleHandsTheActiveRoleOnWhenItRemovesIt()
    {
        SelectionState selection = Apply(
            State(First, Second), SelectionIntent.Toggle, Second);

        Assert.Equal(First, selection.Active);
    }

    [Fact]
    public void TheLastOneOutLeavesNothingSelected()
    {
        SelectionState selection = Apply(State(First), SelectionIntent.Toggle, First);

        Assert.Empty(selection.Targets);
        Assert.Equal(default, selection.Active);
    }

    /// <summary>
    /// Extend widens and never narrows — that is the whole of the difference
    /// between it and a toggle, and it is what lets a Shift range be dragged back
    /// and forth without punching holes in itself.
    /// </summary>
    [Fact]
    public void ExtendNeverTakesAnythingOut()
    {
        SelectionState selection = Apply(
            State(First, Second), SelectionIntent.Extend, Second);

        Assert.Equal(new[] { First, Second }, selection.Targets);
        Assert.Equal(Second, selection.Active);
    }

    [Fact]
    public void ExtendAddsTheWholeRunItWasGiven()
    {
        SelectionState selection = SelectionEdit.Apply(
            State(First), new[] { First, Second, Third }, Third, SelectionIntent.Extend);

        Assert.Equal(new[] { First, Second, Third }, selection.Targets);
        Assert.Equal(Third, selection.Active);
    }

    /// <summary>
    /// A modifier that named nothing leaves the selection alone: the hand was
    /// adding to it and missed, which is not the same sentence as "select nothing".
    /// </summary>
    [Theory]
    [InlineData(SelectionIntent.Toggle)]
    [InlineData(SelectionIntent.Extend)]
    public void AModifierThatNamedNothingChangesNothing(SelectionIntent intent)
    {
        SelectionState current = State(First, Second);

        SelectionState selection = SelectionEdit.Apply(
            current, Array.Empty<SelectionTarget>(), default, intent);

        Assert.Equal(current.Targets, selection.Targets);
        Assert.Equal(current.Active, selection.Active);
    }

    [Fact]
    public void APlainClickOnNothingClearsIt()
    {
        SelectionState selection = SelectionEdit.Apply(
            State(First), Array.Empty<SelectionTarget>(), default, SelectionIntent.Replace);

        Assert.Empty(selection.Targets);
    }

    /// <summary>
    /// A selection holds one kind. Nothing downstream — the inspector, the handles,
    /// the bin — has a meaning for an element and a material held at once, so a
    /// modifier reaching across kinds replaces rather than mixes.
    /// </summary>
    [Theory]
    [InlineData(SelectionIntent.Toggle)]
    [InlineData(SelectionIntent.Extend)]
    public void AModifierAcrossKindsReplacesRatherThanMixes(SelectionIntent intent)
    {
        var material = new SelectionTarget(SelectionKind.Material, Guid.NewGuid());

        SelectionState selection = Apply(State(First, Second), intent, material);

        Assert.Equal(new[] { material }, selection.Targets);
    }

    /// <summary>
    /// And a run that genuinely crossed a category — a Shift drag from a scene row
    /// down past the materials — keeps the kind of the row actually clicked.
    /// </summary>
    [Fact]
    public void ARunThatCrossedKindsKeepsTheClickedOnes()
    {
        var scene = new SelectionTarget(SelectionKind.Scene, Guid.NewGuid());
        var material = new SelectionTarget(SelectionKind.Material, Guid.NewGuid());
        var other = new SelectionTarget(SelectionKind.Material, Guid.NewGuid());

        SelectionState selection = SelectionEdit.Apply(
            SelectionState.Empty,
            new[] { scene, material, other },
            other,
            SelectionIntent.Extend);

        Assert.Equal(new[] { material, other }, selection.Targets);
        Assert.Equal(other, selection.Active);
    }

    [Fact]
    public void TheSameTargetTwiceInARunCountsOnce()
    {
        SelectionState selection = SelectionEdit.Apply(
            SelectionState.Empty,
            new[] { First, Second, First },
            Second,
            SelectionIntent.Replace);

        Assert.Equal(new[] { First, Second }, selection.Targets);
    }

    /// <summary>
    /// What the shell does when something selected is deleted or undone away: what
    /// went is dropped and the rest stands.
    /// </summary>
    [Fact]
    public void RemovingOneLeavesTheRest()
    {
        SelectionState selection = SelectionEdit.Remove(State(First, Second, Third), Second);

        Assert.Equal(new[] { First, Third }, selection.Targets);
        Assert.Equal(Third, selection.Active);
    }

    [Fact]
    public void RemovingTheActiveOneHandsTheRoleOn()
    {
        SelectionState selection = SelectionEdit.Remove(State(First, Second), Second);

        Assert.Equal(First, selection.Active);
    }

    [Fact]
    public void RemovingSomethingThatWasNotInChangesNothing()
    {
        SelectionState current = State(First);

        Assert.Equal(current.Targets, SelectionEdit.Remove(current, Second).Targets);
    }

    private static SelectionState Apply(
        SelectionState current, SelectionIntent intent, SelectionTarget target) =>
        SelectionEdit.Apply(current, new[] { target }, target, intent);

    /// <summary>The last one is the active one, which is what a click leaves behind.</summary>
    private static SelectionState State(params SelectionTarget[] targets) =>
        new(targets, targets[^1]);

    private static SelectionTarget Element() =>
        new(SelectionKind.Element, Guid.NewGuid());
}
