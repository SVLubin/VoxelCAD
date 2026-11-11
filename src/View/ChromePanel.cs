using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// One chrome panel of <c>docs/panels.md</c>: a glass card with a header row —
/// chevron, glyph, title, pin — over a body the column sizes. The header is one
/// component in three roles: the cap of an expanded panel, a folded pinned
/// panel, and the tab of an unpinned one; which role it is playing is the
/// column's answer, applied through <see cref="ApplyPlan"/>.
/// </summary>
/// <remarks>
/// Deliberately decision-free: what a header click means (fold? open?) and how
/// tall the body may be are the shell's and the solver's. The panel only
/// reports presses and wears what it is told — the same standing rule every
/// panel view follows, because a Godot node cannot be built without an engine.
/// </remarks>
internal sealed partial class ChromePanel : Control
{
    /// <summary>
    /// The header's height, which is also the side of a collapsed tab: a tab is
    /// this panel shrunk to its own icon, and the icon must not move a pixel
    /// when it grows. See <c>docs/panels.md</c>.
    /// </summary>
    public const float HeaderHeight = 28f;

    /// <summary>
    /// How far the glyph sits from the surface's left edge, in the header and
    /// in a collapsed tab alike — the other half of the icon standing still.
    /// </summary>
    public const float IconInset = 6f;

    /// <summary>
    /// How far inside the surface its contents begin. The stub the shape keeps
    /// is measured from the same place, or the notch cuts a unit into the very
    /// header it is supposed to stop under.
    /// </summary>
    public const float FrameInset = 1f;

    /// <summary>
    /// The square a collapsed panel occupies: its header, plus the surface's
    /// own frame on either side of it. A tab is exactly this square, and the
    /// stub a grown panel keeps is exactly this square too — which is what
    /// makes the two the same thing rather than nearly the same thing. Measured
    /// one unit short on each count once, and the panel then covered the last
    /// unit of every tab it was supposed to be revealing.
    /// </summary>
    public const float CollapsedSide = HeaderHeight + 2f * FrameInset;

    /// <summary>
    /// What the cut leaves between its own edge and the card it reveals, and —
    /// being the same number — what a run puts between one tab and the next, so
    /// a tab standing in a notch is framed evenly on every side. A cut that
    /// stopped exactly at the card's edge laid the surface's hairline along the
    /// tab's own: the two read as welded rather than as one thing standing
    /// clear of another.
    /// </summary>
    public const float TabClearance = 6f;

    /// <summary>The gap between the header's band and the body under it.</summary>
    public const float BodyGap = 4f;

    /// <summary>
    /// What a panel gives its contents, and the one width written down anywhere
    /// in the chrome. Everything else about how wide something is — the surface,
    /// the surface with a notch bitten out of it, the column that holds them —
    /// is solved backwards from here.
    /// </summary>
    /// <remarks>
    /// It is the primitive because it is the only one a person can judge: a body
    /// is what holds the inspector's rows and the project tree's names, and a
    /// dock is just what has to be built around it. Written the other way round
    /// — one number per dock, the body left over after subtracting a frame and
    /// sometimes a notch — a panel had <em>three</em> body widths per column,
    /// 290/298/334 on the left against 320/328/364 on the right, none of which
    /// anybody chose. The number itself is the inspector's shipped body, which is
    /// the tightest thing the chrome carries.
    /// </remarks>
    public const float BodyWidth = 320f;

    /// <summary>
    /// The surface that body needs, notch aside: it is the same whether the
    /// panel is pinned in the stack or grown out of its tab, which is what
    /// makes one panel one width in every state it has.
    /// </summary>
    public const float SurfaceWidth = BodyWidth + 2f * FrameInset;

    /// <summary>What the pin occupies at the header's far end.</summary>
    internal const float PinWidth = 26f;

    internal const float PinInset = 2f;

    /// <summary>What the row of words leaves clear of the pin at the far end.</summary>
    internal const float PinClearance = PinWidth + 2f * PinInset;

    /// <summary>
    /// The header width below which the pin would crowd the glyph rather than
    /// stand beside it. A collapse narrows the header all the way to the tab's
    /// side, and the pin sits against the opposite edge — so it stands down
    /// first.
    /// </summary>
    /// <remarks>
    /// Measured against what the row of words actually gives up, which is the
    /// pin's whole <see cref="PinClearance"/> and not just its width: reckoned
    /// the other way this was two units short, and at exactly that width the
    /// pin arrived and squeezed the glyph to fourteen units. Found by the
    /// solver's own sweep, never by looking.
    /// </remarks>
    internal const float PinRoom = IconInset + GlyphSide + PinClearance;

    internal const float GlyphSide = 16f;

    /// <summary>The resize grip's square, in the surface's inner-bottom corner.</summary>
    internal const float GripSide = 16f;

    /// <summary>How far the grip stands in from the frame's corner.</summary>
    internal const float GripInset = 2f;

    /// <summary>What stands between the glyph, the words and the chevron.</summary>
    internal const float TextGap = 4f;

    private readonly string _title;
    private readonly Control _content;
    private readonly bool _contentScrolls;
    private readonly float? _minBodyOverride;
    private readonly Func<float>? _measureNatural;

    /// <summary>
    /// Where a body that scrolls itself stands. The inspector goes through a
    /// <see cref="ScrollContainer"/> this panel made and is asked directly; the
    /// two trees crank a scrollbar of their own, and only they can be asked.
    /// </summary>
    private readonly Func<ScrollExtent>? _measureScroll;

    /// <summary>
    /// How far over the pin lies while the panel is not pinned. A right angle,
    /// so it reads as put down rather than as slightly crooked; upright is
    /// stuck in, which is what the panel being pinned means.
    /// </summary>
    private const float PinLooseDegrees = 90f;

    private readonly MotionTiming _timing;
    private readonly Smoothed _pinDriven;
    private readonly TextureRect _pinGlyph = new() { Name = "PinGlyph" };

    /// <summary>How much of the chevron is there: it fades and opens together.</summary>
    private readonly Smoothed _chevronDriven;

    /// <summary>
    /// How far the chevron is turned over: nought points down, one points up.
    /// </summary>
    private readonly Smoothed _chevronTurned;

    private readonly GlassBackdrop _glass;
    private readonly Button _header;
    private readonly Button _pin;
    private readonly Label _titleLabel;
    private readonly TextureRect _chevron;
    private readonly TextureRect _glyph;
    private readonly Control _bodyHost;
    private readonly TextureRect _grip = new() { Name = "Grip" };
    private readonly ScrollFade _fade = new();
    private readonly Control _contentPane;
    private readonly ScrollContainer? _scroll;

    private Color _recededPin = new(1f, 1f, 1f, 0.45f);

    private bool _iconsLoaded;
    private bool _pinnedVisual = true;
    private bool _foldedVisual;
    private bool _collapsedVisual;
    private bool _carriedVisual;
    private bool _bodyShownVisual = true;
    private bool _mirroredVisual;

    public ChromePanel(
        PanelKind kind,
        string title,
        Control content,
        MotionTiming timing,
        bool contentScrolls,
        float? minBody = null,
        Func<float>? measureNatural = null,
        Func<ScrollExtent>? measureScroll = null,
        bool heightResizable = true)
    {
        _timing = timing;
        _pinDriven = new Smoothed(MotionSpeed.Opacity, timing);
        _chevronDriven = new Smoothed(MotionSpeed.Opacity, timing);
        _chevronTurned = new Smoothed(MotionSpeed.Opacity, timing);

        Kind = kind;
        _title = title;
        _content = content;
        _contentScrolls = contentScrolls;
        _minBodyOverride = minBody;
        _measureNatural = measureNatural;
        _measureScroll = measureScroll;
        HeightResizable = heightResizable;

        Name = $"{kind}Panel";

        // A plain Control, and every part inside it is placed outright by
        // ApplyPlan rather than by a container. A container sizes its children
        // from a deferred sort while their position follows the parent at once,
        // so on the frame the panel's rect changes the two disagree by exactly
        // one step — and on a mirrored column, where every part is measured
        // from the far edge, that is a unit of shiver per tick for the whole
        // length of every animation. See PanelLayoutSolver.
        _glass = new GlassBackdrop();
        _glass.AnchorRight = 1f;
        _glass.AnchorBottom = 1f;
        AddChild(_glass);

        // Clipped, because a collapse narrows this all the way to the tab's own
        // side: the words and the chevron slide out of the far edge as it
        // closes rather than spilling over the scene, while the glyph — pinned
        // against the stub's own edge — stays put.
        // No tooltip, on purpose. Collapsed this is the tab, and hovering a tab
        // is what *opens* the panel — so the hint arrived floating over the very
        // surface it had just summoned. Grown, the title is standing there in
        // words and the chevron says what a press does.
        _header = new Button
        {
            ThemeTypeVariation = "PanelHeader",
            ClipContents = true
        }.MouseOnly().Named($"{kind}Header");
        _header.Pressed += () => HeaderPressed?.Invoke();

        _chevron = HeaderGlyph();
        _glyph = HeaderGlyph();

        _titleLabel = new Label
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore
        };

        // The glyph at the tab's own inset, so a collapsed tab and an expanded
        // header put it in the same pixel; the chevron at the far end, for the
        // same reason. All three placed outright by the solver — an HBox was
        // the last container in here, and a row too wide for its box grew away
        // from the near edge and carried the glyph out of the header.
        _header.AddChild(_glyph);
        _header.AddChild(_titleLabel);
        _header.AddChild(_chevron);

        // Drawn after the strip so a press on the pin is the pin's, not the
        // header's — the engine hands the click to the topmost control. Full
        // header height: a flat button centres its icon itself, and a box
        // shorter than the icon's own margins drew the pin visibly low.
        _pin = new Button
        {
            Flat = true,
            TooltipText = "Pin the panel into the column; unpinned it folds to a tab when the pointer leaves."
        }.MouseOnly().Named($"{kind}Pin");
        _pin.Pressed += () => PinPressed?.Invoke();

        // The glyph is a child of the button rather than the button's own Icon,
        // because a Button draws its icon and this one turns: only a control of
        // its own has a rotation and a pivot to turn about.
        _pinGlyph.StretchMode = TextureRect.StretchModeEnum.KeepCentered;
        _pinGlyph.MouseFilter = MouseFilterEnum.Ignore;
        _pinGlyph.SetAnchorsPreset(LayoutPreset.FullRect);
        _pin.AddChild(_pinGlyph);

        _header.AddChild(_pin);

        ApplyMirroring(mirrored: false);

        // The clip window the body is revealed through. It is the one that
        // follows the animation; what is inside stands still.
        _bodyHost = new Control { Name = "Body", ClipContents = true };

        if (_contentScrolls)
        {
            _contentPane = content;
        }
        else
        {
            _scroll = new ScrollContainer
            {
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
            }.Named("Scroll");
            content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _scroll.AddChild(content);
            _contentPane = _scroll;
        }

        // The mask stands between the clip window and what scrolls inside it,
        // so the body is cut softly at whichever edge has content running past
        // it. Anchored rather than laid out: it is a wrapper, and a wrapper
        // that pushed back on its size is the trap this panel already paid for.
        _bodyHost.AddChild(_fade);
        _fade.AddChild(_contentPane);

        // The corner grip a resize drag takes hold of. It stops the mouse so a
        // press on it does not land on the row beneath, and that is the whole
        // of its event handling: the gesture itself is polled by the shell,
        // like every drag in this chrome, so an injected pointer performs it
        // the way a hand does. The machine cursor's shape is set for the hand's
        // benefit alone.
        _grip.MouseFilter = MouseFilterEnum.Stop;
        _grip.StretchMode = TextureRect.StretchModeEnum.KeepCentered;
        _grip.TooltipText = heightResizable
            ? "Drag to set this panel's height and the side's width."
            : "Drag to set the side's width.";
        _grip.Named($"{kind}Resize");

        // Flat, and in this order: the glass under everything, then the header,
        // then the body, then the grip over the body's corner. No wrappers — a
        // wrapper here is either a container (which defers) or a plain Control
        // that stops the mouse over the notch and steals the presses meant for
        // the tabs it reveals. Both were shipped.
        AddChild(_header);
        AddChild(_bodyHost);
        AddChild(_grip);
    }

    public event Action? HeaderPressed;

    public event Action? PinPressed;

    /// <summary>
    /// The body is about to be hidden while something inside it holds the
    /// keyboard. Raised from every hide path — the solver's auto-fold and the
    /// rail transition included, which no explicit gesture covers — because a
    /// hidden control releases focus to nobody, measured twice in this codebase.
    /// </summary>
    public event Action? FocusEvicted;

    public PanelKind Kind { get; }

    public string Title => _title;

    /// <summary>Where the header stands on screen — what a drag is picked up by.</summary>
    public Rect2 HeaderGlobalRect => _header.GetGlobalRect();

    /// <summary>
    /// Whether the panel is currently displayed folded by the cascade rather
    /// than by its stored flag. The shell asks before deciding what a header
    /// click means: on an auto-folded panel it reads as "open", and toggling
    /// the stored flag would persist the very fold the user was undoing.
    /// </summary>
    public bool ShownAutoFolded { get; private set; }

    /// <summary>
    /// Whether the grip moves this panel's height as well as the side's width.
    /// A panel whose body is a fixed row of controls — the toolbar — has no
    /// height to speak of, so its grip pulls the width alone.
    /// </summary>
    public bool HeightResizable { get; }

    /// <summary>The body height on screen, which is what a resize starts from.</summary>
    public float CurrentBodyHeight => _bodyHost.Size.Y;

    /// <summary>
    /// Where the resize grip stands on screen, or null where none is shown —
    /// the shell's polled drag asks, so the answer has to be about what is
    /// actually shown, not about what exists.
    /// </summary>
    public Rect2? GripGlobalRect =>
        _grip.Visible ? _grip.GetGlobalRect() : null;

    /// <summary>
    /// How wide the cut below the stub is — the stub being the collapsed square
    /// this surface grew out of, and the cut being what is taken back off it so
    /// the tabs underneath show through. Zero on a pinned panel, and zero
    /// whenever there is no body for the cut to run past.
    /// </summary>
    public float NotchWidth { get; private set; }

    public bool StubOnRight { get; private set; }

    /// <summary>
    /// The notch is not this control's to answer for: a press there belongs to
    /// whatever the surface was cut away to reveal. Asked of the same two
    /// numbers the shader is given, so what is drawn and what is hittable
    /// cannot come apart.
    /// </summary>
    /// <remarks>
    /// Through the very function the column's press layer calls, so what the
    /// engine routes an event to and what that layer calls the owner of a press
    /// are one answer rather than two that agree today.
    /// </remarks>
    public override bool _HasPoint(Vector2 point) =>
        PanelPressTargets.Covers(
            new Rect2(Vector2.Zero, Size), NotchWidth, StubOnRight, point);

    /// <summary>The least body worth granting; null in the constructor meant "the natural".</summary>
    public float MinBody => _minBodyOverride ?? MeasureNaturalBody();

    /// <summary>
    /// Shows or hides the whole panel, walking the keyboard out first — the
    /// rail transition hides panels wholesale, and that path strands focus
    /// exactly like a hidden body does.
    /// </summary>
    public void SetShown(bool shown)
    {
        if (!shown && Visible && HoldsFocus())
            FocusEvicted?.Invoke();

        Visible = shown;
    }

    /// <summary>Whether the keyboard is currently held by something inside this panel.</summary>
    public bool HoldsFocus()
    {
        Control? owner = GetViewport()?.GuiGetFocusOwner();

        return owner != null && IsAncestorOf(owner);
    }

    public float MeasureNaturalBody() =>
        _measureNatural?.Invoke() ?? _content.GetCombinedMinimumSize().Y + 12f;

    /// <summary>
    /// Wears the column's answer: whether the body shows and at what height,
    /// and what the chevron and pin should say about the panel's state.
    /// </summary>
    /// <param name="cutWidth">
    /// How far in the notch reaches, animated by the column. The whole distance
    /// rather than the stub with the clearance added on here: a cut derived
    /// from a stub jumps by the clearance the instant the stub leaves zero,
    /// whereas a number that <em>is</em> the cut opens from nothing.
    /// </param>
    /// <param name="collapsed">
    /// Whether this surface has arrived at the tab's own square. A tab is the
    /// panel shrunk to its glyph — the same card, not a button standing in for
    /// one — so what changes here is only what no longer fits: the words, the
    /// chevron and the pin stand down, and the band takes all four corners
    /// because there is no body left for it to cap.
    /// </param>
    /// <param name="carried">
    /// Whether the panel is under the hand. While it is, its header is cargo
    /// rather than a button, and wears the one look the drag gives it.
    /// </param>
    /// <param name="settledHeight">
    /// The height the body's contents are laid out against — the mask rule's
    /// vertical half, and the column's answer rather than this frame's. A fold
    /// closes the clip over a body that stands still; sized by the mask
    /// instead, a scrolling body re-measured on every frame of it.
    /// </param>
    public void ApplyPlan(
        bool bodyVisible,
        float settledHeight,
        bool pinned,
        bool folded,
        bool autoFolded,
        bool collapsed = false,
        bool carried = false,
        float cutWidth = 0f,
        bool stubOnRight = false,
        float settledWidth = 0f,
        float settledCut = 0f)
    {
        LoadIcons();

        ShownAutoFolded = autoFolded;

        // The notch exists to let the tabs below show past the body. With no
        // body there is nothing to cut it past, and cutting anyway would take a
        // bite out of the tab's own underside.
        float cut = bodyVisible ? cutWidth : 0f;

        NotchWidth = cut;
        StubOnRight = stubOnRight;
        _glass.SetShape(new PanelShape(new Rect2(Vector2.Zero, Size), cut, stubOnRight));

        // Every rect in one pass, in the same call that was handed the panel's
        // own — never through a container, whose sort lands a frame later.
        PanelLayout layout = PanelLayoutSolver.Solve(
            Size,
            settledWidth,
            settledHeight,
            cut,
            bodyVisible ? settledCut : 0f,
            stubOnRight,
            _chevronDriven.Value);

        Apply(_header, layout.Header);
        Apply(_bodyHost, layout.Body);
        Apply(_glyph, layout.Glyph);
        Apply(_titleLabel, layout.Title);
        Apply(_chevron, layout.Chevron);
        Apply(_pin, layout.Pin);
        Apply(_contentPane, layout.Content);
        Apply(_grip, layout.Grip);

        // Only where there is a body to resize: a tab, a folded header and a
        // panel in the hand each have nothing a pull could mean. Mirrored with
        // the header — one drawing, flipped — and the machine cursor mirrors
        // with it.
        _grip.Visible = bodyVisible && !collapsed && !carried;
        _grip.FlipH = stubOnRight;
        _grip.MouseDefaultCursorShape = HeightResizable
            ? (stubOnRight ? CursorShape.Bdiagsize : CursorShape.Fdiagsize)
            : CursorShape.Hsize;

        if (_mirroredVisual != stubOnRight)
        {
            _mirroredVisual = stubOnRight;
            ApplyMirroring(stubOnRight);
        }

        if (!bodyVisible && _bodyHost.Visible && HoldsFocus())
            FocusEvicted?.Invoke();

        _bodyHost.Visible = bodyVisible;

        // Asked of the width the contents are laid out against, not of the one
        // the surface is passing through: under the mask rule the pin stands
        // where it will stand and is uncovered, so whether it is there at all
        // is a question about the settled header, not about this frame.
        _pin.Visible = !collapsed && layout.PinFits;

        if (_pinnedVisual != pinned
            || _foldedVisual != folded
            || _collapsedVisual != collapsed
            || _carriedVisual != carried
            || _bodyShownVisual != bodyVisible
            || !_iconsLoaded)
        {
            _pinnedVisual = pinned;
            _foldedVisual = folded;
            _collapsedVisual = collapsed;
            _carriedVisual = carried;
            _bodyShownVisual = bodyVisible;
            ApplyStateVisuals();
        }
    }

    public override void _Notification(int what)
    {
        if ((long)what == NotificationThemeChanged)
            _iconsLoaded = false;
    }

    /// <summary>
    /// Turns the pin. One number for both halves of it — how far over it lies
    /// and how lit it is — because they are one fact said twice: pinned is
    /// upright and bright, loose is on its side and quiet, and driving them
    /// apart would let a pin arrive somewhere between the two readings.
    /// </summary>
    /// <remarks>
    /// The pivot is set every frame rather than once, because the glyph's rect
    /// follows the header's and a pivot left at an old middle turns the pin
    /// about a point beside itself.
    /// </remarks>
    public override void _Process(double delta)
    {
        _pinDriven.Tick((float)delta);
        _chevronDriven.Tick((float)delta);
        _chevronTurned.Tick((float)delta);

        float driven = _pinDriven.Value;

        _pinGlyph.PivotOffset = _pinGlyph.Size * 0.5f;
        _pinGlyph.Rotation = Mathf.DegToRad(PinLooseDegrees * (1f - driven));
        _pinGlyph.SelfModulate = _recededPin.Lerp(Colors.White, driven);

        DriveChevron();

        _fade.SetStrength(SolveFade());
    }

    /// <summary>
    /// The chevron's two motions, which are one gesture said twice — how much
    /// of the mark is facing you — on one axis.
    /// </summary>
    /// <remarks>
    /// <b>The mark says what the press will do, not what the panel is.</b>
    /// Folded, it points down: press it and the body comes down. Open, it points
    /// up: press it and the body goes away. The disclosure triangle's own
    /// convention is the other one — the mark as a picture of the state — and
    /// this is a header you press rather than a twisty beside a row, so what it
    /// is for is what it should describe.
    /// <para>
    /// That reading is also what makes the turn honest. Down and up are the same
    /// glyph mirrored across its own middle, so <c>scale.y</c> running from +1
    /// to −1 <b>is</b> the mark turning over: nothing is swapped at the midpoint
    /// and there is no second picture. The version this replaced pointed right
    /// when folded, which is a <em>rotation</em> away from down rather than a
    /// mirror — so a scale flip squeezed one glyph to nothing and produced a
    /// different one, which is a trade dressed as a turn.
    /// </para>
    /// <para>
    /// One axis for both, because appearing and turning are the same sentence
    /// about this mark: it unfolds from its own horizontal and it turns over
    /// through it. The two numbers multiply — a panel that stops being pinned
    /// half-way through a turn has one answer, not two fighting over it. And
    /// nothing here is a special case for animation being off:
    /// <see cref="Smoothed"/> snaps on that setting, so both arrive on the frame
    /// they are aimed.
    /// </para>
    /// <para>
    /// The pivot is written every frame for the pin's own reason: the glyph's
    /// rect follows the header's, and a pivot left at an old middle turns the
    /// mark about a point beside itself.
    /// </para>
    /// </remarks>
    private void DriveChevron()
    {
        float present = _chevronDriven.Value;
        float turned = _chevronTurned.Value;

        _chevron.PivotOffset = _chevron.Size * 0.5f;
        _chevron.Scale = new Vector2(1f, present * (1f - 2f * turned));
        _chevron.SelfModulate = new Color(1f, 1f, 1f, present);
    }

    /// <summary>
    /// How far this body has run past each of its edges, whichever way it
    /// scrolls. The arithmetic is <see cref="ScrollFadeEdges"/>'s; all this does
    /// is find the two numbers to hand it.
    /// </summary>
    private ScrollFadeStrength SolveFade()
    {
        if (!_bodyHost.Visible || _bodyHost.Size.Y <= 0f)
            return ScrollFadeStrength.None;

        if (_scroll is not null)
        {
            VScrollBar bar = _scroll.GetVScrollBar();

            return ScrollFadeEdges.Solve(
                new ScrollExtent(
                    (float)bar.Value, (float)Mathf.Max(0d, bar.MaxValue - bar.Page)));
        }

        // A body that scrolls itself answers both halves or neither: how far it
        // has gone and how far it may go come off the same bar, so the two
        // cannot be read against different rulers.
        return ScrollFadeEdges.Solve(_measureScroll?.Invoke() ?? ScrollExtent.None);
    }

    /// <summary>
    /// Turns the header round for a column whose stub is on the right. The
    /// glyph belongs against the stub's own edge — that is where the tab was,
    /// and the icon may not move when the panel grows out of it — so on the
    /// right it goes last in the row, the words read inward from it, and the
    /// pin takes the far end. Without this the right column's glyph jumped the
    /// whole width of the panel every time it opened.
    /// </summary>
    /// <remarks>
    /// The grow direction is the other half of it: a narrowing header cannot
    /// hold the whole row, and Godot spills an over-wide box to the right by
    /// default — which on the right column would carry the glyph out of the
    /// stub and clip it. Growing towards the beginning instead keeps the glyph
    /// against the edge it belongs to and clips the words off the other side,
    /// mirroring what the left column already does.
    /// </remarks>
    private void ApplyMirroring(bool mirrored)
    {
        // Where each of them stands is the solver's; all that is left here is
        // which way the words read — toward the glyph, so toward the edge the
        // stub is on.
        _titleLabel.HorizontalAlignment =
            mirrored ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    private static TextureRect HeaderGlyph() => new()
    {
        StretchMode = TextureRect.StretchModeEnum.KeepCentered,
        MouseFilter = MouseFilterEnum.Ignore
    };

    /// <summary>
    /// Puts one part where the solver says it belongs, outright — never through
    /// anchors, and never through a container.
    /// </summary>
    private static void Apply(Control control, Rect2 rect)
    {
        control.Position = rect.Position;
        control.Size = rect.Size;
    }

    private void LoadIcons()
    {
        if (_iconsLoaded || !IsInsideTree())
            return;

        _iconsLoaded = true;
        _glyph.Texture = GetThemeIcon(
            EditorIconCatalog.ForPanel(Kind), EditorThemeFactory.SemanticType);
        _pinGlyph.Texture = GetThemeIcon(
            EditorIconCatalog.PanelPin, EditorThemeFactory.SemanticType);
        _grip.Texture = GetThemeIcon(
            HeightResizable
                ? EditorIconCatalog.ResizeDiagonal
                : EditorIconCatalog.ResizeHorizontal,
            EditorThemeFactory.SemanticType);

        // One mark for both readings — the up one is this one turned over, and
        // turning it over is what the fold animates. See DriveChevron.
        _chevron.Texture = GetThemeIcon(
            EditorIconCatalog.ChevronDown, EditorThemeFactory.SemanticType);
        _recededPin = GetThemeColor("icon_receded", EditorThemeFactory.SemanticType);

        ApplyStateVisuals();
    }

    private void ApplyStateVisuals()
    {
        if (!IsInsideTree())
            return;

        // With no body the band caps the whole card and takes all four of its
        // corners — folded in the stack or shrunk to a tab, the same sentence.
        // Over a body it keeps the two it is flush against square.
        _header.ThemeTypeVariation = _bodyShownVisual ? "PanelHeader" : "PanelHeaderAlone";

        _titleLabel.Visible = !_collapsedVisual;

        // A fold is "keep reserving my room but show only my header", so only a
        // pinned panel has anything to say with it. On a tab's temporary panel
        // the header press means dismiss the glance instead, and a chevron there
        // was an affordance pointing at a gesture that is not offered — the same
        // header, two sentences, one of them drawn and not spoken.
        //
        // A tab hides it outright, like the title: that is the panel shrinking
        // into its own glyph, not the mark going away. Between pinned and not,
        // it turns away and back.
        _chevron.Visible = !_collapsedVisual;
        _chevronDriven.Retarget(_pinnedVisual ? 1f : 0f);

        // Up while the body is showing, because pressing it takes the body
        // away; down while it is folded, because pressing it brings it back.
        _chevronTurned.Retarget(_foldedVisual ? 0f : 1f);

        _pinDriven.Retarget(_pinnedVisual ? 1f : 0f);

        ApplyCarriedBand();
    }

    /// <summary>
    /// While the panel is in hand its header wears one band, and it is the lit
    /// one: the card is under the hand, so it should look held.
    /// </summary>
    /// <remarks>
    /// It is not a matter of taste which band, but it is a matter of correctness
    /// that it is <em>one</em>. A <see cref="Button"/> takes its draw mode from
    /// the engine's pointer against its own rect, and this chrome places that
    /// rect from <c>_Process</c> — a frame after the motion event the engine
    /// tested it with. So the local point the header is asked about is the grab
    /// offset plus the pointer's travel since the last frame, and a header is
    /// 28 units tall: drag briskly and it leaves the band on some frames and not
    /// others, which is the header shivering between its normal and hover
    /// shades for the length of every drag. Crossing to the other column is the
    /// same defect outright, the reparent dropping the engine's grip on the
    /// press — measured over four frames as Pressed, Normal, Normal, Hover.
    /// <para>
    /// Overriding the idle band rather than filtering the mouse, because the
    /// engine keeps feeding motion to whatever the press grabbed whatever its
    /// mouse filter says, and because the press itself is already answered
    /// correctly (<c>ShellView._pressAnswered</c>) — what was wrong was only
    /// ever the paint. The lit band is read back through the header's own type
    /// variation, so a carried tab and a carried panel each wear the right one.
    /// </para>
    /// </remarks>
    private void ApplyCarriedBand()
    {
        if (!_carriedVisual)
        {
            _header.RemoveThemeStyleboxOverride("normal");
            _header.RemoveThemeColorOverride("font_color");

            return;
        }

        _header.AddThemeStyleboxOverride("normal", _header.GetThemeStylebox("hover"));
        _header.AddThemeColorOverride("font_color", _header.GetThemeColor("font_hover_color"));
    }
}
