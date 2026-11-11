using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The corner grip's arithmetic, and the width flowing through the column's
/// geometry. The gesture itself lives in the shell and is driven; what a pull
/// is worth, and what a column does with a width that is not the shipped one,
/// are decidable here.
/// </summary>
public sealed class PanelResizeTests
{
    private const float Wider = 420f;

    [Fact]
    public void AnUntouchedPanelAsksForItsContentAndADraggedOneForItsHeight()
    {
        Assert.Equal(300f, PanelResize.RequestedBody(0.0, 300f, 120f));
        Assert.Equal(240f, PanelResize.RequestedBody(240.0, 300f, 120f));
    }

    /// <summary>
    /// Floored, never refused — the clamp-don't-throw bargain — and deliberately
    /// not capped at the content: a fixed-height panel that does not grow with
    /// every new row is half of what a dragged height is for.
    /// </summary>
    [Fact]
    public void AStoredHeightIsFlooredAtThePanelsMinimumAndNotCappedAtTheContent()
    {
        Assert.Equal(120f, PanelResize.RequestedBody(40.0, 300f, 120f));
        Assert.Equal(500f, PanelResize.RequestedBody(500.0, 300f, 120f));
    }

    [Fact]
    public void AWidthIsHeldToTheSameBoundsThePreferencesKeep()
    {
        Assert.Equal((float)PanelWidths.Minimum, PanelResize.ClampWidth(10.0));
        Assert.Equal((float)PanelWidths.Maximum, PanelResize.ClampWidth(5000.0));
        Assert.Equal(400f, PanelResize.ClampWidth(400.0));
    }

    /// <summary>
    /// Dragging toward the scene widens on either side: the left column's inner
    /// edge is its right, the right column's is its left.
    /// </summary>
    [Fact]
    public void APullTowardTheSceneWidensOnBothColumns()
    {
        Assert.Equal(50f, PanelResize.TowardScene(PanelSide.Left, 50f));
        Assert.Equal(50f, PanelResize.TowardScene(PanelSide.Right, -50f));
    }

    /// <summary>
    /// The width is the side's: every pinned surface of a column solved at a
    /// dragged width wears it, whole.
    /// </summary>
    [Theory]
    [InlineData(PanelSide.Left)]
    [InlineData(PanelSide.Right)]
    public void EveryPinnedSurfaceWearsTheSidesWidth(PanelSide side)
    {
        IReadOnlyList<PanelColumnPlace> places = PanelColumnGeometry.Solve(
            new[]
            {
                new PanelSlotInput(true, false, false, 200f, 120f),
                new PanelSlotInput(true, false, false, 200f, 120f)
            },
            691f,
            side,
            Wider);

        Assert.All(places, place => Assert.Equal(Wider, place.Rect.Size.X, 3));
    }

    /// <summary>
    /// The run still hangs on the window's edge: on the right column the tabs
    /// stand a full (wider) surface in from the column's inner edge, so a wider
    /// column moves its own tabs with it.
    /// </summary>
    [Fact]
    public void TheRightColumnsRunFollowsTheWidthToTheWindowsEdge()
    {
        IReadOnlyList<PanelColumnPlace> places = PanelColumnGeometry.Solve(
            new[]
            {
                new PanelSlotInput(true, false, false, 200f, 120f),
                new PanelSlotInput(false, false, false, 200f, 120f)
            },
            691f,
            PanelSide.Right,
            Wider);

        Assert.Equal(
            Wider + EdgeColumn.Inset - EdgeColumn.TabCard,
            places[1].Rect.Position.X,
            3);
    }

    /// <summary>A grown overlay is the side's surface plus the notch it gives away.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGrownPanelIsTheSidesSurfacePlusItsCut(bool revealsTabs)
    {
        PanelShape grown = PanelColumnGeometry.SolveGrown(
            new Vector2(4f, 100f), revealsTabs, 200f, 80f, 691f, PanelSide.Left, Wider);

        Assert.Equal(
            Wider + PanelNotch.Reach(pinned: false, revealsTabs),
            grown.Rect.Size.X,
            3);
    }

    /// <summary>
    /// The dragged height reaches the cascade as the panel's request, so a
    /// panel dragged shorter than its content scrolls instead of growing back.
    /// </summary>
    [Fact]
    public void ADraggedHeightIsWhatTheCascadeIsAskedFor()
    {
        IReadOnlyList<PanelColumnPlace> places = PanelColumnGeometry.Solve(
            new[] { new PanelSlotInput(true, false, false, PanelResize.RequestedBody(180.0, 400f, 120f), 120f) },
            691f,
            PanelSide.Left);

        Assert.Equal(180f, places[0].BodyHeight, 3);
    }
}
