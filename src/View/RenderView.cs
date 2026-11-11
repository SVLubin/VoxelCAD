using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The render form: a glass card over the window holding a picture that arrives a
/// little at a time, the dials that decide what it is a picture of, and the two
/// things a person does with one.
/// </summary>
/// <remarks>
/// A card rather than a dock panel, because a render is not something you work
/// beside — it is something you ask for, watch arrive and then put away, which is
/// what MagicaVoxel's and Goxel's own render views are. It arrives and leaves like
/// the settings card and for the same reasons, including the one that matters:
/// paint eases, refusal does not.
/// </remarks>
internal sealed partial class RenderView : Control, IRenderView
{
    private const float FieldWidth = 84f;

    /// <summary>
    /// The floor under the settings column beside the picture — wide enough for a
    /// labelled field. What it actually takes is measured from the dials
    /// (FitSettingsColumn), because the widest of them is a font's answer.
    /// </summary>
    private const float SettingsWidth = 150f;

    /// <summary>
    /// Room for the zoom readout at any zoom it reaches, so the buttons beside it
    /// do not step sideways as the number grows a digit.
    /// </summary>
    private const float ZoomWidth = 44f;

    /// <summary>
    /// As much room as a note beside the progress may ever take. It asks for what
    /// its own words need and no more; the ceiling is what stops a long one — a
    /// save that failed naming a path — dragging the card wider with it, and it
    /// clips instead.
    /// </summary>
    private const float MessageCeiling = 260f;
    private const float RecededAlpha = 0.5f;

    /// <summary>The corner grip's square, and how far it stands in from the frame.</summary>
    private const float GripSide = 16f;

    private const float GripInset = 4f;

    /// <summary>How much window is left round the card at its largest.</summary>
    private const float CardMargin = 24f;

    /// <summary>Barely a size change: more than this reads as a popup out of a game.</summary>
    private const float ArrivalScale = 0.98f;

    private readonly RenderCanvas _canvas = new();

    private readonly ScrollFade _settingsFade = new();

    private readonly Label _progress = new() { Name = "RenderProgress" };
    private readonly Label _message = new() { Name = "RenderMessage" };
    private readonly Label _zoom = new() { Name = "RenderZoom" };

    private readonly SpinBox _width = new() { Name = "RenderWidth" };
    private readonly SpinBox _height = new() { Name = "RenderHeight" };
    private readonly SpinBox _samples = new() { Name = "RenderSamples" };
    private readonly SpinBox _exposure = new() { Name = "RenderExposure" };
    private readonly SpinBox _sunElevation = new() { Name = "RenderSunElevation" };
    private readonly SpinBox _sunAzimuth = new() { Name = "RenderSunAzimuth" };
    private readonly SpinBox _sunSize = new() { Name = "RenderSunSize" };
    private readonly SpinBox _glare = new() { Name = "RenderGlare" };

    private readonly SegmentedRow _backdropRow = new() { Name = "RenderBackdropRow" };

    private readonly List<RenderBackdrop> _backdropsList = new();

    private RenderBackdrop _backdrop = RenderBackdrop.Environment;

    private VScrollBar? _settingsScrollBar;
    private ScrollContainer? _settingsScroll;
    private PanelContainer? _settingsPanel;
    private VBoxContainer? _dials;

    private readonly CheckBox _ground = new()
    {
        Name = "RenderGround",
        Text = "Floor"
    };

    private readonly Button _focusPick = new() { Name = "RenderFocusPick", Text = "Focus", ToggleMode = true };

    private readonly Button _settingsToggle = new()
    {
        Name = "RenderSettingsToggle",
        Text = "Settings",
        ToggleMode = true,
        TooltipText = "Show or hide the settings beside the picture."
    };

    private readonly RenderProgressBar _bar;
    private readonly MotionTiming _timing;

    private Control? _settings;
    private readonly TextureRect _grip = new() { Name = "RenderResize" };

    private Vector2 _cardSize = new(RenderLimits.DefaultCardWidth, RenderLimits.DefaultCardHeight);
    private Vector2 _grabbedSize;
    private Vector2 _grabbedAt;
    private bool _resizing;

    private readonly PanelContainer _card;
    private readonly Smoothed _presence;

    private bool _filling;

    public RenderView(MotionTiming timing)
    {
        // Snapped rather than left unaimed: the first target a Smoothed is ever
        // given snaps, so without this the card's first opening would arrive
        // outright and only the second would ease.
        _presence = new Smoothed(MotionSpeed.Opacity, timing);
        _presence.SnapTo(0f);

        Name = "Render";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);

        // One clipped line rather than a wrapping block: a note that grew by a line
        // would make the card taller, and a card that changes height while a render
        // runs is the shiver this exists to stop.
        //
        // Its width is measured from its own words as it is given them (Say), which
        // happens when something has happened rather than on every refresh — so it
        // is readable whole, and still cannot move while a render runs. What it
        // says is the short half; the whole sentence is 'render-status'.
        _message.ClipText = true;
        _message.Modulate = new Color(1f, 1f, 1f, RecededAlpha);

        // The card is as big as the larger of what it was dragged to and what its
        // contents need, so anything inside it that grows with its own text drags
        // the card with it — which is a window that changes size while a render
        // runs, reported as a shiver. The progress line's text changes on every
        // refresh, so it asks for no width at all and takes whatever the row has
        // left over instead, clipping when that is not enough.
        _progress.ClipText = true;
        _progress.CustomMinimumSize = new Vector2(0f, 0f);
        _progress.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _progress.Modulate = new Color(1f, 1f, 1f, RecededAlpha);

        _zoom.ClipText = true;
        _zoom.CustomMinimumSize = new Vector2(ZoomWidth, 0f);
        _zoom.Modulate = new Color(1f, 1f, 1f, RecededAlpha);

        _timing = timing;
        _bar = new RenderProgressBar(timing);

        // The bounds and the steps are RenderDialRanges', which is where a test can
        // reach them: written down here, three of them stopped short of what the
        // renderer accepts, and two had lattices that could not express their own
        // defaults — an exposure of 1 arrived as 1.01 and was stored that way.
        Dial(_width, RenderDialRanges.Width);
        Dial(_height, RenderDialRanges.Height);
        Dial(_samples, RenderDialRanges.Samples);
        Dial(_exposure, RenderDialRanges.Exposure);
        Dial(_sunElevation, RenderDialRanges.SunElevation);
        Dial(_sunAzimuth, RenderDialRanges.SunAzimuth);
        Dial(_sunSize, RenderDialRanges.SunSize);
        Dial(_glare, RenderDialRanges.Glare);

        BuildChoices();

        _ground.Toggled += _ => OnDialMoved();

        // Both, and the horizontal one is what makes the picture the wide thing: a
        // row gives a child its minimum unless told otherwise, and a canvas that
        // draws rather than holding a texture has a minimum of nothing — so
        // without this the picture is exactly nought pixels wide and what shows
        // through is the glass.
        _canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _canvas.SizeFlagsVertical = SizeFlags.ExpandFill;
        _canvas.ViewChanged += OnViewChanged;
        _canvas.PointPicked += (x, y) => FocusPicked?.Invoke(x, y);

        _focusPick.Toggled += SetPickingFocus;

        _settingsToggle.Toggled += open =>
        {
            ShowSettings(open);
            DialsFolded?.Invoke(open);
        };

        _card = new PanelContainer
        {
            Name = "Card",
            ThemeTypeVariation = "GlassPanel",
            MouseFilter = MouseFilterEnum.Stop
        };

        _card.AddChild(new GlassBackdrop());
        _card.AddChild(Ui.Margin(BuildContent(), 10).Named("CardBody"));
        _card.AddChild(BuildGripLayer());

        // Centred by a container rather than by anchors: a control anchored to the
        // middle grows down and right from it, which puts the card's own corner in
        // the middle of the window.
        var centre = new CenterContainer { Name = "Centre" };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        centre.MouseFilter = MouseFilterEnum.Ignore;
        centre.AddChild(_card);

        AddChild(centre);
    }

    public event Action? CloseRequested;

    public event Action? DialMoved;

    public event Action? RenderAgainRequested;

    public event Action? SaveRequested;

    public event Action<Vector2>? SizeChanged;

    public event Action<int, int>? FocusPicked;

    public event Action<bool>? DialsFolded;

    /// <summary>
    /// Asked of where the card is heading, not of whether it is drawn: one still
    /// fading out is visible and is not open.
    /// </summary>
    public bool IsOpen => _presence.Target > 0f;

    public RenderDials Dials => new(
        Width: (int)_width.Value,
        Height: (int)_height.Value,
        Samples: (int)_samples.Value,
        Exposure: (float)_exposure.Value,
        SunElevationDegrees: (float)_sunElevation.Value,
        SunAzimuthDegrees: (float)_sunAzimuth.Value,
        SunSizeDegrees: (float)_sunSize.Value,
        Glare: (float)_glare.Value,
        Backdrop: _backdrop,
        Ground: _ground.ButtonPressed);

    /// <summary>
    /// Which part of the picture is on screen. Empty while it is fitted, which is
    /// how "all of it, equally" is said.
    /// </summary>
    public RenderRegion PriorityRegion => _canvas.VisibleRegion;

    public void Open(RenderDials dials, Vector2 size, bool dialsOpen)
    {
        if (IsOpen)
            return;

        Fill(dials);
        Resize(size);

        ShowSettings(dialsOpen);

        // A card that opens is a new picture, so it is shown whole — where "Render
        // again" keeps whatever the user had zoomed into. Fitting now is what
        // clears the aim rather than what computes the view: the canvas has not
        // been laid out at its new size yet, and an unaimed picture re-fits itself
        // on the resize that follows.
        _canvas.Fit();

        _progress.Text = string.Empty;
        _message.Text = string.Empty;

        _bar.SetFraction(0f);

        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;

        _presence.Retarget(1f);
        SetProcess(true);
    }

    public void ShowDials(RenderDials dials) => Fill(dials);

    public void Close()
    {
        if (!Visible || !IsOpen)
            return;

        // The refusal rather than the paint, so it happens now: a card at nought
        // alpha that still took the mouse would eat clicks meant for the editor
        // behind it.
        MouseFilter = MouseFilterEnum.Ignore;
        _resizing = false;

        _presence.Retarget(0f);
        SetProcess(true);
    }

    /// <summary>
    /// Escape, and a press past the card. A resize in flight answers first and is
    /// put back where it started — the gesture is what Escape is nearest to, and
    /// closing the card out from under a drag would lose both.
    /// </summary>
    public bool TryDismiss()
    {
        if (!IsOpen)
            return false;

        if (_resizing)
        {
            _resizing = false;

            Resize(_grabbedSize);

            return true;
        }

        CloseRequested?.Invoke();

        return true;
    }

    public void ShowImage(Texture2D image) => _canvas.SetImage(image);

    public void ShowSize(int width, int height) => _canvas.SetImageSize(width, height);

    public void ShowProgress(RenderProgress progress)
    {
        _progress.Text = RenderProgressLine.Describe(progress);
        _bar.SetFraction(progress.Fraction);
    }

    public void FitImage() => _canvas.Fit();

    public void ShowActualSize() => _canvas.ShowActualSize();

    public void ShowRegion(RenderRegion region) => _canvas.ShowRegion(region);

    /// <summary>
    /// Shows or hides the column. A picture nobody has zoomed into keeps filling
    /// whatever room it now has, deferred because the containers have not
    /// re-sorted on the frame the toggle is pressed.
    /// </summary>
    public void ShowSettings(bool shown)
    {
        if (_settings is not null)
            _settings.Visible = shown;

        if (_settingsToggle.ButtonPressed != shown)
            _settingsToggle.SetPressedNoSignal(shown);

        CallDeferred(MethodName.KeepPictureFitted);
    }

    public bool IsPickingFocus => _canvas.IsPicking;

    /// <summary>
    /// Arms, or disarms, the next click. The button follows whatever was asked,
    /// so an agent turning it on and a hand pressing it leave the card saying the
    /// same thing.
    /// </summary>
    public void SetPickingFocus(bool picking)
    {
        _canvas.SetPicking(picking);

        if (_focusPick.ButtonPressed != picking)
            _focusPick.SetPressedNoSignal(picking);
    }

    public void ShowMessage(string message)
    {
        _message.Text = message;

        Say();
    }

    /// <summary>
    /// Gives the note room for exactly what it says, up to the ceiling. Measured in
    /// the font it will be drawn in rather than picked by eye: a width chosen by
    /// looking was one character short of "The model has changed", and what the
    /// card then showed was "The model has change".
    /// </summary>
    private void Say()
    {
        if (_message.Text.Length == 0)
        {
            _message.CustomMinimumSize = new Vector2(0f, 0f);

            return;
        }

        Font font = _message.GetThemeFont("font");
        int fontSize = _message.GetThemeFontSize("font_size");

        float width = font
            .GetStringSize(_message.Text, HorizontalAlignment.Left, -1f, fontSize)
            .X;

        _message.CustomMinimumSize = new Vector2(MathF.Min(width, MessageCeiling), 0f);
    }

    /// <summary>
    /// The theme is only reachable once the node is in the tree, which is where a
    /// glyph and a colour can first be asked for.
    /// </summary>
    public override void _Ready()
    {
        Dress();

        // Once in the tree there is a font, so what the dials need can be asked for
        // the first time — the change they raise on the way in fires before that.
        FitSettingsColumn();
    }

    public override void _Process(double delta)
    {
        _presence.Tick((float)delta);

        float presence = _presence.Value;

        Modulate = new Color(1f, 1f, 1f, presence);

        // Written every frame, or the card scales about a point beside itself the
        // first time the window is resized.
        _card.PivotOffset = _card.Size * 0.5f;
        _card.Scale = Vector2.One * Mathf.Lerp(ArrivalScale, 1f, presence);

        if (!_presence.IsSettled)
            return;

        if (presence <= 0f)
            Visible = false;

        SetProcess(false);
    }

    /// <summary>
    /// The window changed shape. A card larger than the window it is centred in
    /// hangs off both sides at once, so it is brought back inside — and what was
    /// dragged is left alone, so widening the window again gives the size back.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationResized && Visible)
            Apply(_cardSize);

        // A colour read once is a colour a repaint cannot reach — the standing
        // rule for anything in this chrome that holds one rather than looking it
        // up as it draws.
        if (what == NotificationThemeChanged)
            Dress();
    }

    private void Dress()
    {
        _grip.Texture = GetThemeIcon(
            EditorIconCatalog.ResizeDiagonal, EditorThemeFactory.SemanticType);

        _grip.Modulate = GetThemeColor("icon_receded", EditorThemeFactory.SemanticType);

        // A width measured in one font is a width in that font alone, so the note
        // and the column are both measured again whenever the theme is — the
        // standing rule for anything here that holds a value rather than looking it
        // up as it draws.
        Say();
        FitSettingsColumn();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } button || !IsOpen)
            return;

        // A wheel notch is not a press, and reading it as one closed the card and
        // then zoomed the scene behind it — a scroll that nothing inside the card
        // answered (a settings column already at its top) arrives here exactly like
        // a click on the glass. It is swallowed instead: the card is over the
        // editor, so nothing behind it may answer a gesture aimed at the card.
        if (MouseWheel.IsNotch(button.ButtonIndex))
        {
            AcceptEvent();

            return;
        }

        // A press on the glass behind the card puts it away, the way a press on
        // the scene puts a temporary panel away.
        CloseRequested?.Invoke();
        AcceptEvent();
    }

    private Control BuildContent()
    {
        _settings = BuildSettingsColumn();

        HBoxContainer picture = Ui.HBox(_canvas, _settings);
        picture.AddThemeConstantOverride("separation", 8);
        picture.SizeFlagsVertical = SizeFlags.ExpandFill;

        VBoxContainer content = Ui.VBox(
            BuildHeader(),
            picture.Named("RenderBody"),
            BuildStatus());

        content.AddThemeConstantOverride("separation", 8);

        return content.Named("RenderContent");
    }

    /// <summary>
    /// The dials, in a column beside the picture rather than a band under it. A
    /// render window is a picture with its settings to one side — the picture is
    /// the wide thing, and a row of fields across the bottom takes height from the
    /// one part of the card anybody is looking at.
    /// </summary>
    /// <remarks>
    /// Fixed width, and that is what keeps the card still: the card is as big as
    /// the larger of what it was dragged to and what its contents need, so anything
    /// inside it that grows with its own text drags the whole card with it.
    /// </remarks>
    private Control BuildSettingsColumn()
    {
        VBoxContainer column = Ui.VBox(
            Stacked("Width", _width),
            Stacked("Height", _height),
            Stacked("Samples", _samples),
            Stacked("Exposure", _exposure),
            Choice("Backdrop", _backdropRow),
            Stacked("Sun up", _sunElevation),
            Stacked("Sun round", _sunAzimuth),
            Stacked("Sun size", _sunSize),
            Stacked("Glare", _glare),
            _ground);

        column.AddThemeConstantOverride("separation", 8);
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var scroll = new ScrollContainer
        {
            Name = "RenderSettingsScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };

        scroll.AddChild(column.Named("RenderDials"));

        // The panels' own soft cut, for the same reason they have it: a column
        // taller than the card is cut somewhere, and a hard line through the
        // middle of a number reads as a mistake. It is a plain Control rather
        // than a container, so what it holds is anchored rather than sorted.
        _settingsFade.AddChild(scroll);
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);

        _dials = column;
        _settingsScroll = scroll;

        // Driven by the bar rather than by a frame tick: the strength only ever
        // changes when the body moves or the card is resized, and this card stops
        // processing the moment it has finished arriving.
        VScrollBar bar = scroll.GetVScrollBar();
        bar.ValueChanged += _ => ShowScrollCut();
        scroll.Resized += ShowScrollCut;

        _settingsScrollBar = bar;

        // A fill and no border, the rule the transform blocks already keep: the
        // card around it has a frame, and a frame inside a frame is two lines
        // saying what one fill says better. Translucent, on the panels' own terms —
        // anything inside a pane tints the blur that has already been read rather
        // than taking another.
        var panel = new PanelContainer
        {
            Name = "RenderSettings",
            ThemeTypeVariation = "FieldGroup",

            SizeFlagsHorizontal = SizeFlags.ShrinkEnd
        };

        panel.AddChild(_settingsFade);

        _settingsPanel = panel;

        // What the dials actually need, measured; a plain Control does not carry
        // its child's minimum up, and the widest thing in here — a track reading
        // "Perspective | Parallel" — is wider than any number picked by eye. Left
        // at one, the column cut its own fields off at the frame.
        column.MinimumSizeChanged += FitSettingsColumn;

        return panel;
    }

    /// <summary>
    /// Gives the column the width its widest dial asks for, never less than the
    /// floor. Measured rather than written down: the answer is a font's, so it
    /// moves with the theme, and the same guess-instead-of-measure is what left
    /// the card's own note reading "The model has change".
    /// </summary>
    private void FitSettingsColumn()
    {
        if (_dials is null || _settingsScroll is null || _settingsPanel is null)
            return;

        float wanted = _dials.GetCombinedMinimumSize().X
            + _settingsScroll.GetVScrollBar().GetCombinedMinimumSize().X;

        if (_settingsPanel.HasThemeStylebox("panel"))
        {
            StyleBox box = _settingsPanel.GetThemeStylebox("panel");

            wanted += box.GetMargin(Side.Left) + box.GetMargin(Side.Right);
        }

        _settingsPanel.CustomMinimumSize =
            new Vector2(MathF.Max(SettingsWidth, wanted), 0f);
    }

    /// <summary>
    /// How far the column has run past each of its own edges, which is what the
    /// dissolve there is drawn at. Both numbers come off the one bar, or an
    /// estimate stands in for the second and an edge with nothing past it fades.
    /// </summary>
    private void ShowScrollCut()
    {
        if (_settingsScrollBar is null)
            return;

        _settingsFade.SetStrength(ScrollFadeEdges.Solve(
            new ScrollExtent(
                (float)_settingsScrollBar.Value,
                (float)Mathf.Max(0d, _settingsScrollBar.MaxValue - _settingsScrollBar.Page))));
    }

    /// <summary>
    /// The foot of the card: how far along, then what to do with the render. The
    /// bar spans the whole width above them, so the one thing that moves while a
    /// render runs is not competing with the words for a corner.
    /// </summary>
    /// <remarks>
    /// The row ends in a gap the width of the corner grip. Without it the last
    /// button sits under the grip, and a press meant for one lands on the other —
    /// which is what the card did until somebody tried to resize it.
    /// </remarks>
    private Control BuildStatus()
    {
        HBoxContainer row = Ui.HBox(
            _progress,
            _message,
            Ui.Button("Render again", () => RenderAgainRequested?.Invoke()).Named("RenderAgain"),
            Ui.Button("Save…", () => SaveRequested?.Invoke()).Named("RenderSave"),
            Ui.Button("Close", () => CloseRequested?.Invoke()).Named("RenderClose"),
            new Control
            {
                CustomMinimumSize = new Vector2(GripSide + GripInset, 0f),
                MouseFilter = MouseFilterEnum.Ignore
            });

        row.AddThemeConstantOverride("separation", 8);

        _progress.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _message.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        VBoxContainer status = Ui.VBox(_bar, row.Named("RenderButtons"));
        status.AddThemeConstantOverride("separation", 8);

        return status.Named("RenderStatus");
    }

    /// <summary>
    /// A fold changes how much room the picture has, and a picture nobody has
    /// zoomed into should go on filling it. Deferred, because the containers have
    /// not re-sorted on the frame the fold is asked for.
    /// </summary>
    private void KeepPictureFitted()
    {
        if (_canvas.IsFitted)
            _canvas.Fit();
    }

    /// <summary>
    /// The title, and the two things that decide how the picture is being looked
    /// at. They belong over the picture rather than beside Save and Close: those
    /// two are what to do with the render, these two are what to do with the view
    /// of it.
    /// </summary>
    private Control BuildHeader()
    {
        var title = Ui.Label("Render");
        title.AddThemeFontSizeOverride("font_size", 16);

        HBoxContainer row = Ui.HBox(
            title.Named("RenderTitle"),
            new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore },
            _zoom,
            _settingsToggle,
            _focusPick,
            Ui.Button("Fit", FitImage).Named("RenderFit"),
            Ui.Button("1:1", ShowActualSize).Named("RenderActual"));

        row.AddThemeConstantOverride("separation", 8);
        _zoom.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        return row.Named("RenderHeader");
    }

    /// <summary>
    /// The corner grip, over everything else in the card. A layer of its own
    /// because a <see cref="PanelContainer"/> fits every child to its whole rect —
    /// so the way to put something in one corner is a transparent child that takes
    /// the rect and holds the thing by its anchors.
    /// </summary>
    private Control BuildGripLayer()
    {
        var layer = new Control
        {
            Name = "RenderGripLayer",

            // A wrapper that stopped the mouse would eat every press meant for
            // what is underneath it — the trap the chrome already paid for once.
            MouseFilter = MouseFilterEnum.Ignore
        };

        _grip.MouseFilter = MouseFilterEnum.Stop;
        _grip.StretchMode = TextureRect.StretchModeEnum.KeepCentered;
        _grip.MouseDefaultCursorShape = CursorShape.Fdiagsize;
        _grip.TooltipText = "Drag to resize the card. The render's own size is set above.";
        _grip.CustomMinimumSize = new Vector2(GripSide, GripSide);
        _grip.SetAnchorsPreset(LayoutPreset.BottomRight);
        _grip.OffsetLeft = -(GripSide + GripInset);
        _grip.OffsetTop = -(GripSide + GripInset);
        _grip.OffsetRight = -GripInset;
        _grip.OffsetBottom = -GripInset;
        _grip.GuiInput += OnGripInput;

        layer.AddChild(_grip);

        return layer;
    }

    /// <summary>
    /// One question with a handful of answers, dressed the way the settings form
    /// dresses the same shape — a track with the chosen answer lit inside it.
    /// </summary>
    private void BuildChoices()
    {
        _backdropsList.AddRange(Enum.GetValues<RenderBackdrop>());

        _backdropRow.SetSegments(_backdropsList
            .Select(backdrop => new SegmentSpec(
                $"Backdrop{backdrop}",
                backdrop switch
                {
                    RenderBackdrop.Environment => "Sky",
                    RenderBackdrop.Plain => "Plain",
                    _ => "None"
                },
                backdrop switch
                {
                    RenderBackdrop.Environment => "The sky the scene is lit by.",
                    RenderBackdrop.Plain => "One flat colour behind everything.",
                    _ => "Nothing: the picture carries an alpha channel."
                }))
            .ToList());

        _backdropRow.Chosen += index =>
        {
            _backdrop = _backdropsList[index];
            OnDialMoved();
        };

        SetBackdrop(_backdrop);
    }

    private void SetBackdrop(RenderBackdrop backdrop)
    {
        _backdrop = Enum.IsDefined(backdrop) ? backdrop : RenderBackdrop.Environment;
        _backdropRow.Select(_backdropsList.IndexOf(_backdrop));
    }

    private static Control Choice(string caption, SegmentedRow row)
    {
        Label label = Ui.Label(caption);
        label.Modulate = new Color(1f, 1f, 1f, RecededAlpha);

        // Filled rather than hugging its segments: in a column of full-width boxes
        // a track that hugged sat against the right edge at whatever width its
        // words happened to need, so two questions one under the other were two
        // different shapes hanging off the same edge.
        row.Fill();

        VBoxContainer field = Ui.VBox(label, row);
        field.AddThemeConstantOverride("separation", 2);

        return field;
    }

    /// <summary>
    /// A caption over its field, filling the column's width. The row form the card
    /// used to have put the fields side by side, which is what a band under a
    /// picture wants and not what a column does.
    /// </summary>
    private static Control Stacked(string caption, SpinBox box)
    {
        Label label = Ui.Label(caption);
        label.Modulate = new Color(1f, 1f, 1f, RecededAlpha);

        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.CustomMinimumSize = new Vector2(0f, 0f);

        VBoxContainer field = Ui.VBox(label, box);
        field.AddThemeConstantOverride("separation", 2);

        return field;
    }

    private static Control Field(string caption, SpinBox box)
    {
        Label label = Ui.Label(caption);
        label.Modulate = new Color(1f, 1f, 1f, RecededAlpha);

        VBoxContainer field = Ui.VBox(label, box);
        field.AddThemeConstantOverride("separation", 2);

        return field;
    }

    /// <summary>
    /// A dial restarts the render when it is <em>committed</em> — Godot takes a
    /// spin box's text on Enter or on the field losing focus, never as the digits
    /// arrive, which is what keeps typing 128 from starting a render at 1, then at
    /// 12, then at 128.
    /// </summary>
    private void Dial(SpinBox box, RenderDialRange range)
    {
        box.MinValue = range.Minimum;
        box.MaxValue = range.Maximum;
        box.Step = range.Step;
        box.UpdateOnTextChanged = false;
        box.MinSize(FieldWidth, 0f);
        box.ValueChanged += _ => OnDialMoved();
    }

    /// <summary>
    /// The grip's drag. Twice the pull, because the card is centred: the corner
    /// then follows the pointer instead of trailing it by half — dragging it out
    /// grows the card away from its middle in both directions.
    /// </summary>
    private void OnGripInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                _resizing = true;
                _grabbedAt = _grip.GetGlobalTransformWithCanvas() * press.Position;
                _grabbedSize = _cardSize;

                _grip.AcceptEvent();

                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (_resizing)
                {
                    _resizing = false;

                    SizeChanged?.Invoke(_cardSize);
                }

                _grip.AcceptEvent();

                break;

            case InputEventMouseMotion motion when _resizing:
                Vector2 at = _grip.GetGlobalTransformWithCanvas() * motion.Position;

                Resize(_grabbedSize + (at - _grabbedAt) * 2f);

                _grip.AcceptEvent();

                break;
        }
    }

    /// <summary>
    /// A size the card may be: no smaller than its own minimum, and never larger
    /// than the window holding it. The bounds are the preferences' own, so a
    /// stored file and a drag are held to the same pair.
    /// </summary>
    private void Resize(Vector2 size)
    {
        _cardSize = new Vector2(
            Mathf.Clamp(size.X, RenderLimits.MinimumCardWidth, RenderLimits.MaximumCardSide),
            Mathf.Clamp(size.Y, RenderLimits.MinimumCardHeight, RenderLimits.MaximumCardSide));

        Apply(_cardSize);
    }

    /// <summary>
    /// What the card is actually given: the wanted size, cut down to the window.
    /// Kept apart from what was wanted, so a small window does not overwrite a
    /// size somebody chose.
    /// </summary>
    private void Apply(Vector2 wanted)
    {
        Vector2 window = GetViewportRect().Size;

        _card.CustomMinimumSize = new Vector2(
            Mathf.Max(Mathf.Min(wanted.X, window.X - 2f * CardMargin), RenderLimits.MinimumCardWidth),
            Mathf.Max(Mathf.Min(wanted.Y, window.Y - 2f * CardMargin), RenderLimits.MinimumCardHeight));
    }

    private void OnViewChanged()
    {
        int percent = (int)Mathf.Round(_canvas.Zoom * 100f);

        _zoom.Text = $"{percent}%";
    }

    private void OnDialMoved()
    {
        if (_filling)
            return;

        DialMoved?.Invoke();
    }

    /// <summary>
    /// Whether focusing means anything: with a pinhole the whole picture is sharp,
    /// so the button is refused rather than hidden — and it says why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pushed rather than read off a dial, because the blur that decides it is the
    /// <em>camera's</em> now and this card holds no field for it. The presenter
    /// asks the camera and tells the button, which is the same seam every other
    /// fact about the document crosses.
    /// </para>
    /// <para>
    /// Greyed rather than taken away, the rule the settings form's pace row already
    /// keeps: a control that vanishes takes the sentence explaining it with it, and
    /// here it would also move the three buttons beside it every time the blur
    /// crossed nought. Being disabled is what refuses the press as well as what
    /// draws it, so the look and the rule cannot come apart.
    /// </para>
    /// </remarks>
    public void SetFocusAvailable(bool available)
    {
        _focusPick.Disabled = !available;

        _focusPick.TooltipText = available
            ? "Click a point in the picture to focus the lens on it."
            : "The camera's Blur is nought: with none, the whole picture is sharp "
                + "and there is nothing to focus. Turn it up in the inspector.";

        // A pick armed and then made pointless is disarmed, or the picture would
        // keep its crosshair over a lens that no longer has a focus to move.
        if (!available && _focusPick.ButtonPressed)
            SetPickingFocus(false);
    }

    private void Fill(RenderDials dials)
    {
        _filling = true;

        try
        {
            _width.Value = dials.Width;
            _height.Value = dials.Height;
            _samples.Value = dials.Samples;
            _exposure.Value = dials.Exposure;
            _sunElevation.Value = dials.SunElevationDegrees;
            _sunAzimuth.Value = dials.SunAzimuthDegrees;
            _sunSize.Value = dials.SunSizeDegrees;
            _glare.Value = dials.Glare;
            SetBackdrop(dials.Backdrop);
            _ground.ButtonPressed = dials.Ground;
        }
        finally
        {
            _filling = false;
        }
    }
}
