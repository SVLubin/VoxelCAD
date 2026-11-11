using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Where every part of a <see cref="ChromePanel"/> stands for one frame. Rects
/// are given in the coordinates of the control that owns them:
/// <see cref="Header"/> and <see cref="Body"/> in the panel's, <see cref="Strip"/>
/// and <see cref="Pin"/> in the header's, <see cref="Content"/> in the body's.
/// </summary>
internal readonly record struct PanelLayout(
    Rect2 Header,
    Rect2 Body,
    Rect2 Glyph,
    Rect2 Title,
    Rect2 Chevron,
    Rect2 Pin,
    Rect2 Content,
    Rect2 Grip,
    bool PinFits);

/// <summary>
/// The whole of a panel's internal arithmetic, as a pure function. A plain
/// class rather than lines inside the node, because everything that has gone
/// wrong in here was arithmetic and none of it was reachable by a test — see
/// <c>docs/panels.md</c>.
/// </summary>
/// <remarks>
/// Two rules it exists to keep, both learned the hard way:
/// <list type="bullet">
/// <item><b>Nothing is placed by a container.</b> A container sizes its children
/// from a deferred sort while their position follows the parent at once, so on
/// the frame a panel's rect changes the two disagree — and anything measured
/// from the far edge, which is <em>everything</em> on a mirrored column, shivers
/// by a unit every tick for the length of the animation. Every rect here is
/// handed over in the same pass that assigns the panel's own.</item>
/// <item><b>Nothing moves while the panel opens, or while it folds.</b> The
/// parts are placed against <c>laidOutWidth</c> and <c>laidOutHeight</c> — the
/// size the panel is heading for — and measured from the edges that stand
/// still, so the surface uncovers them. Only the mask animates. It was one
/// axis for a while, and a fold is what showed it: the body's height followed
/// the mask, so the contents re-flowed all the way down and the scroll bar
/// came and went inside a panel nobody was scrolling.</item>
/// </list>
/// </remarks>
internal static class PanelLayoutSolver
{
    /// <param name="panelSize">The surface's size this frame — the mask.</param>
    /// <param name="laidOutWidth">
    /// The width the surface is heading for. The parts are placed against this
    /// and not against <paramref name="panelSize"/>, which is the whole point:
    /// they stand still and are revealed.
    /// </param>
    /// <param name="laidOutHeight">
    /// The body height the contents are laid out against — the same rule one
    /// axis over, and the same argument for it. A fold is the mask closing
    /// over a body that does not change: laid out against the animating height
    /// instead, a scrolling body re-measured every frame of the fold, so its
    /// bar appeared on the way down and went again on the way back up and the
    /// rows shifted under it. Held at the last height that had room for them,
    /// which is why it is the caller's number and not
    /// <paramref name="panelSize"/> minus the header.
    /// </param>
    /// <param name="cut">How far the notch reaches in from the stub's side.</param>
    /// <param name="settledCut">
    /// The notch the panel is heading for — the cut's own half of the mask rule,
    /// and the reason it is a second argument rather than the same one. A cut
    /// animates when a panel stops being the last card in its run (36 to 0, or
    /// back), and the contents must no more follow that than they follow the
    /// width: measured against the moving cut they slid a notch's worth across
    /// the body while the surface was still easing.
    /// </param>
    /// <param name="mirrored">
    /// Whether the stub — and so the edge that stands still — is on the right.
    /// </param>
    /// <param name="chevron">
    /// How much of the chevron is there, nought to one. A fold is "keep
    /// reserving my room but show only my header", so only a pinned panel has
    /// anything to say with one — and the number is a fraction rather than a
    /// flag because the mark fades rather than vanishing, and the title has to
    /// take the room back at the same pace or it jumps into a space that is
    /// still occupied.
    /// <para>
    /// Only the <em>title's</em> far end follows it. The chevron keeps its own
    /// full-sized box the whole way: it is a <c>KeepCentered</c> texture, so a
    /// shrinking box would clip the glyph and slide it rather than scaling it,
    /// and what does the appearing is the glyph's own alpha and scale.
    /// </para>
    /// </param>
    public static PanelLayout Solve(
        Vector2 panelSize,
        float laidOutWidth,
        float laidOutHeight,
        float cut,
        float settledCut,
        bool mirrored,
        float chevron = 1f)
    {
        const float inset = ChromePanel.FrameInset;

        float header = Mathf.Max(0f, panelSize.X - 2f * inset);
        float laidOutHeader = Mathf.Max(0f, laidOutWidth - 2f * inset);

        float bodyTop = inset + ChromePanel.HeaderHeight + ChromePanel.BodyGap;
        float bodyHeight = Mathf.Max(0f, panelSize.Y - bodyTop - inset);
        float bodyWidth = Mathf.Max(0f, header - cut);
        float laidOutBody = Mathf.Max(0f, laidOutHeader - settledCut);

        // The row only stops short of the pin when there is one. Subtracting its
        // clearance unconditionally inverted the row on a header the width of a
        // tab — 28 less 30 — and an inverted rect on the mirrored side lands off
        // the far edge, which is where the right column's glyph went.
        bool pinFits = laidOutHeader >= ChromePanel.PinRoom;
        float rowEnd = laidOutHeader
            - (pinFits ? ChromePanel.PinClearance : ChromePanel.IconInset);

        // The row's three parts, each placed outright. An HBox here was the last
        // container inside a panel and it cost the glyph twice over: a row wider
        // than its box grows away from the near edge, and the glyph — last in
        // the order on a mirrored column, so that it lands against the stub —
        // was carried straight out of the header and clipped to nothing.
        float glyphFrom = ChromePanel.IconInset;
        float glyphTo = glyphFrom + ChromePanel.GlyphSide;
        float chevronTo = Mathf.Max(glyphTo + ChromePanel.TextGap, rowEnd);
        float chevronFrom = chevronTo - ChromePanel.GlyphSide;
        float titleTo = Mathf.Max(
            glyphTo,
            chevronTo - (ChromePanel.GlyphSide + ChromePanel.TextGap) * chevron);
        float glyphTop = (ChromePanel.HeaderHeight - ChromePanel.GlyphSide) * 0.5f;

        // The grip hangs on the surface's inner-bottom corner — the two edges a
        // pull moves — so unlike the contents it is placed from the size the
        // panel is passing through, the way the glass frame is: it is furniture
        // on the frame, and a mark that stood at the settled corner would float
        // in the air for the length of every transition.
        float gripX = mirrored
            ? inset + ChromePanel.GripInset
            : panelSize.X - inset - ChromePanel.GripInset - ChromePanel.GripSide;
        float gripY = panelSize.Y - inset - ChromePanel.GripInset - ChromePanel.GripSide;

        return new PanelLayout(
            Header: new Rect2(inset, inset, header, ChromePanel.HeaderHeight),
            Body: new Rect2(
                mirrored ? inset : inset + cut, bodyTop, bodyWidth, bodyHeight),
            Glyph: Span(
                glyphFrom, glyphTo, header, glyphTop, ChromePanel.GlyphSide, mirrored),
            Title: Span(
                glyphTo + ChromePanel.TextGap,
                titleTo,
                header,
                0f,
                ChromePanel.HeaderHeight,
                mirrored),
            Chevron: Span(
                chevronFrom, chevronTo, header, glyphTop, ChromePanel.GlyphSide, mirrored),
            Pin: Span(
                laidOutHeader - ChromePanel.PinWidth - ChromePanel.PinInset,
                laidOutHeader - ChromePanel.PinInset,
                header,
                0f,
                ChromePanel.HeaderHeight,
                mirrored),
            Content: Span(0f, laidOutBody, bodyWidth, 0f, laidOutHeight, mirrored),
            Grip: new Rect2(gripX, gripY, ChromePanel.GripSide, ChromePanel.GripSide),
            PinFits: pinFits);
    }

    /// <summary>
    /// A part's place inside a box, given where it belongs measured inward from
    /// the edge that stands still. The box's far edge moves while the panel
    /// opens; its near edge does not, so everything is measured from there and
    /// the far end is simply not reached yet.
    /// </summary>
    private static Rect2 Span(
        float from, float to, float box, float top, float height, bool mirrored)
    {
        float width = Mathf.Max(0f, to - from);

        return new Rect2(mirrored ? box - to : from, top, width, height);
    }
}
