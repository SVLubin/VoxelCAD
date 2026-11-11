using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The loop a drag closes, and whether it settles.
/// </summary>
/// <remarks>
/// A drag runs this once a frame: the pointer picks a drop target, the target
/// is applied as a preview, the preview re-solves the column, and the column's
/// new geometry is what the <em>next</em> frame's pointer is measured against.
/// Nothing in that chain has hysteresis, and the height cascade is
/// order-dependent — it shrinks and folds bottom-up — so moving a panel one
/// place up the stack can change every height below it and therefore move the
/// very boundaries the decision was made on.
/// <para>
/// A loop that does not reach a fixed point alternates between two layouts, one
/// per frame. Every frame is individually correct, so a screenshot shows nothing
/// and a recording sampling one phase shows nothing; an eye integrating both
/// sees one image doubled. That is why this is a test and not a screenshot.
/// </para>
/// </remarks>
public sealed class PanelDragLoopTests
{
    private const float ColumnHeight = 691f;

    /// <summary>The pointer must land on the same answer it produced last frame.</summary>
    private const int Rounds = 6;

    /// <summary>
    /// The whole loop, run to exhaustion for one pointer position. Returns the
    /// sequence of answers, so a caller can tell "settled" from "alternating".
    /// </summary>
    private static List<PanelDropTarget> Iterate(
        IReadOnlyList<PanelPlacement> pristine,
        IReadOnlyDictionary<PanelKind, (float Natural, float Min)> content,
        PanelKind carried,
        float pointerX,
        float pointerY,
        bool frozen = true)
    {
        var answers = new List<PanelDropTarget>();
        IReadOnlyList<PanelPlacement> preview = pristine;

        for (var round = 0; round < Rounds; round++)
        {
            PanelDropTarget target = PanelDropTargets.Solve(
                pointerX,
                pointerY,
                WindowWidth,
                DescribeSide(preview, content, PanelSide.Left, carried),
                DescribeSide(preview, content, PanelSide.Right, carried));

            answers.Add(target);

            preview = PanelPlacementEdit.Move(
                pristine, carried, target.Side, target.Pinned, target.Index);

            if (frozen)
                preview = pristine;
        }

        return answers;
    }

    private const float WindowWidth = 1152f;

    private static PanelColumnDrop DescribeSide(
        IReadOnlyList<PanelPlacement> placements,
        IReadOnlyDictionary<PanelKind, (float Natural, float Min)> content,
        PanelSide side,
        PanelKind carried)
    {
        (IReadOnlyList<PanelPlacement> ordered, PanelSlotInput[] slots) =
            Order(placements, content, side);

        var excluded = -1;

        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Kind == carried)
                excluded = index;
        }

        return PanelColumnGeometry.Describe(
            PanelColumnGeometry.Solve(slots, ColumnHeight, side), 0f, excluded);
    }

    /// <summary>
    /// One column's panels in the order it stacks them — pinned first, then the
    /// run — with what each of them asks for. The column's own ordering, so a
    /// place looked up here is the place the drop was judged against.
    /// </summary>
    private static (IReadOnlyList<PanelPlacement>, PanelSlotInput[]) Order(
        IReadOnlyList<PanelPlacement> placements,
        IReadOnlyDictionary<PanelKind, (float Natural, float Min)> content,
        PanelSide side)
    {
        List<PanelPlacement> ordered = placements
            .Where(placement => placement.Side == side)
            .OrderBy(placement => placement.Pinned ? 0 : 1)
            .ThenBy(placement => placement.Order)
            .ToList();

        var slots = new PanelSlotInput[ordered.Count];

        for (var index = 0; index < ordered.Count; index++)
        {
            (float natural, float min) = content[ordered[index].Kind];

            slots[index] = new PanelSlotInput(
                ordered[index].Pinned,
                ordered[index].Folded,
                OpenTemp: false,
                natural,
                min);
        }

        return (ordered, slots);
    }

    /// <summary>
    /// Four panels in one column, three of them pinned and asking for more room
    /// than the column has — which is the case that puts the cascade's shrink
    /// and fold steps in play, and so the case where the order a panel is
    /// previewed at changes everybody's height.
    /// </summary>
    private static IReadOnlyList<PanelPlacement> Crowded => new[]
    {
        new PanelPlacement(PanelKind.Project, PanelSide.Left, 0, Pinned: true, Folded: false),
        new PanelPlacement(PanelKind.History, PanelSide.Left, 1, Pinned: true, Folded: false),
        new PanelPlacement(PanelKind.Inspector, PanelSide.Left, 2, Pinned: true, Folded: false),
        new PanelPlacement(PanelKind.Toolbar, PanelSide.Left, 3, Pinned: false, Folded: false)
    };

    private static IReadOnlyDictionary<PanelKind, (float, float)> Hungry =>
        new Dictionary<PanelKind, (float, float)>
        {
            [PanelKind.Project] = (420f, 140f),
            [PanelKind.History] = (380f, 120f),
            [PanelKind.Inspector] = (300f, 120f),
            [PanelKind.Toolbar] = (60f, 44f)
        };

    /// <summary>
    /// The span a drop is judged by counts the carried panel's own reserved
    /// place, so previewing that panel into the stack does not move it.
    /// </summary>
    /// <remarks>
    /// This test used to assert the opposite, and the opposite was the defect.
    /// The column reserves the carried panel's place and the ghost is drawn in
    /// it, while <c>Describe</c> left that panel out of everything it reported
    /// — so the span began below the panel's own hole, and a pointer parked in
    /// that hole read as outside the stack. Counting the place in the span and
    /// leaving the panel out of the <em>centres</em> alone is what separates
    /// the two questions: how tall the stack is, and how many panels the
    /// pointer has passed.
    /// </remarks>
    [Fact]
    public void PreviewingTheCarriedPanelLeavesTheSpanItIsJudgedByAlone()
    {
        PanelColumnDrop before =
            DescribeSide(Crowded, Hungry, PanelSide.Left, PanelKind.Toolbar);

        IReadOnlyList<PanelPlacement> previewed = PanelPlacementEdit.Move(
            Crowded, PanelKind.Toolbar, PanelSide.Left, pinned: true, index: 0);

        PanelColumnDrop after =
            DescribeSide(previewed, Hungry, PanelSide.Left, PanelKind.Toolbar);

        Assert.Equal(before.PinnedTop, after.PinnedTop, 3);
    }

    /// <summary>
    /// The place a panel was lifted from is a place it can be put back, which
    /// is the whole of what "drag it and change nothing" has to mean. The
    /// bottom-most pinned panel is the case that was reported: its own room was
    /// left out of the stack it was measured against, so pulling it down by a
    /// unit already read as below the stack and it unpinned itself.
    /// </summary>
    [Theory]
    [InlineData(PanelKind.Project)]
    [InlineData(PanelKind.History)]
    [InlineData(PanelKind.Inspector)]
    public void APinnedPanelDraggedInsideItsOwnPlaceStaysPinned(PanelKind carried)
    {
        Rect2 place = PlaceOf(Crowded, Hungry, PanelSide.Left, carried);
        PanelColumnDrop left = DescribeSide(Crowded, Hungry, PanelSide.Left, carried);
        PanelColumnDrop right = DescribeSide(Crowded, Hungry, PanelSide.Right, carried);

        for (float y = place.Position.Y; y <= place.Position.Y + place.Size.Y; y += 1f)
        {
            PanelDropTarget target =
                PanelDropTargets.Solve(100f, y, WindowWidth, left, right);

            Assert.True(target.Pinned, $"{carried} at y={y} reads as unpinned");
        }
    }

    /// <summary>
    /// And the sharper half of it: with one pinned panel and that panel in
    /// hand, the list of the others is empty and the stack is not. Asking the
    /// list meant such a panel could never be put back, wherever it was
    /// dropped — it always came down as a tab.
    /// </summary>
    [Fact]
    public void AColumnWhoseOnlyPinnedPanelIsInHandStillHasAStack()
    {
        PanelColumnDrop left =
            DescribeSide(OnePinned, Hungry, PanelSide.Left, PanelKind.Project);

        Assert.Empty(left.PinnedCentres);
        Assert.True(left.HasStack);

        Rect2 place = PlaceOf(OnePinned, Hungry, PanelSide.Left, PanelKind.Project);

        PanelDropTarget target = PanelDropTargets.Solve(
            100f,
            place.Position.Y + (place.Size.Y * 0.5f),
            WindowWidth,
            left,
            DescribeSide(OnePinned, Hungry, PanelSide.Right, PanelKind.Project));

        Assert.True(target.Pinned);
    }

    /// <summary>One pinned panel over a run of tabs — the reported column.</summary>
    private static IReadOnlyList<PanelPlacement> OnePinned => new[]
    {
        new PanelPlacement(PanelKind.Project, PanelSide.Left, 0, Pinned: true, Folded: false),
        new PanelPlacement(PanelKind.History, PanelSide.Left, 0, Pinned: false, Folded: false),
        new PanelPlacement(PanelKind.Inspector, PanelSide.Left, 1, Pinned: false, Folded: false),
        new PanelPlacement(PanelKind.Toolbar, PanelSide.Left, 2, Pinned: false, Folded: false)
    };

    /// <summary>Where one panel of a column stands, in window coordinates.</summary>
    private static Rect2 PlaceOf(
        IReadOnlyList<PanelPlacement> placements,
        IReadOnlyDictionary<PanelKind, (float Natural, float Min)> content,
        PanelSide side,
        PanelKind kind)
    {
        (IReadOnlyList<PanelPlacement> ordered, PanelSlotInput[] slots) =
            Order(placements, content, side);

        IReadOnlyList<PanelColumnPlace> places =
            PanelColumnGeometry.Solve(slots, ColumnHeight, side);

        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Kind == kind)
                return places[index].Rect;
        }

        throw new KeyNotFoundException($"{kind} is not in the {side} column.");
    }

    /// <summary>
    /// The same invariant with the ruler deliberately <b>unfrozen</b>: measured
    /// against the very preview it produced, a parked pointer must still reach
    /// a fixed point.
    /// </summary>
    /// <remarks>
    /// This is the test that says the alternation was cured rather than
    /// covered. It used to assert the opposite — that the unfrozen loop cycles
    /// — because it did: 148 of 692 pointer positions on the left, 148 on the
    /// right, 206 with a pinned panel in hand. Every one of those came from the
    /// span leaving out the place it was reserving, so counting that place
    /// removed all of them, for every panel of this column in turn.
    /// <para>
    /// The freeze stays regardless, and is no less right for being belt and
    /// braces: one crowded column swept exhaustively is not a proof about every
    /// layout, and the rule it keeps — a decision taken once a frame from a
    /// pointer is measured against something that decision cannot move — is
    /// worth having whether or not anything currently violates it. What this
    /// holds is the root: reintroduce a self-moving ruler and it fails here,
    /// where the freeze would hide it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(PanelKind.Project)]
    [InlineData(PanelKind.History)]
    [InlineData(PanelKind.Inspector)]
    [InlineData(PanelKind.Toolbar)]
    public void EvenAnUnfrozenRulerSettles(PanelKind carried)
    {
        var unsettled = new List<string>();

        for (float pointerY = 0f; pointerY <= ColumnHeight; pointerY += 1f)
        {
            List<PanelDropTarget> answers =
                Iterate(Crowded, Hungry, carried, 100f, pointerY, frozen: false);

            if (answers[^1] != answers[^2])
                unsettled.Add($"y={pointerY}: {answers[^2]} → {answers[^1]}");
        }

        Assert.True(
            unsettled.Count == 0,
            $"{unsettled.Count} pointer positions never settle:\n"
            + string.Join("\n", unsettled.Take(8)));
    }

    /// <summary>
    /// The invariant the whole gesture rests on: wherever the hand is parked,
    /// the answer must be the same on every frame. Anything else is a layout
    /// alternating at frame rate under a hand that is not moving — every frame
    /// individually correct, so a screenshot and a recording sampling one phase
    /// both show nothing, while an eye integrating both sees one image doubled.
    /// </summary>
    [Fact]
    public void AParkedPointerMustSettleOnOneAnswer()
    {
        var unsettled = new List<string>();

        for (float pointerY = 0f; pointerY <= ColumnHeight; pointerY += 1f)
        {
            List<PanelDropTarget> answers =
                Iterate(Crowded, Hungry, PanelKind.Toolbar, 100f, pointerY);

            if (answers[^1] != answers[^2])
                unsettled.Add($"y={pointerY}: …{answers[^3]} → {answers[^2]} → {answers[^1]}");
        }

        Assert.True(
            unsettled.Count == 0,
            $"{unsettled.Count} pointer positions never settle:\n"
            + string.Join("\n", unsettled.Take(12)));
    }

    /// <summary>
    /// The same sweep on the mirrored column, because every defect this
    /// subsystem has had lived on the right and was missed by checking the left.
    /// </summary>
    [Fact]
    public void AParkedPointerSettlesOnTheMirroredColumnToo()
    {
        IReadOnlyList<PanelPlacement> right = Crowded
            .Select(placement => placement with { Side = PanelSide.Right })
            .ToList();

        var unsettled = new List<string>();

        for (float pointerY = 0f; pointerY <= ColumnHeight; pointerY += 1f)
        {
            List<PanelDropTarget> answers =
                Iterate(right, Hungry, PanelKind.Toolbar, 1000f, pointerY);

            if (answers[^1] != answers[^2])
                unsettled.Add($"y={pointerY}: {answers[^2]} → {answers[^1]}");
        }

        Assert.True(
            unsettled.Count == 0,
            $"{unsettled.Count} pointer positions never settle:\n"
            + string.Join("\n", unsettled.Take(12)));
    }

    /// <summary>
    /// And with a pinned panel in hand, which is the gesture that failed:
    /// the hole its own plan reserves is part of what moves the boundaries.
    /// </summary>
    [Fact]
    public void APinnedPanelInHandSettlesToo()
    {
        var unsettled = new List<string>();

        for (float pointerY = 0f; pointerY <= ColumnHeight; pointerY += 1f)
        {
            List<PanelDropTarget> answers =
                Iterate(Crowded, Hungry, PanelKind.History, 100f, pointerY);

            if (answers[^1] != answers[^2])
                unsettled.Add($"y={pointerY}: {answers[^2]} → {answers[^1]}");
        }

        Assert.True(
            unsettled.Count == 0,
            $"{unsettled.Count} pointer positions never settle:\n"
            + string.Join("\n", unsettled.Take(12)));
    }
}
