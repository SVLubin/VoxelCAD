using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Where a render sits in the panel showing it. The invariant that matters is the
/// zoom's — what is under the pointer must not move — and it is the one nobody can
/// check by looking, because being wrong by a few pixels reads as the picture
/// drifting rather than as arithmetic.
/// </summary>
public sealed class ImageViewTests
{
    private static readonly Vector2 Image = new(1280f, 720f);
    private static readonly Vector2 Canvas = new(640f, 360f);

    [Fact]
    public void FittingShowsThePictureWholeAndCentred()
    {
        ImageView view = ImageView.Fit(Image, Canvas);

        Assert.Equal(0.5f, view.Zoom, 3);

        Rect2 placement = view.Placement(Image);

        Assert.Equal(0f, placement.Position.X, 3);
        Assert.Equal(0f, placement.Position.Y, 3);
        Assert.Equal(Canvas, placement.Size);
    }

    /// <summary>
    /// The whole point of storing an offset rather than a centre: whatever the
    /// pointer is on stays under it while the wheel turns.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(320f, 180f)]
    [InlineData(640f, 360f)]
    [InlineData(97f, 233f)]
    public void ZoomingHoldsWhatIsUnderThePointer(float x, float y)
    {
        var at = new Vector2(x, y);
        ImageView view = ImageView.Fit(Image, Canvas);

        Vector2 before = (at - view.Offset) / view.Zoom;

        ImageView zoomed = view.ScaledAt(at, 4f, Image, Canvas);

        Vector2 after = (at - zoomed.Offset) / zoomed.Zoom;

        Assert.Equal(before.X, after.X, 2);
        Assert.Equal(before.Y, after.Y, 2);
    }

    /// <summary>
    /// A picture smaller than the panel is centred rather than left wherever a
    /// drag put it, and one larger than the panel may not be dragged clear of it.
    /// The rule every photo viewer has.
    /// </summary>
    [Fact]
    public void APictureSmallerThanThePanelStaysCentred()
    {
        ImageView view = ImageView.Fit(Image, Canvas).Panned(new Vector2(500f, 500f), Image, Canvas);

        Assert.Equal(0f, view.Placement(Image).Position.X, 3);
        Assert.Equal(0f, view.Placement(Image).Position.Y, 3);
    }

    [Fact]
    public void APictureLargerThanThePanelCannotBeDraggedClearOfIt()
    {
        ImageView view = ImageView.Fit(Image, Canvas)
            .AtActualSize(Image, Canvas)
            .Panned(new Vector2(5000f, 5000f), Image, Canvas);

        Rect2 placement = view.Placement(Image);

        Assert.True(placement.Position.X <= 0f);
        Assert.True(placement.Position.Y <= 0f);
        Assert.True(placement.End.X >= Canvas.X);
        Assert.True(placement.End.Y >= Canvas.Y);
    }

    /// <summary>
    /// Fitting is always reachable, however large the picture is — the floor on the
    /// zoom must not be able to stop a render being seen whole.
    /// </summary>
    [Fact]
    public void EvenAHugePictureCanBeFitted()
    {
        var huge = new Vector2(4096f, 4096f);
        var small = new Vector2(100f, 100f);
        ImageView view = ImageView.Fit(huge, small);

        Assert.True(view.Zoom < ImageView.MinimumZoom);
        Assert.True(view.Placement(huge).Size.X <= small.X + 0.01f);
        Assert.True(view.Placement(huge).Size.Y <= small.Y + 0.01f);
    }

    [Fact]
    public void ZoomingStopsAtTheCeiling()
    {
        ImageView view = ImageView.Fit(Image, Canvas).ScaledAt(Vector2.Zero, 10000f, Image, Canvas);

        Assert.Equal(ImageView.MaximumZoom, view.Zoom, 3);
    }

    /// <summary>
    /// A fitted picture asks the renderer for nothing in particular, which is how
    /// "all of it, equally" is said — and it must not be a rectangle that happens
    /// to be the whole picture, or a rounding at the edge would read as aiming.
    /// </summary>
    [Fact]
    public void AFittedPictureNamesNoRegion()
    {
        Assert.True(ImageView.Fit(Image, Canvas).VisibleRegion(Image, Canvas).IsEmpty);
    }

    [Fact]
    public void AZoomedPictureNamesWhatIsOnScreen()
    {
        RenderRegion region = ImageView.Fit(Image, Canvas)
            .AtActualSize(Image, Canvas)
            .VisibleRegion(Image, Canvas);

        Assert.False(region.IsEmpty);

        // A fitted picture is centred, so holding the panel's middle at one to one
        // shows the panel's own size out of the middle of the picture.
        Assert.Equal(320, region.X);
        Assert.Equal(180, region.Y);
        Assert.Equal(640, region.Width);
        Assert.Equal(360, region.Height);
    }

    /// <summary>
    /// The region asked for is the region shown, so that aiming a render at a part
    /// of the picture and looking at that part are the same act.
    /// </summary>
    [Fact]
    public void FramingARegionShowsThatRegion()
    {
        var wanted = new RenderRegion(400, 200, 320, 180);
        ImageView view = ImageView.Framing(wanted, Image, Canvas);

        RenderRegion shown = view.VisibleRegion(Image, Canvas);

        Assert.Equal(wanted.X, shown.X);
        Assert.Equal(wanted.Y, shown.Y);
        Assert.Equal(wanted.Width, shown.Width);
        Assert.Equal(wanted.Height, shown.Height);
    }

    [Fact]
    public void FramingNothingIsFitting()
    {
        Assert.Equal(ImageView.Fit(Image, Canvas), ImageView.Framing(default, Image, Canvas));
    }

    /// <summary>
    /// A panel with no size yet — which is what a card is on the frame it is built —
    /// must not produce an infinity that then becomes an offset.
    /// </summary>
    [Fact]
    public void APanelWithNoSizeIsQuiet()
    {
        Assert.Equal(ImageView.None, ImageView.Fit(Image, Vector2.Zero));
        Assert.Equal(ImageView.None, ImageView.Fit(Vector2.Zero, Canvas));
        Assert.True(ImageView.None.VisibleRegion(Vector2.Zero, Canvas).IsEmpty);
    }
}
