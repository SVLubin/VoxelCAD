using System;
using Xunit;
using Godot;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The rule a stack of elements under one pixel is walked by. Held on its own,
/// away from the session and the scene, because it is the whole of what "click
/// again to go deeper" means.
/// </summary>
public sealed class SelectionCycleTests
{
    private static readonly Guid Near = Guid.NewGuid();
    private static readonly Guid Middle = Guid.NewGuid();
    private static readonly Guid Far = Guid.NewGuid();

    private static readonly Vector2 Spot = new(200f, 150f);

    private readonly SelectionCycle _cycle = new();

    [Fact]
    public void TheFirstClickTakesTheNearest() =>
        Assert.Equal(Near, _cycle.Choose(new[] { Near, Far }, Spot, selected: null));

    [Fact]
    public void ClickingNothingChoosesNothing() =>
        Assert.Null(_cycle.Choose(Array.Empty<Guid>(), Spot, selected: null));

    [Fact]
    public void TheSameSpotWalksTheStack()
    {
        Guid[] hits = { Near, Middle, Far };

        Assert.Equal(Near, _cycle.Choose(hits, Spot, selected: null));
        Assert.Equal(Middle, _cycle.Choose(hits, Spot, Near));
        Assert.Equal(Far, _cycle.Choose(hits, Spot, Middle));
        Assert.Equal(Near, _cycle.Choose(hits, Spot, Far));
    }

    /// <summary>
    /// A hand holding a mouse moves it a pixel between two clicks meant for one
    /// place, so the same spot cannot mean the same float.
    /// </summary>
    [Fact]
    public void ANudgeIsStillTheSameSpot()
    {
        Guid[] hits = { Near, Far };

        _cycle.Choose(hits, Spot, selected: null);

        Assert.Equal(Far, _cycle.Choose(hits, Spot + new Vector2(1f, 1f), Near));
    }

    [Fact]
    public void AClickElsewhereStartsOver()
    {
        Guid[] hits = { Near, Far };

        _cycle.Choose(hits, Spot, selected: null);

        Assert.Equal(Near, _cycle.Choose(hits, Spot + new Vector2(60f, 0f), Near));
    }

    /// <summary>
    /// The walk steps from what is selected now rather than from what this last
    /// answered, and a selection this ray never crossed starts it over. (A selection
    /// made elsewhere ends the walk before it gets here: the presenter forgets the
    /// spot — see FocusClickTests.)
    /// </summary>
    [Fact]
    public void SomethingSelectedElsewhereRestartsTheWalk()
    {
        Guid[] hits = { Near, Middle, Far };

        _cycle.Choose(hits, Spot, selected: null);

        Assert.Equal(Near, _cycle.Choose(hits, Spot, Guid.NewGuid()));
    }

    /// <summary>One element under the pointer is one answer, however often it is clicked.</summary>
    [Fact]
    public void ALoneElementStaysSelected()
    {
        Guid[] hits = { Near };

        Assert.Equal(Near, _cycle.Choose(hits, Spot, selected: null));
        Assert.Equal(Near, _cycle.Choose(hits, Spot, Near));
    }
}
