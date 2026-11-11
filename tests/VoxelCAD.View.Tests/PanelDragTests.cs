using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Dragging a tab, in the three parts a node cannot hold a test to: what
/// separates a click from a drag, where a pointer says the tab would land, and
/// what a drop does to the stored placements.
/// </summary>
public sealed class PanelDragTests
{
    private static readonly IReadOnlyList<PanelPlacement> Four = new[]
    {
        new PanelPlacement(PanelKind.Toolbar, PanelSide.Left, 0, Pinned: false, Folded: false),
        new PanelPlacement(PanelKind.Project, PanelSide.Left, 1, Pinned: false, Folded: false),
        new PanelPlacement(PanelKind.History, PanelSide.Left, 2, Pinned: false, Folded: false),
        new PanelPlacement(PanelKind.Inspector, PanelSide.Right, 0, Pinned: false, Folded: false)
    };

    [Fact]
    public void APressThatDoesNotTravelIsAClick()
    {
        var drag = new PanelDragState();

        drag.Press(PanelKind.Project, new Vector2(50f, 100f));
        drag.MoveTo(new Vector2(51f, 101f));

        Assert.False(drag.IsDragging);
        Assert.Equal(PanelDragOutcome.Click, drag.Release());
    }

    [Fact]
    public void APressThatTravelsIsADrag()
    {
        var drag = new PanelDragState();

        drag.Press(PanelKind.Project, new Vector2(50f, 100f));
        drag.MoveTo(new Vector2(50f, 100f + PanelDragState.Threshold + 1f));

        Assert.True(drag.IsDragging);
        Assert.Equal(PanelDragOutcome.Drop, drag.Release());
    }

    /// <summary>
    /// Once it is a drag it stays one, even brought all the way home. A gesture
    /// that changed its mind about what it was would drop a panel *and* toggle
    /// it, or neither, depending on where the hand happened to stop.
    /// </summary>
    [Fact]
    public void ADragBroughtBackIsStillADrag()
    {
        var drag = new PanelDragState();

        drag.Press(PanelKind.Project, new Vector2(50f, 100f));
        drag.MoveTo(new Vector2(200f, 400f));
        drag.MoveTo(new Vector2(50f, 100f));

        Assert.True(drag.IsDragging);
        Assert.Equal(PanelDragOutcome.Drop, drag.Release());
    }

    [Fact]
    public void ReleasingNothingIsNothing()
    {
        var drag = new PanelDragState();

        Assert.Equal(PanelDragOutcome.None, drag.Release());

        drag.Press(PanelKind.Project, Vector2.Zero);
        drag.Cancel();

        Assert.Null(drag.Held);
        Assert.Equal(PanelDragOutcome.None, drag.Release());
    }

    private static readonly PanelColumnDrop Empty =
        new(float.MaxValue, float.MinValue, new float[0], new float[0]);

    /// <summary>A stack of two, 90 units each from y = 4, then a run below it.</summary>
    private static readonly PanelColumnDrop Stacked =
        new(4f, 190f, new[] { 49f, 145f }, new[] { 211f, 247f });

    [Theory]
    [InlineData(100f, PanelSide.Left)]
    [InlineData(575f, PanelSide.Left)]
    [InlineData(577f, PanelSide.Right)]
    [InlineData(1100f, PanelSide.Right)]
    public void TheSideIsWhicheverHalfTheHandIsIn(float pointerX, PanelSide expected)
    {
        PanelDropTarget target = PanelDropTargets.Solve(
            pointerX, 0f, 1152f, Empty, Empty);

        Assert.Equal(expected, target.Side);
    }

    /// <summary>
    /// What a panel is follows where it is put. Between the pinned ones is
    /// pinned; anywhere else is the run — and carried back out it is a tab
    /// again, because nothing is decided until the hand lets go.
    /// </summary>
    [Theory]
    [InlineData(20f, true, 0)]
    [InlineData(100f, true, 1)]
    [InlineData(180f, true, 2)]
    [InlineData(200f, false, 0)]
    [InlineData(400f, false, 2)]
    public void WhichBlockTheHandIsOverDecidesWhatItBecomes(
        float pointerY, bool pinned, int index)
    {
        PanelDropTarget target = PanelDropTargets.Solve(
            100f, pointerY, 1152f, Stacked, Empty);

        Assert.Equal(pinned, target.Pinned);
        Assert.Equal(index, target.Index);
    }

    /// <summary>A column with nothing pinned in it can never pin by accident.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(2f)]
    [InlineData(500f)]
    public void ARailCannotPin(float pointerY) =>
        Assert.False(PanelDropTargets.Solve(100f, pointerY, 1152f, Empty, Empty).Pinned);

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(19f, 0)]
    [InlineData(21f, 1)]
    [InlineData(55f, 1)]
    [InlineData(57f, 2)]
    [InlineData(500f, 3)]
    public void TheIndexIsHowManyTabsTheHandHasPassed(float pointerY, int expected) =>
        Assert.Equal(expected, PanelDropTargets.IndexFor(pointerY, new[] { 20f, 56f, 92f }));

    /// <summary>
    /// A drag moves a panel, it does not edit it — and the dragged height is
    /// part of what it is. The edits copy the found placement, so this holds
    /// the fresh-Slot lesson at the storage: a rebuild that constructed new
    /// records would drop the field silently.
    /// </summary>
    [Fact]
    public void AMoveAndACrossBothKeepTheDraggedHeight()
    {
        IReadOnlyList<PanelPlacement> placements = new[]
        {
            new PanelPlacement(
                PanelKind.Project, PanelSide.Left, 0, Pinned: true, Folded: false,
                Height: 240.0),
            new PanelPlacement(PanelKind.History, PanelSide.Left, 1, Pinned: true, Folded: false)
        };

        IReadOnlyList<PanelPlacement> moved = PanelPlacementEdit.Move(
            placements, PanelKind.Project, PanelSide.Right, pinned: false, index: 0);

        Assert.Equal(
            240.0,
            Assert.Single(moved, placement => placement.Kind == PanelKind.Project).Height);

        IReadOnlyList<PanelPlacement> crossed =
            PanelPlacementEdit.Cross(moved, PanelKind.Project);

        Assert.Equal(
            240.0,
            Assert.Single(crossed, placement => placement.Kind == PanelKind.Project).Height);
    }

    [Fact]
    public void MovingATabToTheOtherSideRenumbersBothSides()
    {
        IReadOnlyList<PanelPlacement> moved =
            PanelPlacementEdit.Move(Four, PanelKind.Project, PanelSide.Right, false, 0);

        Assert.Equal(PanelSide.Right, Placement(moved, PanelKind.Project).Side);
        Assert.Equal(0, Placement(moved, PanelKind.Project).Order);
        Assert.Equal(1, Placement(moved, PanelKind.Inspector).Order);

        // The side it left closes ranks rather than keeping a hole where it was.
        Assert.Equal(0, Placement(moved, PanelKind.Toolbar).Order);
        Assert.Equal(1, Placement(moved, PanelKind.History).Order);
    }

    [Fact]
    public void ReorderingWithinASideIsTheSameMove()
    {
        IReadOnlyList<PanelPlacement> moved =
            PanelPlacementEdit.Move(Four, PanelKind.Toolbar, PanelSide.Left, false, 2);

        Assert.Equal(0, Placement(moved, PanelKind.Project).Order);
        Assert.Equal(1, Placement(moved, PanelKind.History).Order);
        Assert.Equal(2, Placement(moved, PanelKind.Toolbar).Order);
    }

    /// <summary>
    /// A column stacks its pinned panels above the run, so an index counted out
    /// of the run has to land after them — dropped at the top of the tabs, a
    /// panel must not come out above a pinned one.
    /// </summary>
    [Fact]
    public void ADropIntoTheRunLandsBelowThePinnedStack()
    {
        var withPinned = Four
            .Select(placement => placement.Kind == PanelKind.Toolbar
                ? placement with { Pinned = true }
                : placement)
            .ToList();

        IReadOnlyList<PanelPlacement> moved =
            PanelPlacementEdit.Move(withPinned, PanelKind.Inspector, PanelSide.Left, false, 0);

        Assert.True(Placement(moved, PanelKind.Toolbar).Pinned);
        Assert.Equal(0, Placement(moved, PanelKind.Toolbar).Order);
        Assert.Equal(1, Placement(moved, PanelKind.Inspector).Order);
        Assert.False(Placement(moved, PanelKind.Inspector).Pinned);
    }

    /// <summary>
    /// Dropped into the stack, a tab becomes pinned and joins it — and the drop
    /// stores exactly what the preview has been drawing for the whole gesture.
    /// </summary>
    [Fact]
    public void ADropIntoTheStackPinsIt()
    {
        var withPinned = Four
            .Select(placement => placement.Kind == PanelKind.Toolbar
                ? placement with { Pinned = true }
                : placement)
            .ToList();

        IReadOnlyList<PanelPlacement> moved = PanelPlacementEdit.Move(
            withPinned, PanelKind.Inspector, PanelSide.Left, pinned: true, index: 0);

        Assert.True(Placement(moved, PanelKind.Inspector).Pinned);
        Assert.Equal(0, Placement(moved, PanelKind.Inspector).Order);
        Assert.Equal(1, Placement(moved, PanelKind.Toolbar).Order);
        Assert.Equal(2, Placement(moved, PanelKind.Project).Order);
    }

    /// <summary>And a pinned panel dropped into the run becomes a tab.</summary>
    [Fact]
    public void ADropIntoTheRunUnpinsIt()
    {
        var withPinned = Four
            .Select(placement => placement.Kind == PanelKind.Toolbar
                ? placement with { Pinned = true }
                : placement)
            .ToList();

        IReadOnlyList<PanelPlacement> moved = PanelPlacementEdit.Move(
            withPinned, PanelKind.Toolbar, PanelSide.Left, pinned: false, index: 1);

        Assert.False(Placement(moved, PanelKind.Toolbar).Pinned);
        Assert.Equal(0, Placement(moved, PanelKind.Project).Order);
        Assert.Equal(1, Placement(moved, PanelKind.Toolbar).Order);
    }

    /// <summary>An index past the end of the run is the end of the run.</summary>
    [Fact]
    public void AnIndexBeyondTheRunIsClamped()
    {
        IReadOnlyList<PanelPlacement> moved =
            PanelPlacementEdit.Move(Four, PanelKind.Inspector, PanelSide.Left, false, 99);

        Assert.Equal(PanelSide.Left, Placement(moved, PanelKind.Inspector).Side);
        Assert.Equal(3, Placement(moved, PanelKind.Inspector).Order);
    }

    [Fact]
    public void EverySideKeepsDenseOrdersWhateverTheMove()
    {
        IReadOnlyList<PanelPlacement> moved = Four;

        foreach (PanelKind kind in new[]
                 { PanelKind.Inspector, PanelKind.Toolbar, PanelKind.History })
        {
            moved = PanelPlacementEdit.Move(moved, kind, PanelSide.Right, false, 1);
        }

        foreach (PanelSide side in new[] { PanelSide.Left, PanelSide.Right })
        {
            var orders = moved
                .Where(placement => placement.Side == side)
                .Select(placement => placement.Order)
                .OrderBy(order => order)
                .ToList();

            Assert.Equal(Enumerable.Range(0, orders.Count), orders);
        }
    }

    /// <summary>
    /// Unpinning lands the panel at the head of the run, which is the place it
    /// was already standing: the run begins where the pinned stack ends.
    /// </summary>
    /// <remarks>
    /// The flag used to be flipped in place, so the panel kept its stored
    /// <c>Order</c> and was re-sorted into the run by it — the one pinned panel
    /// in a column, drawn at the very top, landed <em>second</em> among the
    /// tabs. That is the case here: Inspector is order 1 with Project at 0.
    /// </remarks>
    [Fact]
    public void UnpinningLandsAtTheHeadOfTheRun()
    {
        var column = new[]
        {
            new PanelPlacement(PanelKind.Project, PanelSide.Left, 0, Pinned: false, Folded: false),
            new PanelPlacement(PanelKind.Inspector, PanelSide.Left, 1, Pinned: true, Folded: false),
            new PanelPlacement(PanelKind.History, PanelSide.Left, 2, Pinned: false, Folded: false)
        };

        IReadOnlyList<PanelPlacement> crossed =
            PanelPlacementEdit.Cross(column, PanelKind.Inspector);

        Assert.False(Placement(crossed, PanelKind.Inspector).Pinned);
        Assert.Equal(0, Placement(crossed, PanelKind.Inspector).Order);
        Assert.Equal(1, Placement(crossed, PanelKind.Project).Order);
        Assert.Equal(2, Placement(crossed, PanelKind.History).Order);
    }

    /// <summary>
    /// And the head of the run is below whatever stays pinned, so a panel
    /// unpinned from the bottom of the stack does not move at all.
    /// </summary>
    [Fact]
    public void UnpinningLandsBelowWhateverStaysPinned()
    {
        var column = new[]
        {
            new PanelPlacement(PanelKind.Project, PanelSide.Left, 0, Pinned: true, Folded: false),
            new PanelPlacement(PanelKind.Inspector, PanelSide.Left, 1, Pinned: true, Folded: false),
            new PanelPlacement(PanelKind.History, PanelSide.Left, 2, Pinned: false, Folded: false)
        };

        IReadOnlyList<PanelPlacement> crossed =
            PanelPlacementEdit.Cross(column, PanelKind.Inspector);

        Assert.True(Placement(crossed, PanelKind.Project).Pinned);
        Assert.Equal(0, Placement(crossed, PanelKind.Project).Order);
        Assert.Equal(1, Placement(crossed, PanelKind.Inspector).Order);
        Assert.Equal(2, Placement(crossed, PanelKind.History).Order);
    }

    /// <summary>
    /// Pinning is the same crossing the other way: the panel joins the end of
    /// the pinned stack, which is the boundary it was standing on.
    /// </summary>
    [Fact]
    public void PinningJoinsTheEndOfTheStack()
    {
        var column = new[]
        {
            new PanelPlacement(PanelKind.Project, PanelSide.Left, 0, Pinned: true, Folded: false),
            new PanelPlacement(PanelKind.Inspector, PanelSide.Left, 1, Pinned: false, Folded: true),
            new PanelPlacement(PanelKind.History, PanelSide.Left, 2, Pinned: false, Folded: false)
        };

        IReadOnlyList<PanelPlacement> crossed =
            PanelPlacementEdit.Cross(column, PanelKind.Inspector);

        Assert.True(Placement(crossed, PanelKind.Inspector).Pinned);
        Assert.Equal(1, Placement(crossed, PanelKind.Inspector).Order);

        // And a fold is about reserving room, so crossing ends it.
        Assert.False(Placement(crossed, PanelKind.Inspector).Folded);
    }

    /// <summary>Crossing twice puts the column back exactly as it was.</summary>
    [Fact]
    public void CrossingBackRestoresTheColumn()
    {
        IReadOnlyList<PanelPlacement> there =
            PanelPlacementEdit.Cross(Four, PanelKind.Inspector);

        IReadOnlyList<PanelPlacement> back =
            PanelPlacementEdit.Cross(there, PanelKind.Inspector);

        foreach (PanelPlacement placement in Four)
            Assert.Equal(placement, Placement(back, placement.Kind));
    }

    private static PanelPlacement Placement(
        IReadOnlyList<PanelPlacement> placements, PanelKind kind) =>
        placements.First(placement => placement.Kind == kind);
}
