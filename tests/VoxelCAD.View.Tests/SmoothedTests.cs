using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.CommonTypes;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The number every animated quantity in the chrome is made of. What is held
/// here is what makes a hand moving quickly harmless: no start value to restart
/// from, no overshoot, and the same wall-clock time landing in the same place
/// whatever the frame rate.
/// </summary>
public sealed class SmoothedTests
{
    private const float Frame = 1f / 60f;

    /// <summary>The pace the editor ships with, which is what these hold.</summary>
    private static readonly MotionTiming Fast = new();

    /// <summary>
    /// What a panel travels when it opens out of its tab: the surface it grows
    /// to, notch included, less the tab's own square. The one distance this
    /// chrome is calibrated on, and comfortably inside the band where the speed
    /// governs rather than either clamp.
    /// </summary>
    private const float PanelOpen =
        ChromePanel.SurfaceWidth + EdgeColumn.NotchReach - ChromePanel.CollapsedSide;

    [Fact]
    public void TheFirstTargetArrivesWithoutMotion()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.Retarget(120f);

        Assert.Equal(120f, value.Value);
        Assert.True(value.IsSettled);
    }

    [Fact]
    public void ItApproachesAndThenSettlesExactly()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.SnapTo(0f);
        value.Retarget(100f);

        for (var tick = 0; tick < 120; tick++)
            value.Tick(Frame);

        Assert.Equal(100f, value.Value);
        Assert.True(value.IsSettled);
    }

    [Fact]
    public void ItNeverOvershoots()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.SnapTo(0f);
        value.Retarget(100f);

        for (var tick = 0; tick < 200; tick++)
        {
            value.Tick(Frame);

            Assert.InRange(value.Value, 0f, 100f);
        }
    }

    /// <summary>
    /// Retargeting mid-flight is the ordinary case under a quick hand, and it
    /// has to continue from where the value stands rather than restart.
    /// </summary>
    [Fact]
    public void ARetargetMidFlightContinuesFromWhereItStands()
    {
        // A panel opening out of its tab, which is the one motion this chrome
        // is judged by and the one distance inside the constant-speed band. A
        // hundred units is below the floor's knee now and arrives in three
        // frames, so there would be no mid-flight left to catch.
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.SnapTo(0f);
        value.Retarget(PanelOpen);

        for (var tick = 0; tick < 3; tick++)
            value.Tick(Frame);

        float halfway = value.Value;
        Assert.InRange(halfway, 1f, PanelOpen - 1f);

        value.Retarget(0f);
        value.Tick(Frame);

        Assert.InRange(value.Value, 0f, halfway);
    }

    /// <summary>
    /// A target flipped every frame — a pointer sweeping across a tab — must
    /// leave a value that merely follows, never one that rings.
    /// </summary>
    [Fact]
    public void AFlippingTargetLeavesNoWobble()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.SnapTo(0f);

        for (var tick = 0; tick < 200; tick++)
        {
            value.Retarget(tick % 2 == 0 ? 100f : 0f);
            value.Tick(Frame);

            Assert.InRange(value.Value, 0f, 100f);
        }
    }

    [Fact]
    public void TheSameElapsedTimeLandsInTheSamePlaceAtAnyFrameRate()
    {
        var fast = new Smoothed(MotionSpeed.Length, Fast);
        var slow = new Smoothed(MotionSpeed.Length, Fast);

        fast.SnapTo(0f);
        slow.SnapTo(0f);
        fast.Retarget(100f);
        slow.Retarget(100f);

        for (var tick = 0; tick < 12; tick++)
            fast.Tick(1f / 120f);

        for (var tick = 0; tick < 3; tick++)
            slow.Tick(1f / 30f);

        Assert.True(
            Math.Abs(fast.Value - slow.Value) < 1.5f,
            $"120 fps reached {fast.Value}, 30 fps reached {slow.Value}");
    }

    [Fact]
    public void ASnapEndsWhateverWasHappening()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.SnapTo(0f);
        value.Retarget(100f);
        value.Tick(Frame);
        value.SnapTo(42f);

        Assert.Equal(42f, value.Value);
        Assert.True(value.IsSettled);

        value.Tick(Frame);

        Assert.Equal(42f, value.Value);
    }

    [Fact]
    public void AFrameOfNoTimeChangesNothing()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.SnapTo(0f);
        value.Retarget(100f);
        value.Tick(0f);

        Assert.Equal(0f, value.Value);
    }

    /// <summary>
    /// The question a caller asks to mean "is this the first time" — and the
    /// point of asking the number rather than asking which way something was
    /// aimed last, which answers yes again on every reversal. A panel reopened
    /// while it was still closing was teleported back into its icon on exactly
    /// that mistake.
    /// </summary>
    /// <summary>
    /// The reason the curve is timed rather than exponential: it <em>ends</em>.
    /// An approach that closes a fraction of the remaining gap per frame never
    /// arrives, so its last few units crawl over several frames after the motion
    /// is visually over — which is exactly where the eye caught, on the last
    /// sliver of a title being uncovered.
    /// </summary>
    [Fact]
    public void ItArrivesWithinABoundedNumberOfFrames()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.SnapTo(0f);
        value.Retarget(100f);

        for (var tick = 0; tick < 8; tick++)
            value.Tick(Frame);

        Assert.Equal(100f, value.Value);
        Assert.True(value.IsSettled);
    }

    /// <summary>
    /// And it is still moving usefully at the end rather than trickling: most
    /// of the last stretch is covered in the last couple of frames, not spread
    /// over a tail.
    /// </summary>
    [Fact]
    public void TheLastFramesStillCoverGround()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.SnapTo(0f);
        value.Retarget(PanelOpen);

        // One frame short of the end of a panel's open, which runs 7.2 frames.
        for (var tick = 0; tick < 6; tick++)
            value.Tick(Frame);

        float beforeTheEnd = value.Value;

        Assert.True(
            beforeTheEnd < PanelOpen - 1f,
            $"nothing left to cover: already at {beforeTheEnd} six frames in");
    }

    /// <summary>
    /// Targets measured from live content jitter by a fraction of a unit, and
    /// restarting the clock on those would hold it at zero for ever and turn
    /// the curve back into the crawl it replaced.
    /// </summary>
    [Fact]
    public void AHairsBreadthOfDriftDoesNotRestartTheCurve()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.SnapTo(0f);
        value.Retarget(100f);

        for (var tick = 0; tick < 20; tick++)
        {
            value.Retarget(100f + (tick % 2 == 0 ? 0.2f : -0.2f));
            value.Tick(Frame);
        }

        Assert.True(value.IsSettled, "the clock was restarted by noise");
        Assert.InRange(value.Value, 99f, 101f);
    }

    [Fact]
    public void ItHasNotStartedUntilItIsAimed()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        Assert.False(value.IsStarted);

        value.Retarget(120f);

        Assert.True(value.IsStarted);
    }

    [Fact]
    public void StartingSurvivesEveryReversal()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.Retarget(0f);
        value.Retarget(100f);
        value.Tick(Frame);
        value.Retarget(0f);

        Assert.True(value.IsStarted);
    }

    [Fact]
    public void ARectangleHasNotStartedUntilItIsAimed()
    {
        var rect = new RectMotion(Fast);

        Assert.False(rect.IsStarted);

        rect.Retarget(new Godot.Rect2(1f, 2f, 3f, 4f));

        Assert.True(rect.IsStarted);
    }

    [Fact]
    public void SnappingAlsoStartsARectangle()
    {
        var rect = new RectMotion(Fast);

        rect.SnapTo(new Godot.Rect2(0f, 0f, 30f, 30f));

        Assert.True(rect.IsStarted);
    }
}

/// <summary>
/// A rectangle that moves as one, and the two questions it can be asked about
/// having stopped.
/// </summary>
public sealed class RectMotionTests
{
    private const float Frame = 1f / 60f;

    /// <summary>The pace the editor ships with, which is what these hold.</summary>
    private static readonly MotionTiming Fast = new();

    private static RectMotion Settled(Rect2 rect)
    {
        var motion = new RectMotion(Fast);

        motion.SnapTo(rect);

        return motion;
    }

    /// <summary>
    /// Being collapsed is a fact about size, not about position: a tab sliding
    /// to a new place in the run is a tab the whole way. Asked of the whole
    /// rect it briefly was not one, and everything a tab hides — its title, its
    /// chevron, its pin — came back for the length of the slide, laid out
    /// against the width it last settled at. Two units of the first letter
    /// showed past the tab's own clip, which is how it was reported.
    /// </summary>
    [Fact]
    public void ASlideLeavesTheSizeSettled()
    {
        RectMotion motion = Settled(new Rect2(4f, 40f, 30f, 30f));

        motion.Retarget(new Rect2(4f, 400f, 30f, 30f));

        Assert.False(motion.IsSettled);
        Assert.True(motion.IsSizeSettled);
    }

    [Fact]
    public void AResizeDoesNot()
    {
        RectMotion motion = Settled(new Rect2(4f, 40f, 30f, 30f));

        motion.Retarget(new Rect2(4f, 40f, 322f, 300f));

        Assert.False(motion.IsSizeSettled);
    }

    /// <summary>And a size that has arrived reports so, mid-slide or not.</summary>
    [Fact]
    public void AResizeThatFinishesReportsSettledWhileStillTravelling()
    {
        RectMotion motion = Settled(new Rect2(4f, 40f, 322f, 300f));

        motion.Retarget(new Rect2(4f, 40f, 30f, 30f));

        for (var frame = 0; frame < 60; frame++)
            motion.Tick(Frame);

        Assert.True(motion.IsSizeSettled);

        motion.Retarget(new Rect2(4f, 700f, 30f, 30f));

        Assert.True(motion.IsSizeSettled);
        Assert.False(motion.IsSettled);
    }

    /// <summary>
    /// Carrying moves a rectangle and does not touch its size — which is what
    /// lets the column ask a carried surface what it <em>is</em> instead of
    /// keeping a flag beside it saying so. A panel carried across the window's
    /// middle is rebuilt as a fresh slot that only ever reaches the carry path,
    /// so any flag the other paths raise is lowered on the far side: the open
    /// panel wore a tab's dress mid-crossing, its title, chevron and pin gone.
    /// </summary>
    [Fact]
    public void CarryingKeepsTheSizeAndLeavesItSettled()
    {
        RectMotion motion = Settled(new Rect2(4f, 40f, 322f, 90f));

        for (var step = 0; step < 20; step++)
        {
            motion.CarryTo(new Vector2(4f + (step * 37f), 40f + (step * 11f)));
            motion.Tick(Frame);

            Assert.True(motion.IsSizeSettled);
            Assert.Equal(new Vector2(322f, 90f), motion.Snapped.Size);
        }
    }
}

/// <summary>
/// Motion measured in speed rather than in time, and the two clamps that bound
/// it. Every number here is the request stated as an assertion: "not in a fixed
/// time but at a fixed speed".
/// </summary>
public sealed class MotionSpeedTests
{
    private const float Frame = 1f / 60f;

    /// <summary>The pace the editor ships with, which is what these hold.</summary>
    private static readonly MotionTiming Fast = new();

    private const float PanelOpen =
        ChromePanel.SurfaceWidth + EdgeColumn.NotchReach - ChromePanel.CollapsedSide;

    /// <summary>
    /// The calibration, pinned so nobody restyles the chrome by nudging a
    /// speed: a panel opening takes what it took under the one fixed duration
    /// it replaced.
    /// </summary>
    [Fact]
    public void APanelOpenStillTakesWhatItAlwaysTook()
    {
        Assert.Equal(
            0.1193f, Smoothed.DurationFor(PanelOpen, MotionSpeed.Length, Fast), 3);

        Assert.Equal(0.0833f, Smoothed.DurationFor(1f, MotionSpeed.Opacity, Fast), 3);
    }

    /// <summary>
    /// Twice the ground, twice the time — the whole of "at a fixed speed",
    /// inside the band where neither clamp governs.
    /// </summary>
    [Theory]
    [InlineData(200f)]
    [InlineData(275f)]
    public void TwiceTheDistanceTakesTwiceTheTime(float distance)
    {
        float once = Smoothed.DurationFor(distance, MotionSpeed.Length, Fast);
        float twice = Smoothed.DurationFor(distance * 2f, MotionSpeed.Length, Fast);

        Assert.Equal(once * 2f, twice, 5);
    }

    /// <summary>
    /// A short move is floored and a long one capped, and the knees are exact.
    /// Below the floor a motion is not one; above the cap a canvas unit's
    /// changing physical size would make the chrome slower on a bigger monitor.
    /// </summary>
    [Fact]
    public void AShortMoveIsFlooredAndALongOneIsCapped()
    {
        Assert.Equal(0.05f, Smoothed.DurationFor(EdgeColumn.NotchReach, MotionSpeed.Length, Fast), 5);
        Assert.Equal(0.2f, Smoothed.DurationFor(653f, MotionSpeed.Length, Fast), 5);

        // The knees: 137.5 units and 550.
        Assert.Equal(0.05f, Smoothed.DurationFor(137.5f, MotionSpeed.Length, Fast), 4);
        Assert.Equal(0.2f, Smoothed.DurationFor(550f, MotionSpeed.Length, Fast), 4);
    }

    /// <summary>
    /// The complaint, as an assertion. Under one fixed duration the return leg
    /// over a single frame's ground cost a whole transition; it costs what is
    /// left now, because the distance is measured from where the value stands.
    /// </summary>
    [Fact]
    public void AReversalCostsWhatIsLeft()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.SnapTo(0f);
        value.Retarget(PanelOpen);
        value.Tick(Frame);

        value.Retarget(0f);

        int frames = FramesToSettle(value);

        Assert.True(
            frames <= 3,
            $"coming home from one frame out took {frames} frames");

        // And from settled, the return is the outbound: the same ground.
        var whole = new Smoothed(MotionSpeed.Length, Fast);

        whole.SnapTo(0f);
        whole.Retarget(PanelOpen);

        int outbound = FramesToSettle(whole);

        whole.Retarget(0f);

        Assert.Equal(outbound, FramesToSettle(whole));
    }

    /// <summary>
    /// The formal statement of "at a fixed speed": inside the band the step a
    /// frame takes is bounded and does not depend on the distance. A sine
    /// ease-out over <c>D / speed</c> leaves at <c>(π/2)·speed</c> whatever D
    /// is, so the bound is one number. This is what catches a future change to
    /// the ease shape.
    /// </summary>
    [Fact]
    public void ThePerFrameStepIsBoundedInsideTheBand()
    {
        float bound = (MathF.PI / 2f) * MotionSpeed.Length.UnitsPerSecond * Frame;

        for (float distance = 1f; distance <= 550f; distance += 1f)
        {
            var value = new Smoothed(MotionSpeed.Length, Fast);

            value.SnapTo(0f);
            value.Retarget(distance);

            float previous = 0f;

            for (var tick = 0; tick < 20; tick++)
            {
                value.Tick(Frame);

                Assert.True(
                    value.Value - previous <= bound + 0.01f,
                    $"a step of {value.Value - previous} over {distance} units");

                previous = value.Value;
            }
        }
    }

    /// <summary>
    /// A target restated unchanged every frame must still settle. Nothing
    /// asserted this, and it is what stops the chrome deadlocking: a motion
    /// that never settles is a panel that can never be picked up, IsStill being
    /// what TryGetDraggable asks.
    /// </summary>
    [Fact]
    public void AnUnchangedTargetRestatedEveryFrameStillSettles()
    {
        var value = new Smoothed(MotionSpeed.Length, Fast);

        value.SnapTo(0f);
        value.Retarget(PanelOpen);

        for (var tick = 0; tick < 600; tick++)
        {
            value.Retarget(PanelOpen);
            value.Tick(Frame);
        }

        Assert.True(value.IsSettled);
        Assert.Equal(PanelOpen, value.Value, 3);
    }

    /// <summary>
    /// The regression test for a defect that shipped: the drop hint's whole
    /// range is 0.14 of an alpha, and it was carried by a number whose "beneath
    /// noticing" was half a unit — so every step it could take was an
    /// assignment and it never faded at all, in either direction, while its own
    /// comment and the canon both said it did.
    /// </summary>
    /// <remarks>
    /// It must be written against the opacity preset. Written against
    /// <see cref="MotionSpeed.Length"/> it passes with the defect present and
    /// proves nothing.
    /// </remarks>
    [Fact]
    public void AnAlphaFadesRatherThanSwitching()
    {
        var alpha = new Smoothed(MotionSpeed.Opacity, Fast);

        alpha.SnapTo(0f);
        alpha.Retarget(0.05f);
        alpha.Tick(Frame);

        Assert.InRange(alpha.Value, 0.0001f, 0.0499f);

        alpha.Retarget(0.14f);
        alpha.Tick(Frame);

        Assert.InRange(alpha.Value, 0.0501f, 0.1399f);
    }

    /// <summary>
    /// Smoothly, but very fast: a full fade is five frames at 60 Hz.
    /// </summary>
    [Fact]
    public void AFullFadeIsFiveFramesAtSixtyHertz()
    {
        var alpha = new Smoothed(MotionSpeed.Opacity, Fast);

        alpha.SnapTo(0f);
        alpha.Retarget(1f);

        Assert.Equal(5, FramesToSettle(alpha));
    }

    /// <summary>
    /// An edge merely coasting on its own clock must not set the pace for a
    /// small change beside it. Without this the change is a regression on the
    /// very complaint it answers.
    /// </summary>
    [Fact]
    public void ACoastingEdgeDoesNotSetTheClock()
    {
        var motion = new RectMotion(Fast);

        motion.SnapTo(new Rect2(0f, 0f, 100f, 100f));
        motion.Retarget(new Rect2(0f, 400f, 100f, 100f));

        // Mid-slide, with most of the 400 units still to run, a twenty-unit
        // change of height is a twenty-unit motion — not a four-hundred one.
        motion.Tick(Frame);

        Assert.Equal(
            0.05f,
            Smoothed.DurationFor(
                motion.RestartTravel(new Rect2(0f, 400f, 100f, 120f)),
                MotionSpeed.Length,
                Fast),
            5);
    }

    /// <summary>
    /// A rectangle is one shape, so its four edges share a clock — and the same
    /// gesture takes the same time on both columns. Under any norm over all
    /// four edges a mirrored open (whose far edge is fixed, so the position
    /// travels too) would visibly outlast a left one.
    /// </summary>
    [Fact]
    public void AMirroredOpenTakesAsLongAsALeftOne()
    {
        var left = new RectMotion(Fast);
        var right = new RectMotion(Fast);

        left.SnapTo(new Rect2(4f, 0f, ChromePanel.CollapsedSide, 200f));
        right.SnapTo(new Rect2(326f - ChromePanel.CollapsedSide, 0f, ChromePanel.CollapsedSide, 200f));

        // Left grows from its near edge; right keeps its far edge and so moves.
        left.Retarget(new Rect2(4f, 0f, ChromePanel.CollapsedSide + PanelOpen, 200f));
        right.Retarget(
            new Rect2(326f - ChromePanel.CollapsedSide - PanelOpen, 0f,
                ChromePanel.CollapsedSide + PanelOpen, 200f));

        var frames = 0;

        while (!left.IsSettled || !right.IsSettled)
        {
            left.Tick(Frame);
            right.Tick(Frame);
            frames++;

            Assert.True(frames < 200, "neither settled");
            Assert.Equal(left.IsSettled, right.IsSettled);
        }
    }

    private static int FramesToSettle(Smoothed value)
    {
        var frames = 0;

        while (!value.IsSettled && frames < 200)
        {
            value.Tick(Frame);
            frames++;
        }

        return frames;
    }
}

/// <summary>
/// The editor's pace: whether it animates at all, and how long a transition
/// takes as a multiple of the shipped one.
/// </summary>
public sealed class MotionTimingTests
{
    private const float Frame = 1f / 60f;

    private const float PanelOpen =
        ChromePanel.SurfaceWidth + EdgeColumn.NotchReach - ChromePanel.CollapsedSide;

    private static MotionTiming At(AnimationSpeed speed)
    {
        var timing = new MotionTiming();

        timing.Set(enabled: true, speed);

        return timing;
    }

    private static MotionTiming Off()
    {
        var timing = new MotionTiming();

        timing.Set(enabled: false, AnimationSpeed.Fast);

        return timing;
    }

    /// <summary>
    /// The pace multiplies the <b>finished</b> duration. The two clamped
    /// distances are in this sweep by name: they are the ones a multiplier
    /// applied to the distance-over-speed term alone would leave untouched, and
    /// they are the chrome's two commonest motions.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(36f)]
    [InlineData(137.5f)]
    [InlineData(328f)]
    [InlineData(550f)]
    [InlineData(653f)]
    [InlineData(5000f)]
    public void ThePaceMultipliesTheFinishedDuration(float distance)
    {
        float fast = Smoothed.DurationFor(distance, MotionSpeed.Length, At(AnimationSpeed.Fast));

        Assert.Equal(
            fast * 1.5f,
            Smoothed.DurationFor(distance, MotionSpeed.Length, At(AnimationSpeed.Medium)),
            5);

        Assert.Equal(
            fast * 2.25f,
            Smoothed.DurationFor(distance, MotionSpeed.Length, At(AnimationSpeed.Slow)),
            5);
    }

    /// <summary>An alpha is paced by the same factor, on its own speed.</summary>
    [Fact]
    public void AFadeIsPacedToo() =>
        Assert.Equal(
            2.25f * Smoothed.DurationFor(1f, MotionSpeed.Opacity, At(AnimationSpeed.Fast)),
            Smoothed.DurationFor(1f, MotionSpeed.Opacity, At(AnimationSpeed.Slow)),
            5);

    /// <summary>
    /// The clock a caller computed must not be cut back to the shipped ceiling
    /// when it is handed over — or every pace above Fast silently does nothing
    /// past 550 units, which is the path every rect and every notch takes.
    /// </summary>
    [Fact]
    public void ACallerSetClockIsNotCutBackToTheShippedCeiling()
    {
        MotionTiming slow = At(AnimationSpeed.Slow);
        var value = new Smoothed(MotionSpeed.Length, slow);

        value.SnapTo(0f);
        value.RetargetOver(653f, Smoothed.DurationFor(653f, MotionSpeed.Length, slow));

        var frames = 0;

        while (!value.IsSettled && frames < 200)
        {
            value.Tick(Frame);
            frames++;
        }

        // 450 ms, not the shipped 200.
        Assert.InRange(frames * Frame, 0.44f, 0.46f);
    }

    /// <summary>
    /// Off is instant, and instant means arrived: everything the chrome asks of
    /// a motion — is this tab collapsed, may this panel be picked up — rests on
    /// IsSettled being true with no tick at all.
    /// </summary>
    [Fact]
    public void NothingEasesWhenAnimationIsOff()
    {
        var value = new Smoothed(MotionSpeed.Length, Off());

        value.SnapTo(0f);
        value.Retarget(PanelOpen);

        Assert.Equal(PanelOpen, value.Value, 5);
        Assert.True(value.IsSettled);
        Assert.True(value.IsStarted);
    }

    /// <summary>
    /// And a motion already in flight when the setting is switched off lands on
    /// the next tick, rather than finishing at the old pace. The case nobody
    /// would think to write.
    /// </summary>
    [Fact]
    public void SwitchingItOffLandsAMotionAlreadyInFlight()
    {
        var timing = new MotionTiming();
        var value = new Smoothed(MotionSpeed.Length, timing);

        value.SnapTo(0f);
        value.Retarget(PanelOpen);
        value.Tick(Frame);

        Assert.False(value.IsSettled);

        timing.Set(enabled: false, AnimationSpeed.Fast);
        value.Tick(Frame);

        Assert.Equal(PanelOpen, value.Value, 5);
        Assert.True(value.IsSettled);
    }

    /// <summary>A rect and a shape are instant on the same terms.</summary>
    [Fact]
    public void ARectAndAShapeAreInstantToo()
    {
        MotionTiming off = Off();
        var rect = new RectMotion(off);
        var shape = new ShapeMotion(off);

        rect.SnapTo(new Rect2(0f, 0f, 30f, 30f));
        rect.Retarget(new Rect2(4f, 400f, 322f, 200f));

        Assert.True(rect.IsSettled);
        Assert.True(rect.IsSizeSettled);
        Assert.Equal(new Rect2(4f, 400f, 322f, 200f), rect.Snapped);

        shape.SnapTo(new Rect2(0f, 0f, 30f, 30f), 0f);
        shape.Retarget(new Rect2(4f, 4f, 358f, 200f), EdgeColumn.NotchReach);

        Assert.True(shape.IsSettled);
        Assert.Equal(EdgeColumn.NotchReach, shape.Cut, 3);
    }

    /// <summary>
    /// The knees stay where they are: the pace stretches the whole curve, it
    /// does not move where the clamps take over.
    /// </summary>
    [Theory]
    [InlineData(AnimationSpeed.Fast)]
    [InlineData(AnimationSpeed.Medium)]
    [InlineData(AnimationSpeed.Slow)]
    public void TheKneesDoNotMoveWithThePace(AnimationSpeed speed)
    {
        MotionTiming timing = At(speed);

        Assert.Equal(
            Smoothed.DurationFor(1f, MotionSpeed.Length, timing),
            Smoothed.DurationFor(137.5f, MotionSpeed.Length, timing),
            5);

        Assert.Equal(
            Smoothed.DurationFor(550f, MotionSpeed.Length, timing),
            Smoothed.DurationFor(5000f, MotionSpeed.Length, timing),
            5);
    }

    /// <summary>
    /// The step a frame takes falls with the pace, which is the arithmetic form
    /// of "slower". 72 units at Fast, 48 at Medium, 32 at Slow.
    /// </summary>
    [Theory]
    [InlineData(AnimationSpeed.Fast, 1f)]
    [InlineData(AnimationSpeed.Medium, 1.5f)]
    [InlineData(AnimationSpeed.Slow, 2.25f)]
    public void TheStepPerFrameFallsWithThePace(AnimationSpeed speed, float factor)
    {
        var value = new Smoothed(MotionSpeed.Length, At(speed));

        value.SnapTo(0f);
        value.Retarget(PanelOpen);

        float bound =
            ((MathF.PI / 2f) * MotionSpeed.Length.UnitsPerSecond * Frame / factor) + 0.01f;
        float previous = 0f;

        for (var tick = 0; tick < 40; tick++)
        {
            value.Tick(Frame);

            Assert.True(
                value.Value - previous <= bound,
                $"a step of {value.Value - previous} against a bound of {bound}");

            previous = value.Value;
        }
    }

    /// <summary>
    /// Quiet is what the output cannot show — a fact about the rasterizer, not
    /// about time — so no pace may touch it.
    /// </summary>
    [Theory]
    [InlineData(AnimationSpeed.Fast)]
    [InlineData(AnimationSpeed.Slow)]
    public void QuietIsNotAPace(AnimationSpeed speed)
    {
        var value = new Smoothed(MotionSpeed.Length, At(speed));

        value.SnapTo(100f);

        Assert.Equal(0f, value.RestartTravel(100.4f), 5);
        Assert.Equal(50f, value.RestartTravel(150f), 5);
    }

    /// <summary>
    /// Every position is a positive finite factor, and the three are distinct —
    /// so a member added tomorrow fails here rather than inheriting Fast by
    /// silence, which is the lesson a fallback switch has already cost once.
    /// </summary>
    [Fact]
    public void EveryPaceIsAPositiveFiniteFactorAndTheyAreDistinct()
    {
        var factors = new List<float>();

        foreach (AnimationSpeed speed in Enum.GetValues<AnimationSpeed>())
        {
            float seconds = Smoothed.DurationFor(328f, MotionSpeed.Length, At(speed));

            Assert.True(float.IsFinite(seconds) && seconds > 0f);
            factors.Add(seconds);
        }

        Assert.Equal(factors.Count, factors.Distinct().Count());
    }

    /// <summary>The view cube's turn follows the same pace, and stops at nought.</summary>
    [Fact]
    public void TheViewCubeTurnFollowsThePace()
    {
        Assert.Equal(0.3f, At(AnimationSpeed.Fast).ScaleFixed(0.3f), 5);
        Assert.Equal(0.45f, At(AnimationSpeed.Medium).ScaleFixed(0.3f), 5);
        Assert.Equal(0.675f, At(AnimationSpeed.Slow).ScaleFixed(0.3f), 5);
        Assert.Equal(0f, Off().ScaleFixed(0.3f), 5);
    }
}
