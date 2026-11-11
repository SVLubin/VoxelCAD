using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// A panel's internal arithmetic. Every defect this file holds a line against
/// was shipped and then found by a human looking at the screen, because the
/// arithmetic lived inside a Godot node where nothing could reach it — which is
/// the reason it is a plain function now.
/// </summary>
public sealed class PanelLayoutSolverTests
{
    private const float Card = ChromePanel.CollapsedSide;
    private const float Cut = Card + ChromePanel.TabClearance;
    private const float Grown = Cut + ChromePanel.SurfaceWidth;

    /// <summary>The room the contents are laid out in, held across a fold.</summary>
    private const float Body = 360f;

    /// <summary>What a panel spends on itself above and below that room.</summary>
    private const float Chrome =
        ChromePanel.FrameInset * 2f + ChromePanel.HeaderHeight + ChromePanel.BodyGap;

    /// <summary>
    /// Every width a panel's surface is ever asked to settle at, with the notch
    /// each one carries: pinned in the stack, grown over a run of tabs, and
    /// grown as the last card with nothing below it to reveal.
    /// </summary>
    public static TheoryData<string, float, float, bool> SettledStates
    {
        get
        {
            var data = new TheoryData<string, float, float, bool>();

            foreach (bool mirrored in new[] { false, true })
            {
                data.Add("pinned", ChromePanel.SurfaceWidth, 0f, mirrored);
                data.Add("grown over tabs", ChromePanel.SurfaceWidth + Cut, Cut, mirrored);
                data.Add("grown as last card", ChromePanel.SurfaceWidth, 0f, mirrored);
            }

            return data;
        }
    }

    /// <summary>
    /// One panel, one width, in every state it has and on either side. The body
    /// is the primitive and the surface is built around it, so a panel pinned in
    /// the stack, grown over a run of tabs, and grown as the last card all give
    /// their contents the same room. Written the other way round — one width per
    /// dock, the body left over — this was 290/298/334 on the left against
    /// 320/328/364 on the right, and nobody had chosen any of the six.
    /// </summary>
    [Theory]
    [MemberData(nameof(SettledStates))]
    public void EveryStateGivesTheBodyTheSameWidth(
        string state, float surface, float cut, bool mirrored)
    {
        Assert.NotEmpty(state);

        PanelLayout layout = Solve(surface, surface, mirrored, cut);

        Assert.Equal(ChromePanel.BodyWidth, layout.Content.Size.X, 3);
        Assert.Equal(ChromePanel.BodyWidth, layout.Body.Size.X, 3);
    }

    /// <summary>
    /// The cut's half of the mask rule. A notch animates when a panel stops
    /// being the last card in its run, and the contents must no more follow it
    /// than they follow the width — so they are laid out against the notch the
    /// panel is heading for, and the surface uncovers them. Measured against the
    /// moving cut instead, they slid a whole notch across the body while the
    /// surface was still easing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NothingInsideMovesWhileTheNotchOpens(bool mirrored)
    {
        const float surface = ChromePanel.SurfaceWidth + Cut;

        PanelLayout settled = PanelLayoutSolver.Solve(
            new Vector2(surface, 400f), surface, Body, Cut, Cut, mirrored);

        for (float cut = 0f; cut <= Cut; cut += 1f)
        {
            PanelLayout opening = PanelLayoutSolver.Solve(
                new Vector2(surface, 400f), surface, Body, cut, Cut, mirrored);

            Assert.Equal(settled.Content.Size.X, opening.Content.Size.X, 3);
            Assert.Equal(
                StillEdge(settled.Content, settled.Body, mirrored),
                StillEdge(opening.Content, opening.Body, mirrored),
                3);
        }
    }

    /// <summary>
    /// The whole promise of the animation: the panel is a mask, and what is
    /// under it does not move. Checked across the entire sweep, on both sides,
    /// because the far edge is the moving one and on a mirrored column
    /// <em>everything</em> hangs off it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NothingInsideMovesWhileTheSurfaceOpens(bool mirrored)
    {
        PanelLayout settled = Solve(Grown, Grown, mirrored);

        for (float width = Card; width <= Grown; width += 1f)
        {
            PanelLayout opening = Solve(width, Grown, mirrored);

            Assert.Equal(StillEdge(settled.Glyph, settled.Header, mirrored),
                StillEdge(opening.Glyph, opening.Header, mirrored), 3);
            Assert.Equal(StillEdge(settled.Chevron, settled.Header, mirrored),
                StillEdge(opening.Chevron, opening.Header, mirrored), 3);
            Assert.Equal(StillEdge(settled.Pin, settled.Header, mirrored),
                StillEdge(opening.Pin, opening.Header, mirrored), 3);
            Assert.Equal(StillEdge(settled.Content, settled.Body, mirrored),
                StillEdge(opening.Content, opening.Body, mirrored), 3);
            Assert.Equal(settled.Glyph.Size.X, opening.Glyph.Size.X, 3);
        }
    }

    /// <summary>
    /// The same promise on the other axis, which is the one a fold moves. A
    /// folding panel walks its height from a full body down to its header
    /// alone, and the contents must sit still and be covered — sized by the
    /// mask instead, a scrolling body re-measured on every frame of the way
    /// down, so its bar appeared as the room ran out and went again on the way
    /// back up, inside a panel nobody was scrolling.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NothingInsideMovesWhileThePanelFolds(bool mirrored)
    {
        var full = new Vector2(Grown, Body + Chrome);
        PanelLayout settled = PanelLayoutSolver.Solve(full, Grown, Body, Cut, Cut, mirrored);

        for (float height = Chrome; height <= full.Y; height += 1f)
        {
            PanelLayout folding = PanelLayoutSolver.Solve(
                new Vector2(Grown, height), Grown, Body, Cut, Cut, mirrored);

            Assert.Equal(settled.Content.Position.Y, folding.Content.Position.Y, 3);
            Assert.Equal(settled.Content.Size.Y, folding.Content.Size.Y, 3);
        }
    }

    /// <summary>
    /// And the mask really is closing: the clip the contents sit in follows the
    /// surface all the way down. Without this the test above would pass on a
    /// panel that simply ignored its own height.
    /// </summary>
    [Fact]
    public void TheClipTheContentsSitInFollowsTheFold()
    {
        var full = new Vector2(Grown, Body + Chrome);

        Assert.Equal(
            Body,
            PanelLayoutSolver.Solve(full, Grown, Body, Cut, Cut, false).Body.Size.Y,
            3);
        Assert.Equal(
            0f,
            PanelLayoutSolver
                .Solve(new Vector2(Grown, Chrome), Grown, Body, Cut, Cut, false)
                .Body.Size.Y,
            3);
    }

    /// <summary>
    /// The grip hangs on the surface's inner-bottom corner — the two edges a
    /// pull moves — and rides the size the panel is passing through, the way
    /// the glass frame does: it is furniture on the frame, not content under
    /// the mask.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheGripRidesTheInnerBottomCorner(bool mirrored)
    {
        var size = new Vector2(Grown, Body + Chrome);
        PanelLayout layout = PanelLayoutSolver.Solve(size, Grown, Body, Cut, Cut, mirrored);

        float inner = mirrored
            ? ChromePanel.FrameInset + ChromePanel.GripInset
            : size.X - ChromePanel.FrameInset - ChromePanel.GripInset - ChromePanel.GripSide;

        Assert.Equal(inner, layout.Grip.Position.X, 3);
        Assert.Equal(
            size.Y - ChromePanel.FrameInset - ChromePanel.GripInset - ChromePanel.GripSide,
            layout.Grip.Position.Y,
            3);

        // And it really does ride: half the height puts it half the way up.
        PanelLayout folding = PanelLayoutSolver.Solve(
            new Vector2(Grown, Chrome + Body * 0.5f), Grown, Body, Cut, Cut, mirrored);

        Assert.True(folding.Grip.Position.Y < layout.Grip.Position.Y);
    }

    /// <summary>
    /// A rect that runs backwards is placed off the far edge and clipped away
    /// on the mirrored side. That is where the right column's glyph went, on a
    /// header the width of a tab: its width less the pin's clearance is below
    /// zero.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoPartEverRunsBackwards(bool mirrored)
    {
        for (float width = 0f; width <= Grown; width += 1f)
        {
            PanelLayout layout = Solve(width, width, mirrored);

            Assert.True(layout.Header.Size.X >= 0f, $"header at {width}");
            Assert.True(layout.Body.Size.X >= 0f, $"body at {width}");
            Assert.True(layout.Glyph.Size.X >= 0f, $"glyph at {width}");
            Assert.True(layout.Title.Size.X >= 0f, $"title at {width}");
            Assert.True(layout.Chevron.Size.X >= 0f, $"chevron at {width}");
            Assert.True(layout.Pin.Size.X >= 0f, $"pin at {width}");
            Assert.True(layout.Content.Size.X >= 0f, $"content at {width}");
        }
    }

    /// <summary>
    /// The glyph is the identity, and it is whole and in the same place at every
    /// width there is — collapsed, opening, grown, on either side. Placed by an
    /// HBox it was last in the row on a mirrored column so that it landed
    /// against the stub, and a row too wide for its box carried it clean out of
    /// the header: <c>ui</c> reported its rect as all zeroes and the right
    /// column's tab was blank.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheGlyphIsWholeAndUnmovedAtEveryWidth(bool mirrored)
    {
        for (float width = Card; width <= Grown; width += 1f)
        {
            PanelLayout layout = Solve(width, width, mirrored, cut: 0f);

            Assert.Equal(ChromePanel.GlyphSide, layout.Glyph.Size.X, 3);
            Assert.Equal(
                ChromePanel.IconInset,
                StillEdge(layout.Glyph, layout.Header, mirrored),
                3);
        }
    }

    [Fact]
    public void ACollapsedPanelHasNoRoomForThePin()
    {
        Assert.False(Solve(Card, Card, mirrored: false, cut: 0f).PinFits);
        Assert.True(Solve(Grown, Grown, mirrored: false).PinFits);
    }

    /// <summary>
    /// On a tab there is room for the glyph and nothing else, so the words get
    /// no width at all rather than a negative one.
    /// </summary>
    [Fact]
    public void ATabHasRoomForTheGlyphAlone()
    {
        PanelLayout tab = Solve(Card, Card, mirrored: false, cut: 0f);

        Assert.Equal(ChromePanel.GlyphSide, tab.Glyph.Size.X, 3);
        Assert.Equal(0f, tab.Title.Size.X, 3);
    }

    /// <summary>The two sides are each other's mirror, exactly.</summary>
    [Fact]
    public void MirroringIsExact()
    {
        PanelLayout left = Solve(Grown, Grown, mirrored: false);
        PanelLayout right = Solve(Grown, Grown, mirrored: true);

        Assert.Equal(left.Header.Size.X, right.Header.Size.X, 3);
        Assert.Equal(left.Glyph.Size.X, right.Glyph.Size.X, 3);
        Assert.Equal(
            left.Glyph.Position.X,
            left.Header.Size.X - right.Glyph.Position.X - right.Glyph.Size.X,
            3);
        Assert.Equal(
            left.Pin.Position.X,
            left.Header.Size.X - right.Pin.Position.X - right.Pin.Size.X,
            3);
    }

    /// <summary>
    /// The body starts past the cut on the stub's own side, and the cut is
    /// taken off its width once — not twice, and not off the wrong end.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheBodyStandsClearOfTheCut(bool mirrored)
    {
        PanelLayout layout = Solve(Grown, Grown, mirrored);
        float header = Grown - 2f * ChromePanel.FrameInset;

        Assert.Equal(header - Cut, layout.Body.Size.X, 3);
        Assert.Equal(
            mirrored ? ChromePanel.FrameInset : ChromePanel.FrameInset + Cut,
            layout.Body.Position.X,
            3);
    }

    private static PanelLayout Solve(
        float width, float laidOut, bool mirrored, float cut = Cut) =>
        PanelLayoutSolver.Solve(new Vector2(width, 400f), laidOut, Body, cut, cut, mirrored);

    /// <summary>
    /// Where a part sits measured from the edge that does not move — the left
    /// on an ordinary column, the right on a mirrored one. This is the number
    /// the whole design says must be constant.
    /// </summary>
    private static float StillEdge(Rect2 part, Rect2 box, bool mirrored) =>
        mirrored ? box.Size.X - part.Position.X - part.Size.X : part.Position.X;
    /// <summary>
    /// A panel that cannot be folded gets no chevron, and its title runs on into
    /// the room the chevron would have taken.
    /// </summary>
    /// <remarks>
    /// A fold is "keep reserving my room but show only my header": only a pinned
    /// panel reserves any. The chevron was drawn on a tab's temporary panel too,
    /// where a header press dismisses the glance instead — an affordance pointing
    /// at a gesture that is not offered. Hiding it in the node alone would have
    /// left the hole it was standing in, which is why the span is the solver's
    /// answer rather than a visibility flag on top of it.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APanelThatCannotFoldGivesItsChevronsRoomToTheTitle(bool mirrored)
    {
        var size = new Vector2(ChromePanel.SurfaceWidth, 400f);

        PanelLayout foldable =
            PanelLayoutSolver.Solve(size, size.X, Body, 0f, 0f, mirrored, chevron: 1f);

        PanelLayout plain =
            PanelLayoutSolver.Solve(size, size.X, Body, 0f, 0f, mirrored, chevron: 0f);

        // Exactly the chevron and the gap before it, handed to the title.
        Assert.Equal(
            foldable.Title.Size.X + ChromePanel.GlyphSide + ChromePanel.TextGap,
            plain.Title.Size.X,
            3);

        // And on a mirrored column the title still hangs off the moving edge,
        // so growing it must not move where it starts from.
        if (mirrored)
            Assert.Equal(foldable.Title.End.X, plain.Title.End.X, 3);
        else
            Assert.Equal(foldable.Title.Position.X, plain.Title.Position.X, 3);
    }

    /// <summary>
    /// The room comes back at the chevron's own pace, and the chevron keeps its
    /// box the whole way.
    /// </summary>
    /// <remarks>
    /// The title has to give the room up and take it back as the mark fades in
    /// and out, or it jumps into a space something is still standing in. The
    /// chevron's own box does <em>not</em> move: it is a <c>KeepCentered</c>
    /// texture, so a shrinking box would clip the glyph and slide it instead of
    /// scaling it — what appears and turns is the glyph's alpha and scale, which
    /// are the node's and not the solver's.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheTitleTakesTheChevronsRoomAtTheChevronsPace(bool mirrored)
    {
        var size = new Vector2(ChromePanel.SurfaceWidth, 400f);

        PanelLayout open = PanelLayoutSolver.Solve(size, size.X, Body, 0f, 0f, mirrored, 1f);
        PanelLayout half = PanelLayoutSolver.Solve(size, size.X, Body, 0f, 0f, mirrored, 0.5f);
        PanelLayout gone = PanelLayoutSolver.Solve(size, size.X, Body, 0f, 0f, mirrored, 0f);

        Assert.Equal(
            (open.Title.Size.X + gone.Title.Size.X) * 0.5f, half.Title.Size.X, 3);

        foreach (PanelLayout layout in new[] { half, gone })
        {
            Assert.Equal(open.Chevron.Position.X, layout.Chevron.Position.X, 3);
            Assert.Equal(open.Chevron.Size.X, layout.Chevron.Size.X, 3);
        }
    }

}
