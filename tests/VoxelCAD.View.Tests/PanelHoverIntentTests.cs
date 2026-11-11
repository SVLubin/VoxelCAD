using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The Arc-style hover rules: a dwell opens, a graced absence closes, and
/// nothing closes while the hand is still in the panel by any measure.
/// </summary>
public sealed class PanelHoverIntentTests
{
    private const float Frame = 1f / 60f;

    /// <summary>
    /// The shipped pace, at which <see cref="MotionTiming.ScaleFixed"/> is the
    /// identity — so every rule below reads in the constants it is about.
    /// </summary>
    private readonly MotionTiming _timing = new();

    private readonly PanelHoverIntent _intent;

    public PanelHoverIntentTests() => _intent = new PanelHoverIntent(_timing);

    [Fact]
    public void ADwellOnATabOpensItOnce()
    {
        PanelKind? opened = null;
        var elapsed = 0f;

        while (elapsed < PanelHoverIntent.OpenDwellSeconds + 0.1f && opened is null)
        {
            opened = _intent.Tick(Frame, PanelKind.History, null, false, false).Open;
            elapsed += Frame;
        }

        Assert.Equal(PanelKind.History, opened);
        Assert.InRange(elapsed, PanelHoverIntent.OpenDwellSeconds - 0.02f, PanelHoverIntent.OpenDwellSeconds + 0.05f);

        // The next tick does not fire again for the same standing hover.
        Assert.Null(_intent.Tick(Frame, PanelKind.History, PanelKind.History, false, false).Open);
    }

    [Fact]
    public void CrossingATabOnTheWayElsewhereOpensNothing()
    {
        for (var tick = 0; tick < 6; tick++)
            Assert.Null(_intent.Tick(Frame, PanelKind.History, null, false, false).Open);

        Assert.Null(_intent.Tick(Frame, null, null, false, false).Open);
    }

    [Fact]
    public void SwitchingTabsRestartsTheDwellAndOpensTheOther()
    {
        for (var tick = 0; tick < 10; tick++)
            _intent.Tick(Frame, PanelKind.History, null, false, false);

        PanelKind? opened = null;
        var elapsed = 0f;

        while (elapsed < PanelHoverIntent.OpenDwellSeconds + 0.1f && opened is null)
        {
            opened = _intent.Tick(Frame, PanelKind.Project, null, false, false).Open;
            elapsed += Frame;
        }

        Assert.Equal(PanelKind.Project, opened);
        Assert.True(elapsed >= PanelHoverIntent.OpenDwellSeconds - 0.02f);
    }

    [Fact]
    public void LeavingTheOpenPanelClosesItAfterTheGrace()
    {
        var closed = false;
        var elapsed = 0f;

        while (elapsed < PanelHoverIntent.CloseGraceSeconds + 0.1f && !closed)
        {
            closed = _intent.Tick(Frame, null, PanelKind.History, false, false).Close;
            elapsed += Frame;
        }

        Assert.True(closed);
        Assert.True(elapsed >= PanelHoverIntent.CloseGraceSeconds - 0.02f);
    }

    [Fact]
    public void StandingOnThePanelOrItsTabResetsTheGrace()
    {
        for (var tick = 0; tick < 15; tick++)
            _intent.Tick(Frame, null, PanelKind.History, false, false);

        // Back inside for one tick — the countdown starts over.
        _intent.Tick(Frame, null, PanelKind.History, true, false);

        var closed = false;
        var elapsed = 0f;

        while (elapsed < PanelHoverIntent.CloseGraceSeconds - 0.05f)
        {
            closed |= _intent.Tick(Frame, null, PanelKind.History, false, false).Close;
            elapsed += Frame;
        }

        Assert.False(closed);
    }

    [Fact]
    public void AHeldInteractionBlocksClosingIndefinitely()
    {
        for (var tick = 0; tick < 120; tick++)
        {
            Assert.False(
                _intent.Tick(Frame, null, PanelKind.History, false, true).Close);
        }
    }

    /// <summary>
    /// A click leaves the pointer sitting on the button that answered it —
    /// without the latch, unpinning hands the panel straight back as a glance.
    /// </summary>
    [Fact]
    public void ASuppressedPanelDoesNotOpenWhileThePointerStays()
    {
        _intent.SuppressUntilPointerLeaves(PanelKind.Project);

        for (var tick = 0; tick < 120; tick++)
            Assert.Null(_intent.Tick(Frame, PanelKind.Project, null, false, false).Open);
    }

    [Fact]
    public void LeavingAndReturningLiftsTheSuppression()
    {
        _intent.SuppressUntilPointerLeaves(PanelKind.Project);

        for (var tick = 0; tick < 30; tick++)
            _intent.Tick(Frame, PanelKind.Project, null, false, false);

        // Away for a single tick is enough — the hand moved, which is the whole
        // condition; nothing here is timed.
        _intent.Tick(Frame, null, null, false, false);

        PanelKind? opened = null;
        var elapsed = 0f;

        while (elapsed < 1f && opened is null)
        {
            opened = _intent.Tick(Frame, PanelKind.Project, null, false, false).Open;
            elapsed += Frame;
        }

        Assert.Equal(PanelKind.Project, opened);
    }

    [Fact]
    public void SuppressionHoldsOnlyThePanelItNames()
    {
        _intent.SuppressUntilPointerLeaves(PanelKind.Project);

        PanelKind? opened = null;
        var elapsed = 0f;

        while (elapsed < 1f && opened is null)
        {
            opened = _intent.Tick(Frame, PanelKind.History, null, false, false).Open;
            elapsed += Frame;
        }

        Assert.Equal(PanelKind.History, opened);
    }

    /// <summary>
    /// With animation switched off there is no dwell at all: the frame the
    /// pointer arrives on a tab is the frame it opens. The whole of "as if the
    /// animations had never been built" — a surface that lands at once, then a
    /// fifth of a second of nothing, is the delay the setting exists to remove.
    /// </summary>
    [Fact]
    public void WithAnimationOffAGlanceOpensOnTheFirstTick()
    {
        _timing.Set(enabled: false, AnimationSpeed.Fast);

        Assert.Equal(
            PanelKind.History,
            _intent.Tick(Frame, PanelKind.History, null, false, false).Open);
    }

    /// <summary>And the retreat is the same sentence backwards.</summary>
    [Fact]
    public void WithAnimationOffLeavingClosesOnTheFirstTick()
    {
        _timing.Set(enabled: false, AnimationSpeed.Fast);

        Assert.True(_intent.Tick(Frame, null, PanelKind.History, false, false).Close);
    }

    /// <summary>
    /// It is off that is the special case, not zero: nothing opens on a frame
    /// the pointer is not on the tab, however short the dwell has become.
    /// </summary>
    [Fact]
    public void WithAnimationOffNothingOpensWithoutATabUnderThePointer()
    {
        _timing.Set(enabled: false, AnimationSpeed.Fast);

        Assert.Null(_intent.Tick(Frame, null, null, false, false).Open);
    }

    /// <summary>
    /// A slower editor makes up its mind more slowly, by the same factor every
    /// transition is stretched by — one knob, not two.
    /// </summary>
    [Fact]
    public void ASlowerPaceLengthensTheDwellByTheSameFactor()
    {
        _timing.Set(enabled: true, AnimationSpeed.Slow);

        var elapsed = 0f;

        while (elapsed < PanelHoverIntent.OpenDwellSeconds * 4f)
        {
            if (_intent.Tick(Frame, PanelKind.History, null, false, false).Open is not null)
                break;

            elapsed += Frame;
        }

        // 2.25x, which is what AnimationSpeed.Slow multiplies a duration by.
        Assert.InRange(
            elapsed,
            (PanelHoverIntent.OpenDwellSeconds * 2.25f) - 0.03f,
            (PanelHoverIntent.OpenDwellSeconds * 2.25f) + 0.03f);
    }

    [Fact]
    public void DwellingOnAnotherTabSupersedesTheClose()
    {
        // Away from the open History long enough to close, but standing on the
        // Project tab: the tick that opens Project must not also report close.
        HoverDecision decision = default;
        var elapsed = 0f;

        while (elapsed < 1f && decision.Open is null)
        {
            decision = _intent.Tick(Frame, PanelKind.Project, PanelKind.History, false, false);
            elapsed += Frame;
        }

        Assert.Equal(PanelKind.Project, decision.Open);
        Assert.False(decision.Close);
    }
}
