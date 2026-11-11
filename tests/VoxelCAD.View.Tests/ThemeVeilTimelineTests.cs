using System;
using System.Collections.Generic;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The clock a theme change hides behind. What earns a test here is where the
/// swap lands and where the brightness change lands — the rest of the veil is
/// paint, and paint is looked at.
/// </summary>
public sealed class ThemeVeilTimelineTests
{
    /// <summary>
    /// Under about five frames a phase stops being an animation and becomes a
    /// cut, and the difference is not subtle: the defocus at 0.045 s — under
    /// three frames — was reported as the window blurring <b>instantly</b>.
    /// </summary>
    /// <remarks>
    /// A floor rather than a fixed duration, because how long each phase should
    /// be is taste and this is not: whatever the four are tuned to, none of them
    /// may be tuned below the point where the eye stops reading it as motion.
    /// </remarks>
    [Fact]
    public void EveryPhaseIsLongEnoughToBeSeen()
    {
        const float floor = 5f / 60f;

        Assert.True(
            ThemeVeilTimeline.DefocusSeconds >= floor,
            $"the defocus is {ThemeVeilTimeline.DefocusSeconds * 60f:F1} frames");

        Assert.True(
            ThemeVeilTimeline.SettleSeconds >= floor,
            $"the settle is {ThemeVeilTimeline.SettleSeconds * 60f:F1} frames");

        Assert.True(
            ThemeVeilTimeline.DissolveSeconds >= floor,
            $"the dissolve is {ThemeVeilTimeline.DissolveSeconds * 60f:F1} frames");

        Assert.True(
            ThemeVeilTimeline.RefocusSeconds >= floor,
            $"the refocus is {ThemeVeilTimeline.RefocusSeconds * 60f:F1} frames");
    }

    [Fact]
    public void IdleUntilItIsStarted()
    {
        var timeline = new ThemeVeilTimeline();

        ThemeVeilStep step = timeline.Advance(1f);

        Assert.False(timeline.IsRunning);
        Assert.False(step.Swap);
        Assert.False(step.Finished);
    }

    /// <summary>
    /// The window is sharp and showing the frozen copy on the first frame, which
    /// is what makes putting the veil up invisible: the copy is what is already
    /// on screen, so anything else would be a change nobody asked for.
    /// </summary>
    [Fact]
    public void ItStartsSharpAndOnTheFrozenFrame()
    {
        var timeline = new ThemeVeilTimeline();
        timeline.Start();

        Assert.Equal(0f, timeline.Blur);
        Assert.Equal(0f, timeline.Blend);
    }

    /// <summary>
    /// The whole point of the class: whatever the frame rate, the look is never
    /// swapped anywhere the eye could catch it — fully out of focus, and with
    /// nothing of the live window on show.
    /// </summary>
    [Theory]
    [InlineData(1f / 240f)]
    [InlineData(1f / 60f)]
    [InlineData(1f / 30f)]
    [InlineData(0.017f)]
    [InlineData(0.049f)]
    public void TheSwapOnlyEverHappensOutOfSight(float delta)
    {
        var timeline = new ThemeVeilTimeline();
        timeline.Start();

        var swaps = 0;

        while (timeline.IsRunning)
        {
            ThemeVeilStep step = timeline.Advance(delta);

            if (!step.Swap)
                continue;

            swaps++;

            Assert.Equal(1f, step.Blur);
            Assert.Equal(0f, step.Blend);
        }

        Assert.Equal(1, swaps);
    }

    /// <summary>
    /// A hitch pauses the choreography rather than deleting the frames it
    /// covered: however long the step, the clock moves one capped tick, the
    /// swap still happens exactly once and out of sight, and the run then plays
    /// out over ordinary frames and comes back wearing the other theme. It used
    /// to skip instead — a 10-second step finished the whole run in one call —
    /// and the swap frame's own hitch then swallowed the breath's entire rise,
    /// which is the defect the cap removes.
    /// </summary>
    [Fact]
    public void AHitchPausesTheRunInsteadOfSkippingIt()
    {
        var timeline = new ThemeVeilTimeline();
        timeline.Start();

        ThemeVeilStep hitch = timeline.Advance(10f);

        Assert.False(hitch.Finished);
        Assert.True(timeline.IsRunning);

        var swaps = hitch.Swap ? 1 : 0;
        var finished = false;

        for (var frame = 0; frame < 200 && !finished; frame++)
        {
            ThemeVeilStep step = timeline.Advance(1f / 60f);

            if (step.Swap)
            {
                swaps++;
                Assert.Equal(1f, step.Blur);
            }

            finished = step.Finished;
        }

        Assert.True(finished);
        Assert.Equal(1, swaps);
        Assert.Equal(0f, timeline.Blur);
        Assert.Equal(1f, timeline.Blend);
    }

    /// <summary>
    /// The breath starts the instant the defocus lands and plays its whole
    /// first arch before the freeze — the swap sits in the arch's trough,
    /// where the picture stands still anyway, so the repaint's rendering
    /// hitch reads as the exhale's pause instead of a dead delay before
    /// anything breathed. That delay was the user's report, and this is the
    /// placement that removes it.
    /// </summary>
    [Fact]
    public void TheFirstArchPlaysWholeBeforeTheSwapAndTheSwapLandsInItsTrough()
    {
        var peak = 0f;

        for (var step = 0; step <= 500; step++)
        {
            float at = ThemeVeilTimeline.DefocusSeconds
                + ThemeVeilTimeline.BreathArchSeconds * step / 500f;

            peak = MathF.Max(peak, ThemeVeilTimeline.BreathAt(at));
        }

        Assert.True(peak > 0.99f, $"the first arch only reaches {peak} before the swap");
        Assert.True(
            ThemeVeilTimeline.BreathAt(ThemeVeilTimeline.SwapSeconds) < 0.01f,
            "the freeze does not land in the trough");

        // And the run really swaps there: simulated at frame rate, the tick
        // that swaps sees a breath still near the floor.
        var timeline = new ThemeVeilTimeline();
        timeline.Start();

        ThemeVeilStep swapped = default;

        while (!swapped.Swap)
            swapped = timeline.Advance(1f / 60f);

        Assert.True(
            swapped.Breath < 0.15f,
            $"the swap tick is already at breath {swapped.Breath}");
    }

    /// <summary>
    /// The very hitch the run carries inside itself: the swap frame's own work
    /// — the readback, the mip chain, the repaint — arrives as one long step
    /// right after the swap. The breath's rise must survive it, or the still
    /// arrives already swollen and only ever relaxes, which is a decay and not
    /// a breath. Traced on the running editor before the cap: breath 0.00 on
    /// the swap frame and 0.84 on the very next.
    /// </summary>
    [Fact]
    public void TheSwapsOwnHitchCannotEatTheBreathsRise()
    {
        var timeline = new ThemeVeilTimeline();
        timeline.Start();

        ThemeVeilStep step = default;

        while (!step.Swap)
            step = timeline.Advance(1f / 60f);

        // The repaint hitch, as measured: a seventh of a second in one step.
        ThemeVeilStep after = timeline.Advance(0.14f);

        Assert.True(
            after.Breath < 0.3f,
            $"the still comes back from the hitch already at {after.Breath}");
    }

    /// <summary>
    /// Where the brightness change is put, and the reason it is its own phase:
    /// the dissolve happens with the window fully out of focus at both ends, so
    /// what crosses is a field of colour rather than two pages of text.
    /// </summary>
    [Fact]
    public void TheDissolveHappensEntirelyOutOfFocus()
    {
        const float start = ThemeVeilTimeline.DefocusSeconds + ThemeVeilTimeline.SettleSeconds;

        for (var step = 0; step <= 10; step++)
        {
            float elapsed = start + ThemeVeilTimeline.DissolveSeconds * step / 10f;

            Assert.Equal(1f, ThemeVeilTimeline.BlurAt(elapsed));
        }

        Assert.Equal(0f, ThemeVeilTimeline.BlendAt(start));
        Assert.Equal(1f, ThemeVeilTimeline.BlendAt(start + ThemeVeilTimeline.DissolveSeconds));
    }

    /// <summary>
    /// And nothing of the new look is on show before it has been applied — the
    /// blend is the one thing that could reveal a half-repainted window.
    /// </summary>
    [Fact]
    public void NothingLiveIsShownBeforeTheSwap()
    {
        for (var step = 0; step <= 20; step++)
        {
            float elapsed = ThemeVeilTimeline.DefocusSeconds * step / 20f;

            Assert.Equal(0f, ThemeVeilTimeline.BlendAt(elapsed));
        }
    }

    /// <summary>
    /// Out of focus and back, once each: a blur that wavered would read as the
    /// window flickering rather than as one gesture.
    /// </summary>
    [Fact]
    public void ItGoesSoftOnceAndComesBackOnce()
    {
        var timeline = new ThemeVeilTimeline();
        timeline.Start();

        var blurs = new List<float>();
        var blends = new List<float>();

        while (timeline.IsRunning)
        {
            ThemeVeilStep step = timeline.Advance(1f / 120f);

            blurs.Add(step.Blur);
            blends.Add(step.Blend);
        }

        var peak = blurs.IndexOf(1f);

        Assert.True(peak >= 0);

        for (var index = 1; index <= peak; index++)
            Assert.True(blurs[index] >= blurs[index - 1]);

        for (var index = blurs.LastIndexOf(1f) + 1; index < blurs.Count; index++)
            Assert.True(blurs[index] <= blurs[index - 1]);

        for (var index = 1; index < blends.Count; index++)
            Assert.True(blends[index] >= blends[index - 1]);
    }

    /// <summary>
    /// The breath lives only inside the fully blurred stretch: wherever it is
    /// above nought, the base blur is at its ceiling — so it can only ever add
    /// softness, never fight the ramps at either end.
    /// </summary>
    [Theory]
    [InlineData(1f / 30f)]
    [InlineData(1f / 60f)]
    [InlineData(1f / 144f)]
    public void TheBreathOnlyEverAddsToAFullBlur(float delta)
    {
        var timeline = new ThemeVeilTimeline();
        timeline.Start();

        while (timeline.IsRunning)
        {
            ThemeVeilStep step = timeline.Advance(delta);

            Assert.True(step.Breath >= 0f);

            if (step.Breath > 0f)
                Assert.Equal(1f, step.Blur);
        }
    }

    /// <summary>
    /// Breathing is cycles, not one swell — it was twice built as a single
    /// in-and-out welded to the hold's length. It
    /// reaches full breath, touches the floor between cycles (which is what
    /// separates breaths from a bulge), and joins the floor at both ends of
    /// the stretch with no velocity, whatever phase the window cuts it at.
    /// </summary>
    [Fact]
    public void TheBreathCyclesAndTouchesTheFloorBetweenThem()
    {
        const float start = ThemeVeilTimeline.DefocusSeconds;
        const float held =
            ThemeVeilTimeline.SettleSeconds + ThemeVeilTimeline.DissolveSeconds;

        Assert.Equal(0f, ThemeVeilTimeline.BreathAt(start), 3);
        Assert.Equal(0f, ThemeVeilTimeline.BreathAt(start + held), 3);

        var peak = 0f;
        var troughAfterPeak = 1f;
        var seenPeak = false;

        for (var step = 0; step <= 2000; step++)
        {
            float value = ThemeVeilTimeline.BreathAt(start + held * step / 2000f);

            Assert.True(value >= 0f);

            peak = MathF.Max(peak, value);

            if (peak > 0.95f)
                seenPeak = true;

            if (seenPeak && value < troughAfterPeak)
                troughAfterPeak = value;
        }

        Assert.True(peak > 0.95f, $"the breath only reaches {peak}");
        Assert.True(
            troughAfterPeak < 0.05f,
            $"the breath never comes back down between cycles ({troughAfterPeak})");

        // And the stretch's edges move slowly — the envelope's job.
        float edge = ThemeVeilTimeline.BreathAt(start + held * 0.01f);

        Assert.True(edge < 0.05f, $"the breath arrives already at {edge}");
    }

    /// <summary>
    /// Eased at both ends of the defocus, so the window neither snaps out of
    /// focus nor stops dead on the way.
    /// </summary>
    [Fact]
    public void BothEndsOfTheDefocusAreEased()
    {
        const float step = ThemeVeilTimeline.DefocusSeconds / 10f;

        float first = ThemeVeilTimeline.BlurAt(step) - ThemeVeilTimeline.BlurAt(0f);
        float middle =
            ThemeVeilTimeline.BlurAt(ThemeVeilTimeline.DefocusSeconds * 0.5f + step * 0.5f)
            - ThemeVeilTimeline.BlurAt(ThemeVeilTimeline.DefocusSeconds * 0.5f - step * 0.5f);

        Assert.True(first < middle, $"it starts at {first} and is {middle} in the middle");

        float last =
            ThemeVeilTimeline.BlurAt(ThemeVeilTimeline.DefocusSeconds - step)
            - ThemeVeilTimeline.BlurAt(ThemeVeilTimeline.DefocusSeconds);

        Assert.True(last < middle, $"it lands at {last} and is {middle} in the middle");
    }

    [Fact]
    public void ItComesBackSharpAndLive()
    {
        var timeline = new ThemeVeilTimeline();
        timeline.Start();

        while (timeline.IsRunning)
            timeline.Advance(1f / 60f);

        Assert.Equal(0f, timeline.Blur);
        Assert.Equal(1f, timeline.Blend);
    }
}
