using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What an extrude pull resolves to. The heart of it is the latch that tells a
/// click from a gesture: a press that never travelled is one layer, a pull
/// brought home is nothing, and the two must not be mistaken for each other.
/// </summary>
public sealed class ExtrudeDragStateTests
{
    private static readonly ScenePick Pick =
        new(new Vector3I(2, 0, 2), new Vector3I(0, 1, 0), true, false);

    private static readonly ScenePick CarvePick =
        new(new Vector3I(2, 0, 2), new Vector3I(0, 1, 0), true, true);

    [Fact]
    public void APressThatNeverTravelledIsTheClick()
    {
        var drag = new ExtrudeDragState(Pick, baseOffset: 7.3f);

        Assert.Equal(1, drag.ResolveLayers());
    }

    /// <summary>The click's modifier still picks its direction — the view's own rule.</summary>
    [Fact]
    public void TheModifiedClickCarvesInward()
    {
        var drag = new ExtrudeDragState(CarvePick, baseOffset: 7.3f);

        Assert.Equal(-1, drag.ResolveLayers());
    }

    /// <summary>Jitter under half a cell is still the click, not a zero-layer gesture.</summary>
    [Fact]
    public void JitterUnderHalfACellStaysAClick()
    {
        var drag = new ExtrudeDragState(Pick, baseOffset: 0f);

        drag.Update(0.3f);
        drag.Update(-0.2f);

        Assert.Equal(0, drag.PendingLayers);
        Assert.Equal(1, drag.ResolveLayers());
    }

    [Fact]
    public void APullOutwardCountsItsLayers()
    {
        var drag = new ExtrudeDragState(Pick, baseOffset: 4f);

        Assert.True(drag.Update(6.6f));
        Assert.Equal(3, drag.PendingLayers);
        Assert.Equal(3, drag.ResolveLayers());
    }

    [Fact]
    public void APullInwardCountsNegative()
    {
        var drag = new ExtrudeDragState(Pick, baseOffset: 0f);

        drag.Update(-1.8f);

        Assert.Equal(-2, drag.ResolveLayers());
    }

    /// <summary>
    /// The latch: a pull that reached a layer and came home resolves to nothing —
    /// the hand changed its mind — rather than collapsing back into the click it
    /// was retreating from.
    /// </summary>
    [Fact]
    public void APullBroughtHomeResolvesToNothing()
    {
        var drag = new ExtrudeDragState(Pick, baseOffset: 0f);

        drag.Update(2.4f);
        drag.Update(0.1f);

        Assert.Equal(0, drag.PendingLayers);
        Assert.Equal(0, drag.ResolveLayers());
    }

    /// <summary>Update answers whether the count changed — the preview's rebuild signal.</summary>
    [Fact]
    public void UpdateReportsOnlyChanges()
    {
        var drag = new ExtrudeDragState(Pick, baseOffset: 0f);

        Assert.False(drag.Update(0.2f));
        Assert.True(drag.Update(0.8f));
        Assert.False(drag.Update(1.2f));
        Assert.True(drag.Update(1.8f));
    }

    /// <summary>
    /// Halves round away from zero, stated because Godot's own rounding has been
    /// caught doing banker's halves — 2.5 cells of pull is 3 layers, not 2.
    /// </summary>
    [Fact]
    public void HalvesRoundAwayFromZero()
    {
        var drag = new ExtrudeDragState(Pick, baseOffset: 0f);

        drag.Update(2.5f);

        Assert.Equal(3, drag.PendingLayers);

        drag.Update(-2.5f);

        Assert.Equal(-3, drag.PendingLayers);
    }
}
