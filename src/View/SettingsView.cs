using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.CommonTypes;
using VoxelCAD.Localization;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The application settings, as a glass card in the shell rather than a window:
/// a rail of sections with a search over all of them, the form beside it, and
/// one button under both, which only closes it.
/// </summary>
/// <remarks>
/// <b>Every control applies as it is set.</b> There is no Save, so there is no
/// state of the form that is not also the state of the editor, and no Cancel,
/// because there is nothing left to cancel — the escape hatch for a setting is
/// setting it back, which is possible precisely because every value is on
/// screen. What that costs is a colour dragged far from where it started; what
/// it buys is the display page being usable at all, since a theme and an accent
/// are chosen by looking at them and a transaction puts the card in the way.
/// <para>
/// The judgement is <em>when</em> a control has been set, and it is not the same
/// answer for all of them — see <see cref="RaiseEdited"/>.
/// </para>
/// <para>
/// <b>It arrives rather than appearing.</b> A surface that covers the window and
/// was asked for deliberately is the one this is worth doing for: nothing is
/// aimed at it the instant it is summoned, so a twelfth of a second costs nobody
/// a click. The context menu keeps its instant appearance for the opposite
/// reason — a menu <em>is</em> a target, and a target that moves under a pointer
/// already on its way is worse than an abrupt one.
/// </para>
/// <para>
/// Two rules make that correct rather than merely pretty, and both are the
/// canon's. It goes through <see cref="MotionTiming"/>, unlike
/// <see cref="ThemeVeil"/>, which is exempt because it covers a repaint: this is
/// a transition <em>of</em> the interface, so switching animation off must land
/// it at once. And <b>paint eases, refusal does not</b> — the mouse is let
/// through and the interaction token released on the <em>request</em> to close,
/// not when the fade finishes, or a handover would wait on a picture and a click
/// meant for the editor would be eaten by a card at nought alpha.
/// </para>
/// Not an <see cref="AcceptDialog"/> any more, for the reasons the context menu
/// is not a <see cref="PopupMenu"/>: a <see cref="Window"/> paints its own panel
/// outside any child's reach, so it cannot be glass, and its contents are in
/// another window's coordinate space. What it was instead was a form taller than
/// any window this editor runs in, with Save and Cancel below the screen and no
/// visible way out — measured with <c>ui</c>, which is also how this one is
/// checked.
/// <para>
/// It holds an interaction token while it is up, the same one a focused
/// inspector field holds: seizing control out from under an open question is as
/// rude as seizing it mid-edit.
/// </para>
/// </remarks>
internal sealed partial class SettingsView : Control, ISettingsView
{
    private const float CardWidth = 640f;

    /// <summary>
    /// Tall enough for the longest section and no taller. Fixed rather than
    /// hugging each page, because a card that resized as the rail was clicked
    /// would jump under the hand — and the search can filter a page down to one
    /// row, which would collapse it outright.
    /// </summary>
    /// <remarks>
    /// It was 520, which left the tallest page 175 units of empty glass under
    /// it and the shortest nearly 280 — a card mostly made of nothing, over a
    /// scene. Measured with the form open: the rail and the button row take 62
    /// between them, and the pages come out at 203, 283, ~180 and ~100. So the
    /// number is the tallest of those plus the chrome, with one row's slack.
    /// Under-sizing is the safe direction if a page ever grows: the body is in a
    /// <see cref="ScrollContainer"/>, which is what it is there for.
    /// </remarks>
    private const float CardHeight = 356f;
    private const float RailWidth = 168f;
    private const float LabelWidth = 150f;

    /// <summary>
    /// What a row that is not in effect is dimmed to — the strength the pin and
    /// the tree's own receded rows already read at, so a settings row switched
    /// off looks like every other thing in this editor that is switched off.
    /// </summary>
    private const float RecededAlpha = 0.45f;

    private readonly CheckBox _mcpEnabled = new() { Text = "Run the agent endpoint" };
    private readonly SpinBox _port = new();
    private readonly LineEdit _token = new();
    private readonly LineEdit _clientConfigPath = new();
    private readonly CheckBox _countdownEnabled = new() { Text = "Count down before handing over" };
    private readonly SpinBox _countdownSeconds = new();
    private readonly SpinBox _maximumWait = new();
    private readonly SpinBox _silenceWarning = new();
    private readonly SpinBox _silenceGrace = new();

    private readonly CheckBox _inputInjection = new()
    {
        Text = "Let an agent drive the interface (debug)"
    };

    private readonly Label _endpoint = new();
    private readonly Label _status = new();

    private readonly CheckBox _autosaveEnabled = new()
    {
        Name = "AutosaveEnabled",
        Text = "Keep a recovery snapshot while working",
        TooltipText = "A copy of the open document beside the editor's own settings, "
            + "never in your file. Offered back if the editor stops before you save."
    };

    private readonly SpinBox _autosaveInterval = new() { Name = "AutosaveInterval" };
    private readonly LineEdit _autosaveFolder = new() { Name = "AutosaveFolder" };
    private readonly Label _autosaveStatus = new() { Name = "AutosaveStatus" };

    /// <summary>
    /// The interval row and the paragraph under it, held so they can be greyed
    /// with the box above them — the pace row's own arrangement, for the same
    /// reason: a live dial for a value that does nothing is a question the form
    /// should not be asking.
    /// </summary>
    private Control? _autosaveIntervalField;
    private Control? _autosaveNote;
    private readonly LineEdit _search = new() { PlaceholderText = "Search" };

    private readonly List<Row> _rowsList = new();
    private readonly Dictionary<SettingsSection, Button> _tabsBySectionDictionary = new();
    private readonly VBoxContainer _body = new() { Name = "SettingsBody" };
    private readonly PanelContainer _card;

    private SettingsSection _openSection = SettingsSection.Endpoint;

    /// <summary>
    /// Raised while <see cref="Fill"/> is writing the controls, which is the one
    /// time a control changing means nothing: the values are arriving from the
    /// store, and reporting them straight back would be the form editing itself.
    /// </summary>
    private bool _filling;

    // Filled in, never shown, handed straight back. The page is the endpoint and
    // the handover; the cube's corner and the panel placements are display
    // settings with no field here, and a form with no control for a value must
    // return it unchanged rather than send back whatever the default happens to be.
    private ViewCubeCorner _viewCubeCorner;

    private readonly CheckBox _animationsEnabled = new() { Text = "Animate the interface" };

    private readonly CheckBox _viewportOcclusion = new()
    {
        Text = "Shade the creases",
        TooltipText = "The soft darkening where a model's own cells crowd a corner."
    };

    private readonly CheckBox _viewportShadows = new()
    {
        Text = "Cast shadows",
        TooltipText = "Whether the key light casts. Nothing but voxels ever takes one."
    };

    private readonly CheckBox _viewportFactures = new()
    {
        Text = "Draw the surface relief",
        TooltipText = "A material's bumps and grain. Its colour, gloss and metal stay whatever this says."
    };

    private readonly CheckBox _glassPanels = new()
    {
        Text = "Frost the panels",
        TooltipText = "Each panel reads the screen behind it and blurs it. Off, they are solid."
    };
    private readonly SegmentedRow _speedRow = new();
    private readonly List<AnimationSpeed> _speedsList = new();

    /// <summary>
    /// The pace row and the paragraph under it, held so they can be greyed:
    /// with animation switched off there is no pace, and a live control for a
    /// value that does nothing is a question the form should not be asking.
    /// </summary>
    /// <remarks>
    /// Greyed rather than hidden. A row that vanishes takes the reason it
    /// vanished with it, and the paragraph is where the reason is written down;
    /// a row standing there in grey says "this belongs to the box above".
    /// </remarks>
    private Control? _speedField;
    private Control? _speedNote;

    private readonly SegmentedRow _themeRow = new();
    private readonly List<EditorTheme> _themesList = new();

    /// <summary>
    /// A button and a menu rather than a <see cref="SegmentedRow"/> like the two
    /// rows beside it, and the reason is that row's own rule: it gives every
    /// segment the width of the widest, so six languages would each be as wide as
    /// "Portugues (Brasil)" and the run would be several times the card. The menu
    /// is the shell's own, so this stays glass and every entry stays a named
    /// control an agent can aim at.
    /// </summary>
    private readonly Button _languageButton = new() { Name = "LanguageButton" };
    private readonly ContextMenuView _languageMenu = new() { Name = "LanguageMenuCard" };
    private readonly ColorPickerButton _accent = new()
    {
        Name = "SettingsAccent",
        ThemeTypeVariation = EditorThemeFactory.OpaqueSwatch
    };

    /// <summary>Wide enough to read as a colour, short enough to read as a swatch.</summary>
    private const float AccentSwatchWidth = 96f;

    /// <summary>
    /// How small the card starts. Two per cent, which is barely a size change at
    /// all and is meant to be: it is there so the card reads as having been put
    /// in front of you rather than printed on the window, and one that grew from
    /// anything smaller would read as a popup out of a game.
    /// </summary>
    private const float ArrivalScale = 0.98f;

    private readonly Smoothed _presence;

    private AnimationSpeed _animationSpeed = AnimationSpeed.Fast;
    private EditorTheme _theme = EditorTheme.Dark;
    private EditorLanguage _language = EditorLanguage.English;
    private IReadOnlyList<PanelPlacement>? _panelPlacements;

    public SettingsView(MotionTiming timing)
    {
        // Snapped rather than left unaimed: the first target a Smoothed is ever
        // given snaps, so without this the card's first opening would arrive
        // outright and only the second would ease.
        _presence = new Smoothed(MotionSpeed.Opacity, timing);
        _presence.SnapTo(0f);

        Name = "Settings";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _port.MinValue = 1024;
        _port.MaxValue = 65535;
        _port.Step = 1;
        _port.MinSize(110f, 0f);

        _countdownSeconds.MinValue = 0.5;
        _countdownSeconds.MaxValue = 60.0;
        _countdownSeconds.Step = 0.5;
        _countdownSeconds.MinSize(110f, 0f);

        // The same ranges the preferences clamp to, so the form cannot ask for
        // something it will then be shown to have not got.
        Seconds(_maximumWait, 1.0, 600.0);
        Seconds(_silenceWarning, 5.0, 3600.0);
        Seconds(_silenceGrace, 5.0, 3600.0);

        // The same bounds the preferences clamp to, and whole seconds: an
        // interval is a timer.
        Seconds(_autosaveInterval, 10.0, 3600.0);
        _autosaveInterval.Suffix = "s";

        // Shown, copied, never typed into.
        _token.Editable = false;
        _token.Expand();
        _clientConfigPath.Expand();
        _autosaveFolder.Editable = false;
        _autosaveFolder.Expand();
        _autosaveStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _search.TextChanged += _ => RebuildBody();

        BuildRows();
        Watch();

        _card = new PanelContainer
        {
            Name = "Card",
            ThemeTypeVariation = "GlassPanel",
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(CardWidth, CardHeight)
        };
        _card.AddChild(new GlassBackdrop());
        _card.AddChild(Ui.Margin(BuildCardContent(), 10).Named("CardBody"));

        // Centred by a container rather than by anchors: a control anchored to
        // the middle grows down and right from it, which put the card's own
        // corner in the middle of the window and its buttons off the bottom.
        var centre = new CenterContainer { Name = "Centre" };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        centre.MouseFilter = MouseFilterEnum.Ignore;
        centre.AddChild(_card);

        AddChild(centre);

        RebuildBody();
    }

    public event Action<PreferenceValues>? Edited;

    public event Action? CloseRequested;

    public event Action? RegenerateTokenRequested;

    /// <summary>
    /// Raised while the form is up, the same pair a focused inspector field
    /// raises. <see cref="ISettingsView"/> does not carry these: they are the
    /// shell's wiring, not the presenter's business.
    /// </summary>
    public event Action? InteractionBegan;

    public event Action? InteractionEnded;

    /// <summary>Whether the card is on its way in rather than on its way out.</summary>
    private bool IsArriving => _presence.Target > 0f;

    public void Fill(PreferenceValues values, string token, string endpoint, string autosaveDirectory)
    {
        _filling = true;

        try
        {
            FillControls(values, token, endpoint, autosaveDirectory);
        }
        finally
        {
            _filling = false;
        }
    }

    private void FillControls(
        PreferenceValues values,
        string token,
        string endpoint,
        string autosaveDirectory)
    {
        _mcpEnabled.ButtonPressed = values.McpEnabled;
        _port.Value = values.McpPort;
        _clientConfigPath.Text = values.ClientConfigPath;
        _countdownEnabled.ButtonPressed = values.CountdownEnabled;
        _countdownSeconds.Value = values.CountdownSeconds;
        _maximumWait.Value = values.MaximumWaitSeconds;
        _silenceWarning.Value = values.SilenceWarningSeconds;
        _silenceGrace.Value = values.SilenceGraceSeconds;
        _inputInjection.ButtonPressed = values.InputInjectionEnabled;
        _viewCubeCorner = values.ViewCubeCorner;
        _animationsEnabled.ButtonPressed = values.AnimationsEnabled;
        _viewportOcclusion.ButtonPressed = values.ViewportOcclusion;
        _viewportShadows.ButtonPressed = values.ViewportShadows;
        _viewportFactures.ButtonPressed = values.ViewportFactures;
        _glassPanels.ButtonPressed = values.GlassPanels;
        _autosaveEnabled.ButtonPressed = values.AutosaveEnabled;
        _autosaveInterval.Value = values.AutosaveIntervalSeconds;
        _autosaveFolder.Text = autosaveDirectory;
        ApplyAutosaveAvailability();

        SetTheme(values.Theme);
        SetLanguage(values.Language);
        _accent.Color = EditorSkin.ReadAccent(values.AccentColor);

        SetSpeed(values.AnimationSpeed);
        ApplySpeedAvailability();

        _panelPlacements = values.PanelPlacements;
        _token.Text = token;
        _endpoint.Text = endpoint;
    }

    public void SetStatus(string message) => _status.Text = message;

    public void SetAutosaveStatus(string message) => _autosaveStatus.Text = message;

    /// <summary>
    /// The glyphs, fetched once the theme is reachable. Built the way the chrome
    /// panels build theirs, and for their reason: this card is assembled in a
    /// constructor, where a node is not in the tree and <c>GetThemeIcon</c>
    /// answers with the engine's fallback rather than ours.
    /// </summary>
    public override void _Ready()
    {
        LoadIcons();

        // Godot turns processing on for a script that overrides _Process, and it
        // does so as the node is set up — after any constructor.
        SetProcess(false);
    }

    public override void _Notification(int what)
    {
        if ((long)what == NotificationThemeChanged && IsInsideTree())
            LoadIcons();
    }

    private void LoadIcons()
    {
        foreach ((SettingsSection section, Button tab) in _tabsBySectionDictionary)
        {
            tab.Icon = GetThemeIcon(
                EditorIconCatalog.ForSettingsSection(section), EditorThemeFactory.SemanticType);
        }

        // On the right, where a search field's magnifier goes when the words in
        // it are the placeholder rather than a label beside it.
        _search.RightIcon = GetThemeIcon(
            EditorIconCatalog.Search, EditorThemeFactory.SemanticType);
    }

    public void Open()
    {
        // Asked of where it is heading, not of whether it is drawn: a card still
        // fading out is visible and is not open.
        if (IsArriving)
            return;

        _search.Text = string.Empty;
        RebuildBody();

        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;

        _presence.Retarget(1f);
        SetProcess(true);

        InteractionBegan?.Invoke();
    }

    public void Close()
    {
        if (!Visible || !IsArriving)
            return;

        // Both of these are the refusal rather than the paint, so both happen
        // now: a card at nought alpha that still took the mouse would eat clicks
        // meant for the editor behind it, and a token held until a fade finished
        // would make an agent's handover wait on a picture.
        MouseFilter = MouseFilterEnum.Ignore;

        _presence.Retarget(0f);
        SetProcess(true);

        InteractionEnded?.Invoke();
    }

    /// <summary>
    /// Moves the card on. One number does the fade and the size, because they
    /// are one sentence about the card: how much of it has arrived.
    /// </summary>
    public override void _Process(double delta)
    {
        _presence.Tick((float)delta);

        float presence = _presence.Value;

        Modulate = new Color(1f, 1f, 1f, presence);

        // Written every frame, or the card scales about a point beside itself
        // the first time the window is resized.
        _card.PivotOffset = _card.Size * 0.5f;
        _card.Scale = Vector2.One * Mathf.Lerp(ArrivalScale, 1f, presence);

        if (!_presence.IsSettled)
            return;

        if (presence <= 0f)
            Visible = false;

        SetProcess(false);
    }

    /// <summary>
    /// A press the card did not claim landed outside it, which cancels — the
    /// same gesture that dismisses the context menu, and the reason this
    /// control stops the mouse across the whole window.
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } button)
            return;

        // A wheel notch is not a press. The form is taller than its card, so a
        // scroll the body could not answer — already at either end of it —
        // arrived here exactly like a click on the glass and put the form away,
        // which made the rows past the fold unreachable by scrolling at all. It
        // is swallowed rather than merely ignored: the card is over the editor,
        // so nothing behind it may answer a gesture aimed at the card.
        if (MouseWheel.IsNotch(button.ButtonIndex))
        {
            AcceptEvent();

            return;
        }

        AcceptEvent();
        RaiseClose();
    }

    /// <summary>
    /// Asks to be dismissed, as Escape does. Not a key handler of its own: the
    /// viewport's global route consumes Escape before any second handler sees
    /// it, which is the class rule the temporary panels already learned — what
    /// is downstream of a consumed key hangs off the consumer's event.
    /// </summary>
    public bool TryDismiss()
    {
        if (!Visible || !IsArriving)
            return false;

        RaiseClose();

        return true;
    }

    /// <summary>A whole-second field: the thresholds are timers, not stopwatches.</summary>
    private static void Seconds(SpinBox field, double minimum, double maximum)
    {
        field.MinValue = minimum;
        field.MaxValue = maximum;
        field.Step = 1.0;
        field.MinSize(110f, 0f);
    }

    private Control BuildCardContent()
    {
        var rail = Ui.VBox(_search).Named("SettingsRail");
        rail.CustomMinimumSize = new Vector2(RailWidth, 0f);

        foreach (SettingsSection section in Enum.GetValues<SettingsSection>())
        {
            SettingsSection captured = section;

            var tab = new Button
            {
                Text = section.ToString(),
                ToggleMode = true,
                Alignment = HorizontalAlignment.Left,
                ThemeTypeVariation = "SettingsTab"
            }.MouseOnly().Named($"Tab{section}");

            tab.Pressed += () =>
            {
                _openSection = captured;
                _search.Text = string.Empty;
                RebuildBody();
            };

            _tabsBySectionDictionary[section] = tab;
            rail.AddChild(tab);
        }

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        }.Named("SettingsScroll").Expand();

        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_body);

        // One button, and it does nothing but put the card away: every control
        // here has already taken effect, so there is no answer for a second
        // button to carry. Kept as a button rather than left to Escape and a
        // press outside, both of which still work — a surface with no visible
        // way out is a surface somebody gets stuck on.
        //
        // Pinned to its own height and to the bottom of the row. A row is as
        // tall as its tallest child, the status wraps to as many lines as it
        // needs, and a Button fills whatever it is given — so the buttons here
        // grew to sixty-six units, more than twice a button's height, and sat
        // there as slabs. Measured with `ui`, not guessed.
        Button done = Ui.Button("Done", RaiseClose).Named("SettingsDone");

        done.SizeFlagsVertical = SizeFlags.ShrinkEnd;

        // And the status keeps its own line: bottom-aligned too, so a message
        // that wraps grows upward from the buttons rather than floating in the
        // middle of a row that got taller around it.
        _status.VerticalAlignment = VerticalAlignment.Bottom;

        var buttons = Ui.HBox(
            _status.Expand(),
            done).Named("SettingsButtons");

        return Ui.VBox(
            Ui.HBox(rail, scroll).Expand().Named("SettingsColumns"),
            new HSeparator(),
            buttons).Named("SettingsStack");
    }

    /// <summary>
    /// Binds every control to <see cref="RaiseEdited"/>, which is the whole of
    /// applying live — and where the one judgement in it is made: <b>when</b> a
    /// control counts as having been set.
    /// </summary>
    /// <remarks>
    /// A checkbox and a segment are settled the moment they are pressed, so they
    /// report at once. The other two kinds are gestures with a middle, and
    /// reporting from inside one is what makes live-apply forms unpleasant
    /// everywhere they are unpleasant: a value part-way through being typed, or a
    /// colour part-way through being dragged, is not something the user has said
    /// yet. So a field reports when it is committed and a colour when its picker
    /// is put away.
    /// <para>
    /// The colour is the one with a cost worth naming: dragging round the wheel
    /// does not recolour the editor behind the popup, because a preference write
    /// is a file write and the endpoint answers each one — sixty a second of both,
    /// one of them to a file this editor does not own. The swatch on the button
    /// previews it meanwhile.
    /// </para>
    /// </remarks>
    private void Watch()
    {
        // Every checkbox on the form, named in one place rather than one line
        // each: a box added to a page and not to this list is one that ticks and
        // reports nothing, which is a control that looks exactly like it works.
        // The animation box and the autosave box are deliberately absent — each
        // is watched where it is built, because its press also greys the row
        // beside it.
        foreach (CheckBox box in new[]
        {
            _mcpEnabled,
            _countdownEnabled,
            _inputInjection,
            _viewportOcclusion,
            _viewportShadows,
            _viewportFactures,
            _glassPanels
        })
            box.Toggled += _ => RaiseEdited();

        foreach (SpinBox field in new[]
        {
            _port,
            _countdownSeconds,
            _maximumWait,
            _silenceWarning,
            _silenceGrace,
            _autosaveInterval
        })
        {
            // Godot takes a typed value on Enter or on the field losing focus,
            // never as the digits arrive. That is what makes a live port safe —
            // typing 8770 over 8765 would otherwise ask the listener for 8, then
            // 87, then 877 — and it is one property away from being the other
            // thing, so it is said out loud rather than relied upon.
            field.UpdateOnTextChanged = false;
            field.ValueChanged += _ => RaiseEdited();
        }

        _clientConfigPath.TextSubmitted += _ => RaiseEdited();
        _clientConfigPath.FocusExited += RaiseEdited;

        _accent.PopupClosed += RaiseEdited;
    }

    /// <summary>
    /// The form, as rows that know which section they belong to and what words
    /// a search should find them by. Building them once and choosing between
    /// them is what lets a query reach across sections.
    /// </summary>
    private void BuildRows()
    {
        Add(SettingsSection.Endpoint, "run agent endpoint", _mcpEnabled);
        Add(SettingsSection.Endpoint, "port", Field("Port", _port.Expand()));
        Add(SettingsSection.Endpoint, "endpoint url address", Field("Endpoint", _endpoint.Expand()));
        Add(
            SettingsSection.Endpoint,
            "token secret bearer",
            Field(
                "Token",
                _token,
                Ui.Button("New token", () => RegenerateTokenRequested?.Invoke())
                    .Named("RegenerateToken")));
        Add(SettingsSection.Endpoint, "client config mcp.json path", Field("Client config", _clientConfigPath));
        Add(
            SettingsSection.Endpoint,
            "client config note",
            Note("The client config is written when the endpoint starts. Point an agent at "
                + "that file rather than copying the token."));

        Add(SettingsSection.Handover, "count down countdown", _countdownEnabled);
        Add(SettingsSection.Handover, "countdown seconds", Field("Seconds", _countdownSeconds.Expand()));
        Add(SettingsSection.Handover, "wait busy user maximum", Field("Wait on a busy user", _maximumWait.Expand()));
        Add(
            SettingsSection.Handover,
            "wait note",
            Note("Past that the countdown runs anyway, so a field left focused cannot "
                + "hold an agent off for good."));
        Add(SettingsSection.Handover, "silence warning say so after", Field("Say so after", _silenceWarning.Expand()));
        Add(SettingsSection.Handover, "silence grace take over after", Field("Take over after", _silenceGrace.Expand()));
        Add(
            SettingsSection.Handover,
            "silence note",
            Note("Counted from the last command, not from when the agent started: long "
                + "work keeps control, an agent that has stopped talking does not."));

        Add(
            SettingsSection.Display,
            "language localization localisation translation words",
            Field("Language", BuildLanguageRow()));
        Add(
            SettingsSection.Display,
            "language note translation",
            Note("Takes effect when the editor is restarted. Each language is listed in "
                + "itself; anything not translated yet shows in English, and numbers keep "
                + "the formatting they have here whichever language is chosen."));

        Add(SettingsSection.Display, "theme dark light appearance colours", Field("Theme", BuildThemeRow()));
        Add(SettingsSection.Display, "accent colour color highlight selection", Field("Accent", BuildAccentField()));
        Add(
            SettingsSection.Display,
            "theme note accent",
            Note("The accent is what the editor picks things out in — the selected row, "
                + "the pressed tool, the focus ring, the box round what is selected in "
                + "the scene. It applies to both themes."));

        Add(SettingsSection.Display, "animation animate motion transitions", _animationsEnabled);

        _speedField = Field("Speed", BuildSpeedRow());
        _speedNote = Note("Fast is what the editor ships with. The slower settings stretch "
            + "every transition by the same factor, so the chrome moves at a different pace "
            + "and behaves in exactly the same way. With animations off nothing eases "
            + "and nothing waits: panels, tabs and the navigation cube arrive where "
            + "they belong at once, and a tab opens the moment the pointer is over it "
            + "rather than after a pause.");

        Add(SettingsSection.Display, "animation speed pace fast medium slow", _speedField);
        Add(SettingsSection.Display, "animation note pace", _speedNote);

        Add(SettingsSection.Display, "occlusion shading creases corners ambient", _viewportOcclusion);
        Add(SettingsSection.Display, "shadows shadow light cast", _viewportShadows);
        Add(SettingsSection.Display, "facture relief texture bump normal surface", _viewportFactures);
        Add(
            SettingsSection.Display,
            "viewport note performance effects",
            Note("What the scene is drawn with. Each of these costs something and none "
                + "of them is what a material is: switched off, a mirror is still a "
                + "mirror and glass is still glass, they are simply drawn plainly. "
                + "Turn them off on a large model or a slow machine."));

        Add(SettingsSection.Display, "glass panels blur frost chrome transparency", _glassPanels);
        Add(
            SettingsSection.Display,
            "glass note performance panels",
            Note("A frosted panel reads the screen behind it once per panel and blurs "
                + "it. Off, the panels are solid and the screen is not read at all."));

        // Bound after the row exists, and applied once here: the checkbox is
        // filled from the preferences before this runs on a later open, so the
        // signal alone would leave the first showing of the form disagreeing
        // with the box it is meant to follow.
        _animationsEnabled.Toggled += _ =>
        {
            ApplySpeedAvailability();
            RaiseEdited();
        };

        ApplySpeedAvailability();

        Add(
            SettingsSection.Files,
            "autosave recovery snapshot backup crash unsaved keep",
            _autosaveEnabled);

        _autosaveIntervalField = Field("Every", _autosaveInterval.Expand());
        _autosaveNote = Note("A snapshot of the open document is kept beside the editor's own "
            + "settings, never in your file, and only when something has changed since the "
            + "last one; it waits, when it can, for an open picker or dialog to close. If the "
            + "editor stops before you save, the next launch offers it back. Saving or "
            + "discarding a document removes its snapshot; switching this off stops new "
            + "snapshots and deletes none.");

        Add(SettingsSection.Files, "autosave interval seconds every", _autosaveIntervalField);
        Add(SettingsSection.Files, "autosave note recovery", _autosaveNote);
        Add(
            SettingsSection.Files,
            "autosave folder kept in directory location path",
            Field("Kept in", _autosaveFolder));
        Add(SettingsSection.Files, "autosave status last snapshot waiting", _autosaveStatus);

        // Bound where the row exists, on the animation box's own terms: the press
        // greys the interval beside it as well as storing the switch.
        _autosaveEnabled.Toggled += _ =>
        {
            ApplyAutosaveAvailability();
            RaiseEdited();
        };

        ApplyAutosaveAvailability();

        Add(SettingsSection.Debug, "drive interface injection input", _inputInjection);
        Add(
            SettingsSection.Debug,
            "injection note",
            Note("For exercising the editor itself — clicking its buttons, dragging its "
                + "handles — not for building models. Leave it off unless something is "
                + "being tested through the interface."));
    }

    private void Add(SettingsSection section, string keywords, Control control)
    {
        _rowsList.Add(new Row(section, keywords, control));
        _body.AddChild(control);
    }

    /// <summary>
    /// Shows what the section or the search asks for. Rows are hidden rather
    /// than rebuilt: a field being filtered away and back must not lose what
    /// was typed into it.
    /// </summary>
    private void RebuildBody()
    {
        string query = _search.Text;

        foreach (Row row in _rowsList)
        {
            row.Control.Visible =
                SettingsRowFilter.ShouldShow(row.Section, RowText(row), _openSection, query);
        }

        bool sectionInEffect = SettingsRowFilter.IsSectionInEffect(query);

        foreach ((SettingsSection section, Button tab) in _tabsBySectionDictionary)
        {
            tab.SetPressedNoSignal(sectionInEffect && section == _openSection);
            tab.Modulate = sectionInEffect
                ? new Color(1f, 1f, 1f, 1f)
                : new Color(1f, 1f, 1f, 0.55f);
        }
    }

    /// <summary>
    /// What a search matches a row on: the words it was registered with, and
    /// whatever it says on screen — so "port" finds the row and so does the
    /// label the eye reads.
    /// </summary>
    private static string RowText(Row row) => $"{row.Keywords} {ReadText(row.Control)}";

    private static string ReadText(Control control)
    {
        switch (control)
        {
            case Label label:
                return label.Text;

            case Button button:
                return button.Text;
        }

        var text = string.Empty;

        foreach (Node child in control.GetChildren())
        {
            if (child is Control widget)
                text += " " + ReadText(widget);
        }

        return text;
    }

    private static Label Note(string text)
    {
        var note = new Label { Text = text };
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        return note;
    }

    private static HBoxContainer Field(string label, params Control[] controls)
    {
        var row = Ui.HBox(Ui.Label(label).MinSize(LabelWidth, 0f));

        foreach (Control control in controls)
            row.AddChild(control);

        return row;
    }

    /// <summary>
    /// Slowest on the left, fastest on the right, which is the way every speed
    /// control anybody has used reads.
    /// </summary>
    /// <remarks>
    /// Ordered here rather than in the enum. <see cref="AnimationSpeed"/> puts
    /// <see cref="AnimationSpeed.Fast"/> first on purpose — a missing key and a
    /// value outside the enum both have to land on what the editor ships with —
    /// so which end of a row a word is drawn at is this form's decision and not
    /// the file format's.
    /// <para>
    /// A <see cref="SegmentedRow"/> rather than a dropdown, and the same control
    /// the transform channels' modes use: one question with a handful of answers
    /// looks the same wherever the editor asks it, and a popup's items carry no
    /// reference an agent can aim at.
    /// </para>
    /// </remarks>
    private Control BuildSpeedRow()
    {
        _speedsList.Clear();
        _speedsList.AddRange(Enum.GetValues<AnimationSpeed>().Reverse());

        _speedRow.SetSegments(
            _speedsList.Select(speed => new SegmentSpec($"Speed{speed}", speed.ToString(), string.Empty))
                .ToList());

        _speedRow.Chosen += index =>
        {
            SetSpeed(_speedsList[index]);
            RaiseEdited();
        };

        SetSpeed(_animationSpeed);

        return _speedRow;
    }

    /// <summary>
    /// Which way round the chrome is drawn. The same segmented control the pace
    /// uses one row below it, for the reason that control exists: one question
    /// with a handful of answers should look the same wherever it is asked.
    /// </summary>
    private Control BuildLanguageRow()
    {
        _languageButton.MouseOnly();
        _languageButton.Pressed += OpenLanguageMenu;
        _languageMenu.ItemChosen += OnLanguageChosen;

        SetLanguage(_language);

        return _languageButton;
    }

    private void OpenLanguageMenu()
    {
        // Parented on opening rather than in the constructor: the menu draws
        // over the card, and the card is not in the tree when its rows are built.
        if (_languageMenu.GetParent() is null)
            AddChild(_languageMenu);

        var items = new List<ContextMenuItem>();

        for (var i = 0; i < LanguageCodes.All.Count; i++)
        {
            var language = LanguageCodes.All[i];

            items.Add(new ContextMenuItem(
                i,
                LanguageCodes.GetEndonym(language),
                $"Language{language}",
                string.Empty,
                true));
        }

        _languageMenu.Open(
            _languageButton.GlobalPosition + new Vector2(0f, _languageButton.Size.Y + 2f),
            items);
    }

    private void OnLanguageChosen(int id)
    {
        if (id < 0 || id >= LanguageCodes.All.Count)
            return;

        SetLanguage(LanguageCodes.All[id]);
        RaiseEdited();
    }

    /// <summary>
    /// The button wears the chosen language's own name for itself, which is the
    /// one convention a list of languages has: somebody looking for theirs is
    /// looking for the word they would use, not for our translation of it.
    /// </summary>
    private void SetLanguage(EditorLanguage language)
    {
        _language = Enum.IsDefined(language) ? language : EditorLanguage.English;
        _languageButton.Text = LanguageCodes.GetEndonym(_language);
    }

    private Control BuildThemeRow()
    {
        _themesList.Clear();
        _themesList.AddRange(Enum.GetValues<EditorTheme>());

        _themeRow.SetSegments(
            _themesList
                .Select(theme => new SegmentSpec($"Theme{theme}", theme.ToString(), string.Empty))
                .ToList());

        _themeRow.Chosen += index =>
        {
            SetTheme(_themesList[index]);
            RaiseEdited();
        };

        SetTheme(_theme);

        return _themeRow;
    }

    private void SetTheme(EditorTheme theme)
    {
        _theme = Enum.IsDefined(theme) ? theme : EditorTheme.Dark;

        _themeRow.Select(_themesList.IndexOf(_theme));
    }

    /// <summary>
    /// The accent, picked as a colour rather than typed as six digits — though
    /// the picker's own hex field is right there for anyone who would rather
    /// type it, which is why this is the engine's picker and not a swatch row.
    /// </summary>
    /// <remarks>
    /// Opaque on purpose: a translucent accent would leave the selection tint
    /// depending on what happened to be under it, and the tints the palette
    /// derives already carry their own alpha.
    /// </remarks>
    private Control BuildAccentField()
    {
        _accent.EditAlpha = false;

        // A swatch rather than a bar across the column: it is one colour, and a
        // full-width fill of it would read as a state of the form rather than as
        // the thing being chosen.
        _accent.CustomMinimumSize = new Vector2(AccentSwatchWidth, 24f);
        _accent.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _accent.MouseOnly();

        return _accent;
    }

    private void SetSpeed(AnimationSpeed speed)
    {
        _animationSpeed = Enum.IsDefined(speed) ? speed : AnimationSpeed.Fast;

        _speedRow.Select(_speedsList.IndexOf(_animationSpeed));
    }

    /// <summary>
    /// The pace and the paragraph about it follow the box above them: with
    /// animation off there is nothing for a pace to scale.
    /// </summary>
    /// <remarks>
    /// Both halves, and they are different things. <c>Disabled</c> is what
    /// refuses the press — a greyed control that still answered would be the
    /// worse half of the pair — and the modulate is what carries the words
    /// beside the buttons with them, a <see cref="Label"/> having no disabled
    /// state of its own. The value is left exactly as it stands: switching
    /// animation off and on again must give back the pace that was picked, not
    /// whatever the default happens to be.
    /// </remarks>
    private void ApplySpeedAvailability()
    {
        bool available = _animationsEnabled.ButtonPressed;

        _speedRow.SetEnabled(available);
        SetRowAvailability(_speedField, _speedNote, available);
    }

    /// <summary>
    /// The interval and the paragraph about it follow the box above them, on the
    /// pace row's own terms: with snapshots off there is nothing for an interval
    /// to time. The value is left standing, so switching back on gives back the
    /// interval that was picked.
    /// </summary>
    private void ApplyAutosaveAvailability()
    {
        bool available = _autosaveEnabled.ButtonPressed;

        _autosaveInterval.Editable = available;
        SetRowAvailability(_autosaveIntervalField, _autosaveNote, available);
    }

    /// <summary>
    /// The shared half of a row following a box: the words go grey with it. What
    /// refuses the press is the caller's, because it differs per control — a
    /// segmented row is disabled, a spin box is made read-only.
    /// </summary>
    private static void SetRowAvailability(Control? field, Control? note, bool available)
    {
        var modulate = new Color(1f, 1f, 1f, available ? 1f : RecededAlpha);

        if (field is not null)
            field.Modulate = modulate;

        if (note is not null)
            note.Modulate = modulate;
    }

    /// <summary>
    /// Hands over the whole form. Every control reports the lot rather than the
    /// one value that moved: a preference is stored as a unit, and the form is
    /// the only thing that knows what is in the others.
    /// </summary>
    private void RaiseEdited()
    {
        if (_filling)
            return;

        Edited?.Invoke(new PreferenceValues(
            _mcpEnabled.ButtonPressed,
            (int)_port.Value,
            _clientConfigPath.Text,
            _countdownEnabled.ButtonPressed,
            _countdownSeconds.Value,
            _maximumWait.Value,
            _silenceWarning.Value,
            _silenceGrace.Value,
            _inputInjection.ButtonPressed,
            _viewCubeCorner,
            _panelPlacements ?? Array.Empty<PanelPlacement>(),
            _animationsEnabled.ButtonPressed,
            _animationSpeed,
            _theme,
            $"#{_accent.Color.ToHtml(includeAlpha: false)}",
            _language,
            _viewportOcclusion.ButtonPressed,
            _viewportShadows.ButtonPressed,
            _viewportFactures.ButtonPressed,
            _glassPanels.ButtonPressed,
            _autosaveEnabled.ButtonPressed,
            _autosaveInterval.Value));
    }

    private void RaiseClose() => CloseRequested?.Invoke();

    private readonly record struct Row(SettingsSection Section, string Keywords, Control Control);
}
