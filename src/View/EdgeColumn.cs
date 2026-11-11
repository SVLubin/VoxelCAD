using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// One window edge's panel column: the pinned panels stacked down it, then the
/// unpinned ones as a run of icon tabs. See <c>docs/panels.md</c> — a tab is
/// the panel shrunk to its own glyph, and showing it grows the surface out of
/// that very corner.
/// </summary>
/// <remarks>
/// Every frame this works out where each thing <em>belongs</em> and hands that
/// to a <see cref="RectMotion"/>; nothing is ever placed outright. So pinning,
/// folding, growing, collapsing and the column's own width all animate by
/// construction rather than by anybody remembering to animate them — and a
/// target changed halfway through is just the next target, which is what makes
/// a quick hand harmless.
/// <para>
/// What a press means is the shell's decision. The column reports which tab was
/// pressed and changes nothing itself.
/// </para>
/// </remarks>
internal sealed partial class EdgeColumn : Control
{
    public const float RailWidth = 36f;

    internal const float Inset = 4f;

    /// <summary>
    /// What a column occupies once anything is pinned in it: the panel's own
    /// surface plus the column's margins, and never the other way round. The two
    /// sides used to carry a width each — 300 and 330 — and each derived its
    /// panels from it, which is how one panel came to be three different widths
    /// depending on where it was standing.
    /// </summary>
    public const float WideWidth = ChromePanel.SurfaceWidth + 2f * Inset;

    /// <summary>What a column shows while it is merely a place a tab could go.</summary>
    private const float OfferedHintAlpha = 0.05f;

    /// <summary>And while it is the one the tab would land in.</summary>
    private const float TargetHintAlpha = 0.14f;

    /// <summary>
    /// Plain white, because the hint tints whatever is behind it rather than
    /// colouring it: a column stands over the scene, and an accent laid on the
    /// room reads as a thing rather than as a place.
    /// </summary>
    private static readonly Color HintColour = new(1f, 1f, 1f);

    /// <summary>
    /// What stands between one thing in the column and the next — and the same
    /// number the cut leaves round a tab it reveals, so a tab is framed evenly
    /// whether it sits in the run or inside a notch.
    /// </summary>
    internal const float Gap = ChromePanel.TabClearance;

    /// <summary>
    /// The stub a grown panel keeps, which is the header's own height: a tab is
    /// a panel shrunk to its icon, so the icon must land in the same pixel
    /// either way.
    /// </summary>
    private const float TabSide = ChromePanel.HeaderHeight;

    /// <summary>
    /// The square a collapsed panel actually occupies — the header plus the
    /// surface's frame. Everything a tab is measured against uses this and not
    /// <see cref="TabSide"/>: where the run steps, how wide the rail centres it,
    /// and what rect the pointer is tested against.
    /// </summary>
    internal const float TabCard = ChromePanel.CollapsedSide;

    /// <summary>
    /// How far the notch reaches in: the card it reveals, plus the clearance
    /// that keeps the surface's edge off the tab's own.
    /// </summary>
    internal const float NotchReach = TabCard + Gap;

    /// <summary>
    /// What one panel's chrome adds around header and body: the frame's two
    /// content margins and the separation between the header and the body.
    /// </summary>
    private const float FrameMargins = 2f * ChromePanel.FrameInset;

    private const float StackSeparation = ChromePanel.BodyGap;

    /// <summary>
    /// Header, frame and separation together: what a body sits under. Reserved
    /// for every pinned slot while the cascade is solved, which over-reserves
    /// the separation for one the cascade then folds — conservative by four
    /// units, and the alternative is a solve that has to know its own answer.
    /// </summary>
    internal const float Chrome = ChromePanel.HeaderHeight + FrameMargins + StackSeparation;

    private readonly PanelSide _side;
    private readonly MotionTiming _timing;
    private readonly List<Slot> _slotsList = new();
    private readonly Smoothed _width;
    private readonly Smoothed _hint;
    private readonly ColorRect _dropHint;
    private readonly GhostOutline _dropGhost;
    private readonly Smoothed _ghostFade;

    private ShapeMotion? _ghostMotion;
    private PanelShape? _ghostTarget;
    private PanelKind? _openTemp;
    private PanelKind? _carried;
    private Vector2 _carriedAt;
    private bool _carriedStubOnRight;

    // The side's body width — the user's number, clamped before it gets here —
    // and whether a resize drag is under way, during which every motion snaps:
    // an edge under the hand that eased toward the pointer would lag behind it,
    // the carried panel's own rule.
    private float _bodyWidth = (float)PanelWidths.Default;
    private bool _resizing;

    public EdgeColumn(PanelSide side, MotionTiming timing)
    {
        _side = side;
        _timing = timing;
        _width = new Smoothed(MotionSpeed.Length, timing);
        _hint = new Smoothed(MotionSpeed.Opacity, timing);
        _ghostFade = new Smoothed(MotionSpeed.Opacity, timing);

        Name = side == PanelSide.Left ? "LeftColumn" : "RightColumn";
        MouseFilter = MouseFilterEnum.Stop;

        AnchorTop = 0f;
        AnchorBottom = 1f;
        AnchorLeft = side == PanelSide.Left ? 0f : 1f;
        AnchorRight = side == PanelSide.Left ? 0f : 1f;

        // Where a carried tab may be put down. Added first so it lies under the
        // cards, and faded rather than switched: a hint that blinked on would
        // be one more thing snapping during a gesture that is otherwise all
        // motion.
        _dropHint = new ColorRect { Name = "DropHint" };
        _dropHint.MouseFilter = MouseFilterEnum.Ignore;
        _dropHint.AnchorRight = 1f;
        _dropHint.AnchorBottom = 1f;
        _dropHint.Color = new Color(1f, 1f, 1f, 0f);
        AddChild(_dropHint);

        // Transparent is where a hint comes from, so say so. A motion's first
        // target snaps, which is right for a panel arriving on screen and wrong
        // for something that appears by becoming visible: the only callers of
        // SetDropHint are inside a drag, so without this the first hint of every
        // session was a snap to its alpha and the fade began on the second one.
        _hint.SnapTo(0f);
        _ghostFade.SnapTo(0f);

        // Where the panel in hand will land. Drawn *above* every card, which is
        // safe by construction rather than by luck: the hole is empty of panels
        // — that is what makes it a hole — and the only card whose rect can ever
        // meet it is the carried one, which is exactly what the outline has to
        // be legible against. Underneath, a 322-wide panel carried over a
        // 30-wide tab slot would swallow its own destination whole. That claim
        // was only made true by the notch: a rectangular outline hung on a
        // tab's corner lay over the very tabs the cut exists to reveal.
        _dropGhost = new GhostOutline();

        AddChild(_dropGhost);
    }

    /// <summary>Raised as the column's width moves, so the strip follows it.</summary>
    public event Action? WidthChanged;

    public float CurrentWidth { get; private set; }

    /// <summary>The side's body width, which every panel of the column wears.</summary>
    public float BodyWidth => _bodyWidth;

    /// <summary>The surface that body needs — the width the geometry is solved at.</summary>
    private float SurfaceWidthNow => _bodyWidth + FrameMargins;

    public void SetBodyWidth(float width) => _bodyWidth = width;

    /// <summary>
    /// Puts the column into a resize gesture, or takes it out of one. While it
    /// is in one, every motion snaps to its target instead of easing: the edge
    /// is under the hand.
    /// </summary>
    public void SetResizing(bool resizing) => _resizing = resizing;

    /// <summary>
    /// Hands the column its panels for this layout, already sorted — pinned by
    /// order, then unpinned by order. The slots survive it, so a panel that has
    /// just been pinned glides to its new place rather than appearing there.
    /// </summary>
    public void SetPanels(
        IReadOnlyList<(ChromePanel Panel, PanelPlacement Placement)> panels,
        PanelKind? openTemp)
    {
        var kept = new List<Slot>();

        foreach ((ChromePanel panel, PanelPlacement placement) in panels)
        {
            Slot slot = Find(panel.Kind) ?? new Slot(panel, _timing);

            slot.Placement = placement;
            kept.Add(slot);

            if (panel.GetParent() != this)
            {
                panel.GetParent()?.RemoveChild(panel);
                AddChild(panel);
            }
        }

        foreach (Slot slot in _slotsList)
        {
            // Only while it is still ours: the columns are filled left first,
            // so a panel that moved right to left has already been taken in and
            // shown by its new column by the time this runs.
            if (kept.Contains(slot) || slot.Panel.GetParent() != this)
                continue;

            slot.Panel.SetShown(false);
        }

        _slotsList.Clear();
        _slotsList.AddRange(kept);
        _openTemp = openTemp;

        ApplyChildOrder();
    }

    /// <summary>
    /// Whether this column is somewhere a carried tab could be put down, and
    /// whether it is the one it would land in. Both are shown: the places, so
    /// the hand knows where it may go, and the answer, so it knows where it is
    /// going. Null while nothing is being carried.
    /// </summary>
    public void SetDropHint(bool? isTarget) =>
        _hint.Retarget(isTarget switch
        {
            null => 0f,
            true => TargetHintAlpha,
            false => OfferedHintAlpha
        });

    /// <summary>
    /// The tab being carried by a drag, and where its card should sit — the
    /// pointer's own position, less where in the card it was picked up, so it
    /// rides under the hand rather than jumping its corner to it.
    /// </summary>
    /// <param name="stubOnRight">
    /// Which end of the carried card its glyph belongs to, which is the side it
    /// was <em>picked up</em> from and not the side it is over. A panel is
    /// carried as the thing it is: turning its header round at the window's
    /// middle is a jump nobody asked for, and it turns it round the wrong way —
    /// the end that hangs off the screen is always the one away from where the
    /// hand came from, so following the destination puts the glyph and the
    /// title over the edge exactly when the panel is carried to it. Where it
    /// will land is the ghost's sentence, and the ghost still says it.
    /// </param>
    public void SetCarried(PanelKind? kind, Vector2 globalTopLeft, bool stubOnRight = false)
    {
        _carried = kind;
        _carriedAt = globalTopLeft;
        _carriedStubOnRight = stubOnRight;

        ApplyChildOrder();
    }

    /// <summary>
    /// States the column's draw order, which Godot reads off the child list and
    /// nothing here had ever written down. Called from both writers of what it
    /// reads, so neither can undo the other.
    /// </summary>
    private void ApplyChildOrder()
    {
        var ordered = new PanelKind[_slotsList.Count];

        for (var index = 0; index < _slotsList.Count; index++)
            ordered[index] = _slotsList[index].Panel.Kind;

        IReadOnlyList<int> depths = PanelDepthOrder.Solve(ordered, _carried);

        for (var index = 0; index < _slotsList.Count; index++)
        {
            // Past the hint, which is drawn under every card and is child 0. A
            // panel on its way to the other column is still a child of this one
            // for the length of one distribution — it is not in the list, so it
            // is simply pushed past the end and reparented moments later.
            int want = depths[index] + 1;

            if (_slotsList[index].Panel.GetIndex() != want)
                MoveChild(_slotsList[index].Panel, want);
        }

        // Last of all, over every card. See the constructor for why over.
        if (_dropGhost.GetIndex() != GetChildCount() - 1)
            MoveChild(_dropGhost, GetChildCount() - 1);
    }

    /// <summary>
    /// The middle of every tab in the run, top to bottom, in global
    /// coordinates — what a drop target is measured against. Asked of where
    /// they <em>belong</em>, like the hit test, so a run still gliding into
    /// place answers about its destination rather than its flight.
    /// </summary>
    public PanelColumnDrop DescribeDrop(PanelKind? exclude)
    {
        // Derived from the same solve the column lays itself out by, rather
        // than read back off the slots: the geometry a drop is judged against
        // and the geometry the panels stand at must be one answer, or the two
        // disagree about where the boundaries are.
        var described = new PanelSlotInput[_slotsList.Count];
        var excluded = -1;

        for (var index = 0; index < _slotsList.Count; index++)
        {
            described[index] = Describe(_slotsList[index]);

            if (_slotsList[index].Panel.Kind == exclude)
                excluded = index;
        }

        return PanelColumnGeometry.Describe(
            PanelColumnGeometry.Solve(described, Size.Y, _side, SurfaceWidthNow),
            GlobalPosition.Y,
            excluded);
    }

    /// <summary>
    /// The panel a drag would pick up at this point: the one whose header is
    /// under it and which has stopped moving. A header is the whole of a
    /// collapsed card and the top of an open one, so an open panel is dragged
    /// by its title bar exactly as a tab is dragged by its glyph.
    /// </summary>
    /// <remarks>
    /// Still, because a panel caught mid-flight has no settled place to be
    /// picked up from — the grab would be measured against a rect that is
    /// already somewhere else by the next frame.
    /// <para>
    /// Decided by <see cref="PanelPressTargets"/>, which asks the depth this
    /// column already solves rather than taking the first header in layout
    /// order: panels here overlap, so the first is not the topmost.
    /// </para>
    /// </remarks>
    public bool TryGetDraggable(Vector2 point, out PanelKind kind) =>
        PanelPressTargets.TryGetDraggable(DescribeForPress(), _carried, point, out kind);

    /// <summary>Where one panel's tab belongs, in global coordinates.</summary>
    public bool TryGetTabRect(PanelKind kind, out Rect2 rect)
    {
        foreach (Slot slot in _slotsList)
        {
            if (slot.Placement.Pinned || slot.Panel.Kind != kind)
                continue;

            rect = new Rect2(GlobalPosition + slot.TabTarget.Position, slot.TabTarget.Size);

            return true;
        }

        rect = default;

        return false;
    }

    /// <summary>
    /// The tab under a window point, asked of where tabs <em>belong</em> rather
    /// than where they happen to be drawn this frame: a target that moved out
    /// from under the pointer mid-animation would make hovering a lottery.
    /// </summary>
    public bool TryGetTabAt(Vector2 point, out PanelKind kind)
    {
        Vector2 local = point - GlobalPosition;

        foreach (Slot slot in _slotsList)
        {
            if (slot.Placement.Pinned || !slot.TabTarget.HasPoint(local))
                continue;

            kind = slot.Panel.Kind;

            return true;
        }

        kind = default;

        return false;
    }

    /// <summary>
    /// The panel whose resize grip is under this point, if any — and whether
    /// that grip moves the height too. Still, on the draggable's own terms: a
    /// grip caught mid-flight would measure the pull against a corner that is
    /// already somewhere else.
    /// </summary>
    /// <remarks>
    /// The grip of a panel with another panel drawn over it is not under the
    /// point, whatever its rect says — which is the whole of the defect
    /// <see cref="PanelPressTargets"/> exists to state: a temporary panel's own
    /// pin stood over a pinned panel's grip, and the grip answered.
    /// </remarks>
    public bool TryGetResizeGrip(Vector2 point, out PanelKind kind, out bool heightToo) =>
        PanelPressTargets.TryGetGrip(DescribeForPress(), _carried, point, out kind, out heightToo);

    /// <summary>
    /// Every panel this column is showing, as the press layer reads them: where
    /// each surface stands, what it cuts, and the two things inside it a
    /// gesture can take hold of.
    /// </summary>
    /// <remarks>
    /// Built per press rather than per frame — both callers ask on the frame a
    /// button goes down and on no other — so a list of at most four is nothing.
    /// </remarks>
    private IReadOnlyList<PanelPressInput> DescribeForPress()
    {
        var panels = new List<PanelPressInput>(_slotsList.Count);

        foreach (Slot slot in _slotsList)
        {
            if (!slot.Panel.IsVisibleInTree())
                continue;

            panels.Add(new PanelPressInput(
                slot.Panel.Kind,
                slot.IsStill,
                slot.Panel.GetGlobalRect(),
                slot.Panel.NotchWidth,
                slot.Panel.StubOnRight,
                slot.Panel.HeaderGlobalRect,
                slot.Panel.GripGlobalRect,
                slot.Panel.HeightResizable));
        }

        return panels;
    }

    /// <summary>
    /// Whether a global point lands on any surface this column is showing. A
    /// panel routinely stands over the scene — a temporary one grown out of a
    /// rail, one grown over a run and so standing a notch clear of the stack,
    /// one still retracting while its neighbour opens, and above all one under
    /// the hand — so the scene's polled affordances have to ask before they
    /// propose anything at the pointer.
    /// </summary>
    /// <remarks>
    /// Every panel, rather than the one the column was told is open. That is
    /// the fix rather than a widening: asked only of the open temporary panel,
    /// this said no for a panel being <em>dragged</em> across the window, and
    /// the brush went on drawing its cell through the card in the user's own
    /// hand. The question is "is any of this over that point", and answering it
    /// for one privileged panel answers it for one case.
    /// <para>
    /// The whole rect, notch included. A cut is a hole in the surface, and what
    /// is behind it is the column's own tabs wherever a panel is standing in
    /// its column — chrome either way. Only a carried panel can put that hole
    /// over the scene, and a thirty-six unit gap in the answer for the length
    /// of a drag is worth less than the one line it would cost.
    /// </para>
    /// </remarks>
    public bool CoversPoint(Vector2 point)
    {
        foreach (Slot slot in _slotsList)
        {
            if (slot.Panel.IsVisibleInTree() && slot.Panel.GetGlobalRect().HasPoint(point))
                return true;
        }

        return false;
    }

    public override void _Process(double delta)
    {
        var step = (float)delta;

        // An empty column is still a width closing to nothing, and it goes
        // through the same motion as any other: applying a literal here left
        // the number that is the sole authority on the column's width holding a
        // stale value, so it snapped in both directions instead of gliding.
        if (_resizing)
            _width.SnapTo(WantedWidth());
        else
            _width.Retarget(WantedWidth());

        _width.Tick(step);
        ApplyWidth(_width.Value);

        _hint.Tick(step);
        _dropHint.Color = new Color(HintColour, _hint.Value);
        _dropHint.Visible = _hint.Value > 0.001f;

        Retarget();
        ApplyGhost(step);

        if (_slotsList.Count == 0)
            return;

        // By index, not with a foreach: applying a slot can hide a body that
        // holds the keyboard, and what answers that is free to rebuild this
        // very list. The handoff is deferred so it no longer does, and this is
        // the second cut — a per-frame walk over a list a callback can rebuild
        // is the shape of the bug, not the one callback that reached it.
        for (var index = 0; index < _slotsList.Count; index++)
        {
            bool carried = _slotsList[index].Panel.Kind == _carried;

            // Which end the glyph belongs to is the column's, except for the
            // one panel that is not really in it yet. Passed rather than stored
            // beside the slot: a copy would be one more thing to lower on every
            // other path, which is the shape of the bug IsCollapsed was rid of.
            _slotsList[index].Apply(
                step,
                carried,
                carried ? _carriedStubOnRight : _side == PanelSide.Right);
        }
    }

    /// <summary>
    /// Works out where everything belongs this frame and aims the motions at
    /// it. The pinned stack takes its heights from the cascade; the tabs run
    /// below it; the shown panel grows out of its own tab's corner.
    /// </summary>
    private void Retarget()
    {
        // Dropped before anything decides whether to build one: a teardown
        // reached only along the path that goes on to build covers exactly the
        // cases that did not need it, which is how the mirror lasers came to
        // hang over a room that had never had a mirror in it.
        _ghostTarget = null;

        if (_slotsList.Count == 0)
            return;

        var described = new PanelSlotInput[_slotsList.Count];

        for (var index = 0; index < _slotsList.Count; index++)
            described[index] = Describe(_slotsList[index]);

        IReadOnlyList<PanelColumnPlace> places =
            PanelColumnGeometry.Solve(described, Size.Y, _side, SurfaceWidthNow);

        var carriedIndex = -1;

        for (var index = 0; index < _slotsList.Count; index++)
        {
            if (_slotsList[index].Panel.Kind == _carried)
                carriedIndex = index;
        }

        if (carriedIndex >= 0)
        {
            Slot held = _slotsList[carriedIndex];

            _ghostTarget = PanelColumnGeometry.SolveGhost(
                places,
                carriedIndex,
                grown: held.Panel.Kind == _openTemp,
                RequestedBody(held),
                held.Panel.MinBody,
                Size.Y,
                _side,
                SurfaceWidthNow);
        }

        for (var index = 0; index < _slotsList.Count; index++)
        {
            Slot slot = _slotsList[index];
            PanelColumnPlace place = places[index];

            if (place.Pinned)
            {
                // A carried panel is under the hand wherever it came from. The
                // carry test used to live only in the run below, past the line
                // that skips every pinned slot, so a pinned panel dragged was
                // never picked up at all — it merely docked itself into
                // whichever stack the preview had put it in while the pointer
                // was elsewhere. Its plan is still consumed and its height
                // still reserved: that hole is what shows where it would land.
                if (slot.Panel.Kind == _carried)
                    slot.Carry(_carriedAt - GlobalPosition);
                else
                    slot.RetargetPinned(place.Rect, place.AutoFolded, _resizing);

                continue;
            }

            Vector2 corner = place.Rect.Position;

            slot.TabTarget = place.Rect;

            // A carried tab is under the hand, not in the run — snapped there,
            // because a thing being dragged that eased towards the pointer
            // would lag behind it. The run around it still glides: the others
            // part because their own targets moved, which the motion carries
            // for nothing.
            // The cut is the same answer whether the panel is open or shut,
            // because it is what the shape *is* rather than something the
            // opening does: given one answer collapsed and another grown, it
            // eased between them and the last card grew a notch only to close
            // it again.
            bool revealsTabs = place.RevealsTabs;

            if (slot.Panel.Kind == _carried)
                slot.Carry(_carriedAt - GlobalPosition);
            else if (slot.Panel.Kind == _openTemp)
                slot.RetargetGrown(corner, GrownShape(slot, corner, revealsTabs), _resizing);
            else
                slot.RetargetCollapsed(corner, revealsTabs, _resizing);
        }
    }

    /// <summary>
    /// Nothing at all with no panels in it, the old dock width once one is
    /// pinned, and the bare rail otherwise.
    /// </summary>
    private float WantedWidth()
    {
        if (_slotsList.Count == 0)
            return 0f;

        foreach (Slot slot in _slotsList)
        {
            if (slot.Placement.Pinned)
                return SurfaceWidthNow + 2f * Inset;
        }

        return RailWidth;
    }

    /// <summary>
    /// One slot as the solver sees it. <c>OpenTemp</c> is deliberately always
    /// false: a panel grown in a wide column is an overlay over the stack (see
    /// <see cref="GrownShape"/>), not a member of the cascade, so only the
    /// pinned entries ever reach <see cref="PanelColumnLayout"/> and its
    /// temporary-panel branch belongs to the bare rail.
    /// </summary>
    private static PanelSlotInput Describe(Slot slot)
    {
        float natural = RequestedBody(slot);

        return new PanelSlotInput(
            slot.Placement.Pinned,
            slot.Placement.Folded,
            OpenTemp: false,
            natural,
            Math.Min(slot.Panel.MinBody, natural));
    }

    /// <summary>
    /// The body height this slot asks for: the height the user dragged it to,
    /// or the content's own measure while they never have. The rule is
    /// <see cref="PanelResize.RequestedBody"/>'s, asked here because this is
    /// where the placement and the measures meet.
    /// </summary>
    private static float RequestedBody(Slot slot) =>
        PanelResize.RequestedBody(
            slot.Placement.Height,
            slot.Panel.MeasureNaturalBody(),
            slot.Panel.MinBody);

    /// <summary>
    /// Draws the hole the carried panel's own reservation leaves. Eased rather
    /// than placed, and for a sharper reason than consistency: the hole is not
    /// a thing that moves, it is the gap between neighbours that are easing.
    /// Ghost and neighbours are retargeted on the same frame through the same
    /// curve over the same duration, so the outline stays welded to the gap;
    /// snapped, it would stand at the destination while the gap was still
    /// closing.
    /// </summary>
    private void ApplyGhost(float delta)
    {
        if (_ghostTarget is { } target)
        {
            // A fresh motion snaps its first target, which is what a thing
            // arriving on screen wants — and it arrives over a hole already
            // open at that very rect, the drop target on the first carried
            // frame being the panel's own place. Nulled on the way out, so the
            // next drag's ghost does not fly in from where this one ended: the
            // honest "first time ever" is a motion that has never been aimed,
            // never a flag saying which way it was aimed last.
            _ghostMotion ??= new ShapeMotion(_timing);
            _ghostMotion.Retarget(target.Rect, target.Cut);
            _ghostFade.Retarget(1f);
        }
        else
        {
            _ghostMotion = null;
            _ghostFade.Retarget(0f);
        }

        _ghostFade.Tick(delta);

        if (_ghostMotion is { } motion)
        {
            motion.Tick(delta);

            // Both edges on whole units, for the reason every other rect is.
            Rect2 rect = motion.Snapped;

            _dropGhost.Position = rect.Position;
            _dropGhost.Size = rect.Size;

            // The shape in the surface's own coordinates: the outline *is* the
            // shape, so the notch travels with the rect on the rect's clock.
            // Left standing while the fade runs out, for the reason the rect is
            // — a Γ closing into a rectangle as it dissolves would sweep back
            // over the tabs it was framing.
            _dropGhost.SetShape(
                PanelShape.For(new Rect2(Vector2.Zero, rect.Size), motion.Cut, _side));
        }

        // The rect is left where it was while the outline fades, so a drop
        // dissolves it in place as the panel settles into it.
        _dropGhost.SelfModulate = new Color(1f, 1f, 1f, _ghostFade.Value);
        _dropGhost.Visible = _ghostFade.Value > 0.001f;
    }

    /// <summary>
    /// Where a shown panel stands: out of its tab's own corner, away from the
    /// edge, keeping the stub the tab occupied. The height is the overlay's own
    /// solve, so one opened near the bottom scrolls rather than hanging off it.
    /// </summary>
    private PanelShape GrownShape(Slot slot, Vector2 corner, bool revealsTabs) =>
        PanelColumnGeometry.SolveGrown(
            corner,
            revealsTabs,
            RequestedBody(slot),
            slot.Panel.MinBody,
            Size.Y,
            _side,
            SurfaceWidthNow);

    private Slot? Find(PanelKind kind)
    {
        foreach (Slot slot in _slotsList)
        {
            if (slot.Panel.Kind == kind)
                return slot;
        }

        return null;
    }

    private void ApplyWidth(float width)
    {
        if (Mathf.IsEqualApprox(CurrentWidth, width))
            return;

        CurrentWidth = width;
        Visible = width > 0f;

        if (_side == PanelSide.Left)
        {
            OffsetLeft = 0f;
            OffsetRight = width;
        }
        else
        {
            OffsetLeft = -width;
            OffsetRight = 0f;
        }

        WidthChanged?.Invoke();
    }

    /// <summary>
    /// One panel's place in the column and the motion carrying it there. There
    /// is no second control for the collapsed state: a tab <em>is</em> the
    /// panel, shrunk to its own glyph, so the slot moves one surface between
    /// the two sizes and the card never has to be swapped for a stand-in.
    /// </summary>
    private sealed class Slot
    {
        private readonly ShapeMotion _shape;

        private bool _autoFolded;

        // The rect the contents are laid out against. Deliberately not touched
        // by a collapse or by a fold: what a panel closing should do is mask
        // its contents away, not re-flow them into a tab on the way down — so
        // both of these hold the last size that actually had room for the
        // contents, and the surface passing through every size between that and
        // nothing is the mask's business alone.
        private float _settledWidth = TabCard;
        private float _settledHeight;
        private float _settledCut;

        public Slot(ChromePanel panel, MotionTiming timing)
        {
            Panel = panel;
            _shape = new ShapeMotion(timing);
        }

        public ChromePanel Panel { get; }

        /// <summary>
        /// Whether the surface has arrived at the tab's own square — a question
        /// about its size, not about where it is going. A tab travelling along
        /// the run is collapsed the whole way; asked of the whole rect it was
        /// not, and everything a tab hides came back for the length of the
        /// slide.
        /// </summary>
        /// <remarks>
        /// Asked of the surface itself rather than of a flag beside it, and
        /// that is the fix for a bug rather than a tidy-up. The flag was raised
        /// by the three retarget paths and by nothing else — so a panel carried
        /// across the window's middle, which is rebuilt as a fresh slot in the
        /// column it arrives in and only ever reaches <see cref="Carry"/>, was
        /// born with it lowered and wore a tab's dress for the length of the
        /// crossing: no title, no chevron, no pin, on a card three hundred units
        /// wide. Third time on this path (see the ledger), so this time the
        /// question is put to the one thing every path writes.
        /// </remarks>
        public bool IsCollapsed => _shape.IsSizeSettled && _shape.Snapped.Size.X <= TabCard;

        public PanelPlacement Placement { get; set; }

        /// <summary>Where the tab belongs, whether or not it is drawn there yet.</summary>
        public Rect2 TabTarget { get; set; }

        public void RetargetPinned(Rect2 rect, bool autoFolded, bool snap = false)
        {
            _autoFolded = autoFolded;
            _settledWidth = rect.Size.X;
            _settledCut = 0f;

            KeepBody(rect);

            // Snapped for a resize in flight: the rect's moving edge is the one
            // under the hand, and easing it would trail the pointer.
            if (snap)
                _shape.SnapTo(rect, 0f);
            else
                _shape.Retarget(rect, 0f);
        }

        public void RetargetGrown(Vector2 tabCorner, PanelShape shape, bool snap = false)
        {
            // Out of the tab rather than out of nowhere: a panel first shown
            // grows from the icon that was standing there.
            //
            // Asked of the motion, which knows whether it has ever been aimed —
            // never of a flag saying which way it was aimed *last*, which every
            // retraction clears. Reading one of those, reopening a panel still
            // closing teleported it into its icon and grew it again, and on the
            // right column threw it a column's width sideways as well. A guard
            // meaning "the first time ever" must read state that is only ever
            // set once.
            if (!_shape.IsStarted)
                _shape.SnapTo(CollapsedRect(tabCorner), 0f);

            _autoFolded = false;
            _settledWidth = shape.Rect.Size.X;
            _settledCut = shape.Cut;

            KeepBody(shape.Rect);

            if (snap)
                _shape.SnapTo(shape.Rect, _settledCut);
            else
                _shape.Retarget(shape.Rect, _settledCut);
        }

        public void RetargetCollapsed(Vector2 tabCorner, bool revealsTabs, bool snap = false)
        {
            _autoFolded = false;
            _settledCut = PanelNotch.Reach(pinned: false, revealsTabs);

            // Snapped with the rest of the column during a resize: on a
            // mirrored column the run hangs off the width being dragged, and a
            // run easing after a stack that snaps would shear the column.
            if (snap)
                _shape.SnapTo(CollapsedRect(tabCorner), _settledCut);
            else
                _shape.Retarget(CollapsedRect(tabCorner), _settledCut);
        }

        /// <summary>
        /// Under the hand for as long as it is held, at the size it already
        /// was. A panel is carried as the thing it is: picked up open it
        /// travels open and lands open, picked up as a tab it travels as a tab.
        /// Nothing else about it is touched, so what it was is what is put down.
        /// </summary>
        public void Carry(Vector2 corner)
        {
            // A panel carried across the middle is rebuilt as a fresh slot in
            // the column it arrives in, and a fresh slot knows nothing: its
            // motion has never been aimed and its laid-out width is a tab's.
            // Carrying only moves the surface, so nothing else would ever tell
            // it otherwise — it arrived as a rect of nothing, and once that was
            // seeded, as an open panel wearing a tab's layout. Everything the
            // crossing loses is taken from what the surface is actually drawn
            // as, and what cannot be taken from there is not kept here: whether
            // this is a tab is IsCollapsed's question, and it asks the shape.
            if (!_shape.IsStarted)
            {
                _shape.SnapTo(new Rect2(corner, Panel.Size), Panel.NotchWidth);
                _settledWidth = Panel.Size.X;
                _settledCut = Panel.NotchWidth;

                KeepBody(new Rect2(corner, Panel.Size));
            }

            _shape.CarryTo(corner);
        }

        /// <summary>Whether the surface has stopped moving, whatever size it stopped at.</summary>
        public bool IsStill => _shape.IsSettled;

        /// <summary>
        /// Moves the surface a step and hands it what it should be wearing at
        /// the size it has reached. One card the whole way: shrunk to the tab's
        /// square it is the tab, and there is no moment at which one control is
        /// exchanged for another.
        /// </summary>
        /// <param name="carried">
        /// Whether this panel is the one in hand. Passed in rather than kept
        /// here, because the column is the sole authority on it and a copy
        /// beside the slot would be one more thing every other path has to
        /// remember to lower — which is exactly the bug <see cref="IsCollapsed"/>
        /// was just rid of.
        /// </param>
        /// <param name="stubOnRight">
        /// Which end of the card the glyph belongs to: the column's own side,
        /// or — while it is in hand — the side it was picked up from.
        /// </param>
        public void Apply(float delta, bool carried, bool stubOnRight)
        {
            _shape.Tick(delta);

            // Both edges on whole units before it is handed over, or Godot's
            // own snapping rounds the size while leaving the position alone and
            // anything anchored to the far edge shivers. See RectMotion.Snapped.
            Rect2 rect = _shape.Snapped;
            float body = Math.Max(0f, rect.Size.Y - Chrome);

            Panel.SetShown(true);
            Panel.Position = rect.Position;
            Panel.Size = rect.Size;
            Panel.ApplyPlan(
                bodyVisible: body > 0.5f,
                settledHeight: _settledHeight,
                pinned: Placement.Pinned,

                // What the chevron says is about what is on screen, not about
                // what is stored: a panel the cascade folded shows no body, so
                // it must not draw the open chevron over it. The stored flag
                // and the derived one stay apart for the header click, which
                // means different things by them.
                folded: (Placement.Pinned && Placement.Folded) || _autoFolded,
                autoFolded: _autoFolded,
                collapsed: IsCollapsed,
                carried: carried,

                // The motion's own value, never a literal for the pinned case:
                // RetargetPinned already aims it at zero, and short-circuiting
                // it here made pinning drop the notch in one frame while
                // unpinning opened it smoothly — the one transition the stub
                // motion exists for, run outright in one direction.
                cutWidth: _shape.Cut,
                stubOnRight: stubOnRight,

                // The rect this is heading for, so everything inside is laid
                // out where it will stand and the surface merely uncovers it.
                settledWidth: _settledWidth,
                settledCut: _settledCut);
        }

        /// <summary>
        /// Remembers how much room the contents have, from a rect that has some.
        /// A fold aims the surface at the header alone and a collapse at the
        /// tab's square, and neither is a statement about how tall the body is:
        /// it is the same body, about to be covered up.
        /// </summary>
        private void KeepBody(Rect2 rect)
        {
            float body = rect.Size.Y - Chrome;

            if (body > 0f)
                _settledHeight = body;
        }

        private static Rect2 CollapsedRect(Vector2 tabCorner) =>
            new(tabCorner, new Vector2(TabCard, TabCard));
    }
}
