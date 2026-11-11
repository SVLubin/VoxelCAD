using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The outline drawn where a carried panel will land, and where a shown panel
/// stands.
/// </summary>
/// <remarks>
/// What has content here is the <em>selection</em> — which of three rects a
/// landing place is. "The ghost fills the hole" would be a restatement of the
/// one line that advances the column's cursor, and would pass with the feature
/// absent; the ledger's rule about an expected value computed from the thing
/// under test applies. So every number below is a literal.
/// </remarks>
public sealed class PanelGhostGeometryTests
{
    private const float Height = 691f;

    private static PanelSlotInput Pinned(float natural = 200f) =>
        new(Pinned: true, Folded: false, OpenTemp: false, natural, MinBody: 80f);

    private static PanelSlotInput Tab() =>
        new(Pinned: false, Folded: false, OpenTemp: false, 60f, MinBody: 44f);

    private static IReadOnlyList<PanelColumnPlace> Column(
        PanelSide side, params PanelSlotInput[] slots) =>
        PanelColumnGeometry.Solve(slots, Height, side);

    // ---- where a shown panel stands ---------------------------------------

    /// <summary>
    /// One body width in every state, composed through the column rather than
    /// only inside the panel: the surface and the notch it gives away have to
    /// agree, or the panel is one width and the column another.
    /// </summary>
    [Theory]
    [InlineData(PanelSide.Left, true, 358f)]
    [InlineData(PanelSide.Left, false, 322f)]
    [InlineData(PanelSide.Right, true, 358f)]
    [InlineData(PanelSide.Right, false, 322f)]
    public void EveryGrownBodyIsOnePanelWide(
        PanelSide side, bool revealsTabs, float expectedSurface)
    {
        PanelShape grown = PanelColumnGeometry.SolveGrown(
            new Vector2(4f, 100f), revealsTabs, 200f, 80f, Height, side);

        Assert.Equal(expectedSurface, grown.Rect.Size.X, 3);

        // The cut composed from the same answer the width came from, never a
        // literal restated here — that is the whole point of the shape.
        PanelLayout layout = PanelLayoutSolver.Solve(
            grown.Rect.Size,
            grown.Rect.Size.X,
            grown.Rect.Size.Y,
            grown.Cut,
            grown.Cut,
            mirrored: side == PanelSide.Right);

        Assert.Equal(ChromePanel.BodyWidth, layout.Content.Size.X, 3);
    }

    /// <summary>
    /// The right column hangs a grown panel so its stub lands on the very tab
    /// it grew from — the icon never moves — which is what puts its outer edge
    /// on the card's rather than on the column's.
    /// </summary>
    [Fact]
    public void TheMirroredColumnHangsAGrownPanelOffItsOwnTab()
    {
        PanelShape grown = PanelColumnGeometry.SolveGrown(
            new Vector2(296f, 100f), revealsTabs: true, 200f, 80f, Height, PanelSide.Right);

        Assert.Equal(-32f, grown.Rect.Position.X, 3);
        Assert.Equal(326f, grown.Rect.Position.X + grown.Rect.Size.X, 3);
        Assert.Equal(EdgeColumn.NotchReach, grown.Cut, 3);
        Assert.True(grown.StubOnRight);
    }

    // ---- what the landing outline is --------------------------------------

    [Theory]
    [InlineData(PanelSide.Left)]
    [InlineData(PanelSide.Right)]
    public void APinnedGhostIsTheStacksOwnPlan(PanelSide side)
    {
        IReadOnlyList<PanelColumnPlace> places = Column(side, Pinned(), Pinned(), Tab());

        PanelShape ghost = Assert.NotNull(
            PanelColumnGeometry.SolveGhost(
                places, carried: 1, grown: false, 200f, 80f, Height, side));

        Assert.Equal(EdgeColumn.Inset, ghost.Rect.Position.X, 3);
        Assert.Equal(ChromePanel.SurfaceWidth, ghost.Rect.Size.X, 3);
        Assert.Equal(0f, ghost.Cut, 3);
        Assert.Equal(Vector2.Zero, ghost.Stub);
    }

    /// <summary>
    /// A tab's landing place is the card in the run, and where the run hangs
    /// is the one thing that mirrors.
    /// </summary>
    [Theory]
    [InlineData(PanelSide.Left, true, 4f)]
    [InlineData(PanelSide.Right, true, 296f)]
    [InlineData(PanelSide.Left, false, 3f)]
    [InlineData(PanelSide.Right, false, 3f)]
    public void ATabGhostIsACardInTheRun(PanelSide side, bool anythingPinned, float expectedX)
    {
        IReadOnlyList<PanelColumnPlace> places = anythingPinned
            ? Column(side, Pinned(), Tab(), Tab())
            : Column(side, Tab(), Tab());

        var carried = anythingPinned ? 1 : 0;

        PanelShape ghost = Assert.NotNull(
            PanelColumnGeometry.SolveGhost(
                places, carried, grown: false, 60f, 44f, Height, side));

        Assert.Equal(expectedX, ghost.Rect.Position.X, 3);
        Assert.Equal(EdgeColumn.TabCard, ghost.Rect.Size.X, 3);
        Assert.Equal(EdgeColumn.TabCard, ghost.Rect.Size.Y, 3);
        Assert.Equal(0f, ghost.Cut, 3);
    }

    /// <summary>
    /// An open panel dropped in the run lands open — a panel travels as the
    /// thing it is — so its outline is the surface it grows back into, not the
    /// card the run reserves. This is the assertion that fails if the branch is
    /// dropped and the reserved rect returned instead.
    /// </summary>
    [Fact]
    public void AnOpenPanelsGhostIsTheSurfaceItGrowsBackInto()
    {
        IReadOnlyList<PanelColumnPlace> places =
            Column(PanelSide.Right, Pinned(), Tab(), Tab());

        PanelShape ghost = Assert.NotNull(
            PanelColumnGeometry.SolveGhost(
                places, carried: 1, grown: true, 200f, 80f, Height, PanelSide.Right));

        Assert.Equal(358f, ghost.Rect.Size.X, 3);
        Assert.Equal(-32f, ghost.Rect.Position.X, 3);

        // The Γ, stated as the stub the shader takes: a notch one card deep.
        Assert.Equal(new Vector2(EdgeColumn.NotchReach, ChromePanel.CollapsedSide), ghost.Stub);
        Assert.True(ghost.StubOnRight);
    }

    [Fact]
    public void AnOpenLastCardsGhostCutsNoNotch()
    {
        IReadOnlyList<PanelColumnPlace> places =
            Column(PanelSide.Right, Pinned(), Tab());

        PanelShape ghost = Assert.NotNull(
            PanelColumnGeometry.SolveGhost(
                places, carried: 1, grown: true, 200f, 80f, Height, PanelSide.Right));

        Assert.Equal(ChromePanel.SurfaceWidth, ghost.Rect.Size.X, 3);

        // The name of this test has been claiming it all along; only now can it
        // say so, rather than inferring a missing notch from a width.
        Assert.Equal(0f, ghost.Cut, 3);
        Assert.Equal(Vector2.Zero, ghost.Stub);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void NothingCarriedIsNoGhost(int carried) =>
        Assert.Null(
            PanelColumnGeometry.SolveGhost(
                Column(PanelSide.Left, Pinned(), Tab()),
                carried, grown: false, 60f, 44f, Height, PanelSide.Left));
}

/// <summary>
/// The notch as a fact about where a slot stands, and the invariant that fails
/// the instant a rect and its cut come from two places.
/// </summary>
public sealed class PanelShapeTests
{
    private const float Height = 691f;

    private static PanelSlotInput Pinned(float natural = 200f) =>
        new(Pinned: true, Folded: false, OpenTemp: false, natural, MinBody: 80f);

    private static PanelSlotInput Tab() =>
        new(Pinned: false, Folded: false, OpenTemp: false, 60f, MinBody: 44f);

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void OnlyAnUnpinnedSurfaceWithSomethingBelowItCutsANotch(
        bool pinned, bool revealsTabs, bool cuts)
    {
        Assert.Equal(cuts, PanelNotch.Cuts(pinned, revealsTabs));
        Assert.Equal(
            cuts ? EdgeColumn.NotchReach : 0f, PanelNotch.Reach(pinned, revealsTabs), 3);
    }

    /// <summary>
    /// The composed invariant: whatever a grown surface is, less the notch it
    /// gives away, is one body. It cannot hold unless the width and the cut are
    /// one answer.
    /// </summary>
    [Theory]
    [InlineData(PanelSide.Left, true)]
    [InlineData(PanelSide.Left, false)]
    [InlineData(PanelSide.Right, true)]
    [InlineData(PanelSide.Right, false)]
    public void AGhostsSurfaceLessItsNotchIsOneBodyWide(PanelSide side, bool revealsTabs)
    {
        PanelShape grown = PanelColumnGeometry.SolveGrown(
            new Vector2(4f, 100f), revealsTabs, 200f, 80f, Height, side);

        Assert.Equal(ChromePanel.SurfaceWidth, grown.Rect.Size.X - grown.Cut, 3);
    }

    /// <summary>
    /// A place knows whether anything stands below it, so nobody has to be told.
    /// This used to be a walk inside the node, restated wherever it was wanted.
    /// </summary>
    [Fact]
    public void OnlyTheLastCardInARunRevealsNothing()
    {
        IReadOnlyList<PanelColumnPlace> places = PanelColumnGeometry.Solve(
            new[] { Pinned(), Tab(), Tab(), Tab() }, Height, PanelSide.Left);

        Assert.False(places[0].RevealsTabs);
        Assert.True(places[1].RevealsTabs);
        Assert.True(places[2].RevealsTabs);
        Assert.False(places[3].RevealsTabs);
    }

    [Fact]
    public void AColumnHoldingOneTabRevealsNothing()
    {
        IReadOnlyList<PanelColumnPlace> places = PanelColumnGeometry.Solve(
            new[] { Pinned(), Tab() }, Height, PanelSide.Left);

        Assert.False(places[1].RevealsTabs);
    }

    /// <summary>The stub stands on its column's own side, and only there.</summary>
    [Theory]
    [InlineData(PanelSide.Left, false)]
    [InlineData(PanelSide.Right, true)]
    public void AShapesStubIsOnItsColumnsOwnSide(PanelSide side, bool onRight) =>
        Assert.Equal(onRight, PanelShape.For(new Rect2(), 36f, side).StubOnRight);

    /// <summary>A shape with no cut has no stub at all, not a zero-wide one.</summary>
    [Fact]
    public void NoCutIsNoStub() =>
        Assert.Equal(Vector2.Zero, PanelShape.For(new Rect2(), 0f, PanelSide.Left).Stub);
}

/// <summary>
/// A rectangle and the notch cut out of it, moving as one shape.
/// </summary>
public sealed class ShapeMotionTests
{
    private const float Frame = 1f / 60f;

    /// <summary>The pace the editor ships with, which is what these hold.</summary>
    private static readonly MotionTiming Fast = new();

    /// <summary>
    /// Given a clock of its own the notch — 36 units, and so always at the
    /// duration floor — would land in 50 ms while the surface it is cut out of
    /// was still travelling, growing a rectangle and cutting a Γ into it.
    /// </summary>
    [Fact]
    public void ANotchArrivesWithTheSurfaceItIsCutOutOf()
    {
        var shape = new ShapeMotion(Fast);

        shape.SnapTo(new Rect2(4f, 4f, 30f, 200f), 0f);
        shape.Retarget(new Rect2(4f, 4f, 358f, 200f), EdgeColumn.NotchReach);

        // Past the floor a lone notch would have finished in, and short of the
        // surface's own 119 ms.
        for (var tick = 0; tick < 4; tick++)
            shape.Tick(Frame);

        Assert.False(shape.IsSettled);
        Assert.InRange(shape.Cut, 1f, EdgeColumn.NotchReach - 1f);

        for (var tick = 0; tick < 20; tick++)
            shape.Tick(Frame);

        Assert.True(shape.IsSettled);
        Assert.Equal(EdgeColumn.NotchReach, shape.Cut, 3);
    }

    /// <summary>
    /// The first target a fresh shape is given puts rect and cut outright, so
    /// the first frame of an outline stands in a hole already that shape.
    /// </summary>
    [Fact]
    public void AFreshShapeSnapsAllOfItself()
    {
        var shape = new ShapeMotion(Fast);

        shape.Retarget(new Rect2(4f, 4f, 358f, 200f), EdgeColumn.NotchReach);

        Assert.True(shape.IsSettled);
        Assert.Equal(EdgeColumn.NotchReach, shape.Cut, 3);
        Assert.Equal(358f, shape.Snapped.Size.X, 3);
    }

    /// <summary>
    /// Carrying takes over the position and nothing else — a panel crossing the
    /// window keeps the Γ it was carrying, rather than arriving as a rectangle
    /// with its contents still laid out around a notch.
    /// </summary>
    [Fact]
    public void ACarriedShapeKeepsItsNotch()
    {
        var shape = new ShapeMotion(Fast);

        shape.SnapTo(new Rect2(4f, 4f, 358f, 200f), EdgeColumn.NotchReach);
        shape.CarryTo(new Vector2(700f, 90f));

        Assert.Equal(EdgeColumn.NotchReach, shape.Cut, 3);
        Assert.Equal(new Vector2(700f, 90f), shape.Snapped.Position);
        Assert.Equal(358f, shape.Snapped.Size.X, 3);
    }
}
