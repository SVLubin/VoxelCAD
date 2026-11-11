using System.Collections.Generic;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The height cascade of <c>docs/panels.md</c>: a temporary panel takes what is
/// free, then the pinned stack shrinks, then folds — bottom-up, topmost last —
/// and the opener scrolls rather than being refused. Pure, so every branch is
/// held here and the node only applies the answer.
/// </summary>
public sealed class PanelColumnLayoutTests
{
    private static PanelSlotInput Pinned(float natural, float min, bool folded = false) =>
        new(Pinned: true, Folded: folded, OpenTemp: false, NaturalBody: natural, MinBody: min);

    private static PanelSlotInput Tab() =>
        new(Pinned: false, Folded: false, OpenTemp: false, NaturalBody: 0f, MinBody: 0f);

    private static PanelSlotInput Temp(float natural, float min) =>
        new(Pinned: false, Folded: false, OpenTemp: true, NaturalBody: natural, MinBody: min);

    [Fact]
    public void EverythingFitsAndEverybodyGetsTheirNaturalHeight()
    {
        IReadOnlyList<PanelSlotPlan> plans = PanelColumnLayout.Solve(
            new[] { Pinned(46f, 46f), Pinned(120f, 60f), Temp(130f, 70f) },
            availableForBodies: 400f);

        Assert.Equal(46f, plans[0].BodyHeight);
        Assert.Equal(120f, plans[1].BodyHeight);
        Assert.Equal(130f, plans[2].BodyHeight);
        Assert.All(plans, plan => Assert.False(plan.AutoFolded));
        Assert.False(plans[2].Scrolls);
    }

    [Fact]
    public void ATemporaryPanelTakesWhatIsFreeWithoutDisturbingAnybody()
    {
        IReadOnlyList<PanelSlotPlan> plans = PanelColumnLayout.Solve(
            new[] { Pinned(46f, 46f), Pinned(120f, 60f), Temp(130f, 70f) },
            availableForBodies: 46f + 120f + 90f);

        Assert.Equal(46f, plans[0].BodyHeight);
        Assert.Equal(120f, plans[1].BodyHeight);
        Assert.Equal(90f, plans[2].BodyHeight);

        // Less than it wanted, so its content is taller than its body.
        Assert.True(plans[2].Scrolls);
    }

    [Fact]
    public void ThePinnedPanelNearestTheOpeningShrinksFirst()
    {
        IReadOnlyList<PanelSlotPlan> plans = PanelColumnLayout.Solve(
            new[] { Pinned(46f, 46f), Pinned(120f, 60f), Temp(130f, 70f) },
            availableForBodies: 46f + 120f + 30f);

        // The lower panel gave exactly the 40 the minimum was short by; the
        // upper one was never touched.
        Assert.Equal(46f, plans[0].BodyHeight);
        Assert.Equal(80f, plans[1].BodyHeight);
        Assert.Equal(70f, plans[2].BodyHeight);
        Assert.False(plans[1].AutoFolded);
    }

    [Fact]
    public void AfterShrinkingComesFoldingAndTheTopmostSurrendersLast()
    {
        IReadOnlyList<PanelSlotPlan> plans = PanelColumnLayout.Solve(
            new[] { Pinned(46f, 46f), Pinned(120f, 60f), Temp(130f, 70f) },
            availableForBodies: 46f + 70f);

        // The lower pinned panel folded away entirely; the toolbar-shaped one on
        // top kept its body, and the opener got its minimum.
        Assert.Equal(46f, plans[0].BodyHeight);
        Assert.False(plans[0].AutoFolded);
        Assert.False(plans[1].BodyVisible);
        Assert.True(plans[1].AutoFolded);
        Assert.Equal(70f, plans[2].BodyHeight);
    }

    [Fact]
    public void WithEverythingFoldedTheOpenerTakesTheFloorAndScrolls()
    {
        IReadOnlyList<PanelSlotPlan> plans = PanelColumnLayout.Solve(
            new[] { Pinned(46f, 46f), Pinned(120f, 60f), Temp(130f, 70f) },
            availableForBodies: 40f);

        Assert.True(plans[0].AutoFolded);
        Assert.True(plans[1].AutoFolded);
        Assert.Equal(PanelColumnLayout.ScrollFloor, plans[2].BodyHeight);
        Assert.True(plans[2].Scrolls);
    }

    [Fact]
    public void APinnedOverflowWithoutAnyOpenerRunsTheSameCascade()
    {
        IReadOnlyList<PanelSlotPlan> plans = PanelColumnLayout.Solve(
            new[] { Pinned(46f, 46f), Pinned(120f, 60f), Tab() },
            availableForBodies: 90f);

        // Shrink to the minimum was not enough (46 + 60 > 90), so the lower one
        // folded; recomputing with a taller container is what un-folds it.
        Assert.Equal(46f, plans[0].BodyHeight);
        Assert.True(plans[1].AutoFolded);
        Assert.False(plans[2].BodyVisible);
    }

    [Fact]
    public void AutoFoldsAreDerivedSoClosingTheOpenerRestoresTheStack()
    {
        PanelSlotInput[] withoutTemp = { Pinned(46f, 46f), Pinned(120f, 60f), Tab() };

        IReadOnlyList<PanelSlotPlan> plans = PanelColumnLayout.Solve(
            withoutTemp, availableForBodies: 200f);

        Assert.Equal(46f, plans[0].BodyHeight);
        Assert.Equal(120f, plans[1].BodyHeight);
        Assert.False(plans[1].AutoFolded);
    }

    [Fact]
    public void AUserFoldStandsAndIsNeverCountedOrTouched()
    {
        IReadOnlyList<PanelSlotPlan> plans = PanelColumnLayout.Solve(
            new[] { Pinned(46f, 46f), Pinned(120f, 60f, folded: true), Temp(130f, 70f) },
            availableForBodies: 46f + 130f);

        Assert.False(plans[1].BodyVisible);
        Assert.False(plans[1].AutoFolded);
        Assert.Equal(130f, plans[2].BodyHeight);
    }

    /// <summary>
    /// "Takes what is left and scrolls" means what is left — with 60 free and a
    /// minimum of 70, granting the bare floor of 44 would waste a visible strip.
    /// </summary>
    [Fact]
    public void TheScrollFallbackTakesWhatIsLeftNotTheBareFloor()
    {
        IReadOnlyList<PanelSlotPlan> plans = PanelColumnLayout.Solve(
            new[] { Pinned(46f, 46f), Pinned(120f, 60f), Temp(130f, 70f) },
            availableForBodies: 60f);

        Assert.True(plans[0].AutoFolded);
        Assert.True(plans[1].AutoFolded);
        Assert.Equal(60f, plans[2].BodyHeight);
        Assert.True(plans[2].Scrolls);
    }

    /// <summary>
    /// The overlay case that crashed: a minimum override far above a near-empty
    /// panel's natural (the inspector with nothing selected). The minimum never
    /// exceeds what the content asks for, floored at the scroll floor.
    /// </summary>
    [Fact]
    public void AnOverlayWithANaturalBelowTheFloorOpensAtTheFloor()
    {
        OverlayPlan plan = PanelColumnLayout.SolveOverlay(
            naturalBody: 32f,
            minBody: 140f,
            anchorY: 10f,
            columnHeight: 800f,
            inset: 4f,
            headerHeight: 32f);

        Assert.Equal(PanelColumnLayout.ScrollFloor, plan.BodyHeight);
        Assert.Equal(10f, plan.Top);
        Assert.False(plan.Scrolls);
    }

    [Fact]
    public void AnOverlayNearTheBottomIsPushedUpToFit()
    {
        OverlayPlan plan = PanelColumnLayout.SolveOverlay(
            naturalBody: 200f,
            minBody: 84f,
            anchorY: 760f,
            columnHeight: 800f,
            inset: 4f,
            headerHeight: 32f);

        Assert.Equal(84f, plan.BodyHeight);
        Assert.Equal(680f, plan.Top);
        Assert.True(plan.Scrolls);
    }

    [Fact]
    public void AnOverlayInAWindowShorterThanItsMinimumShrinksAndScrolls()
    {
        OverlayPlan plan = PanelColumnLayout.SolveOverlay(
            naturalBody: 200f,
            minBody: 84f,
            anchorY: 4f,
            columnHeight: 100f,
            inset: 4f,
            headerHeight: 32f);

        Assert.Equal(60f, plan.BodyHeight);
        Assert.Equal(4f, plan.Top);
        Assert.True(plan.Scrolls);
    }

    [Fact]
    public void AClosedTabHasNoBodyAtAll()
    {
        IReadOnlyList<PanelSlotPlan> plans = PanelColumnLayout.Solve(
            new[] { Pinned(46f, 46f), Tab(), Tab() },
            availableForBodies: 400f);

        Assert.False(plans[1].BodyVisible);
        Assert.False(plans[2].BodyVisible);
        Assert.Equal(0f, plans[1].BodyHeight);
    }
}
