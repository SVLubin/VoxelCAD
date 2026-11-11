using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VoxelCAD.CommonTypes;
using static VoxelCAD.Localization.Translations;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Code-first editor chrome: hierarchy and history on the left, inspector on the
/// right, over whatever the 3D layer draws.
/// </summary>
/// <remarks>
/// Input is reported, not interpreted: the shortcut handler raises
/// <see cref="UndoRequested"/> and stops there. What undo means is a presenter's
/// business. The same goes for the close button — this asks, it does not quit.
/// </remarks>
internal sealed partial class ShellView : CanvasLayer, IShellView
{
    /// <summary>The least body worth granting a tree panel — about three rows.</summary>
    private const float TreeMinBody = 84f;

    /// <summary>The least body worth granting the inspector before it scrolls.</summary>
    private const float InspectorMinBody = 140f;

    /// <summary>
    /// What the inspector's sections stand in from the panel's own edges. They
    /// had the frame's single unit and nothing else, so a section's border ran
    /// almost along the surface's, and two lines a unit apart read as a mistake
    /// rather than as a card on a pane.
    /// </summary>
    private const int InspectorInset = 6;

    /// <summary>How far the navigation cube sits in from the viewport's edges.</summary>
    private const int ViewCubeInset = 12;

    /// <summary>
    /// How far the drag readout sits in from the viewport's bottom-left corner. The
    /// cube's own inset: two things tucked into the corners of one panel should be
    /// tucked in by the same amount.
    /// </summary>
    private const int DragReadoutInset = ViewCubeInset;

    private readonly HierarchyView _hierarchy = new();
    private readonly InspectorView _inspector;
    private readonly HistoryView _history = new();
    private readonly DialogHost _dialogs;
    private readonly SettingsView _settings;
    private readonly RenderView _render;
    /// <summary>
    /// What may be created where the selection stands, pushed by the presenter.
    /// Plain state rather than buttons: creation is the hierarchy's context
    /// menu now, and a menu is built at the moment it opens — so what the shell
    /// has to keep between openings is the answer, not a widget wearing it.
    /// </summary>
    private readonly Dictionary<CreateKind, bool> _createEnabledByKindDictionary = new();
    private readonly Button _undo;
    private readonly Button _redo;
    private readonly Button _referencesVisible = new() { Name = "ReferencesVisible" };
    private readonly ControlBanner _controlBanner;
    private readonly UiDriver _uiDriver;
    private readonly ViewCube _viewCube;
    private readonly DragReadout _dragReadout;
    private readonly ViewportSurface _surface;
    private readonly Dictionary<PanelKind, ChromePanel> _panelsByKindDictionary = new();
    private readonly EdgeColumn _leftColumn;
    private readonly EdgeColumn _rightColumn;
    private readonly List<Control> _stripControlsList = new();

    private readonly IInputState _input;
    private readonly MotionTiming _timing;
    private readonly PanelHoverIntent _hoverIntent;
    private readonly ContextMenuView _hierarchyMenu = new();

    // The second step of "Replace uses…": the same card, listing the materials
    // the presenter last pushed, opened where the first menu stood.
    private readonly ContextMenuView _materialPickMenu = new();
    private List<MaterialChoice> _offeredChoicesList = new();
    private Vector2 _menuPosition;

    /// <summary>
    /// The File popup, and a second card rather than the hierarchy's own: the
    /// two answer different ids and one of them is open at a time only by
    /// coincidence. An invisible menu takes no mouse, so a spare costs nothing.
    /// </summary>
    private readonly ContextMenuView _fileMenu = new() { Name = "FileMenuCard" };

    private Button? _fileMenuButton;
    private readonly NativeMenuBar? _nativeMenu;

    /// <summary>Whether the File menu's recovery row is live; see <see cref="SetRecoverEnabled"/>.</summary>
    private bool _recoverEnabled;

    private IReadOnlyList<PanelPlacement> _panelPlacements = Array.Empty<PanelPlacement>();

    /// <summary>
    /// Which panel is temporarily shown, and whether it was asked for by a
    /// click rather than a glance. A glance retreats when the pointer wanders
    /// off; something clicked open stays until it is dismissed, which is the
    /// whole difference between the two gestures.
    /// </summary>
    /// <remarks>
    /// One field on purpose, because they are one state and were two: sites
    /// that cleared the kind alone left the flag standing, and the only writer
    /// that clears it returns early when there is no panel — so pinning a
    /// clicked-open panel stranded "held" true and hover-close never fired
    /// again for the rest of the session. Half a state is unwritable now.
    /// </remarks>
    private (PanelKind Kind, bool Held)? _openTemp;

    /// <summary>
    /// How many interactions are in flight — the shell's own count of the same
    /// pairs it forwards as <see cref="UserInteractionBegan"/>. The hover close
    /// asks it, because a temporary panel must not retreat from under an open
    /// picker or a focused field.
    /// </summary>
    private int _interactionsHeldCount;

    private readonly PanelDragState _drag = new();
    private IReadOnlyList<PanelPlacement>? _placementsBeforeDrag;
    private (PanelColumnDrop Left, PanelColumnDrop Right)? _dropGeometryBeforeDrag;
    private Control _uiRoot = null!;
    private Vector2 _grabOffset;

    /// <summary>
    /// Which end of the carried card its glyph stands at, frozen at the pick-up
    /// for the length of the gesture — a panel is carried as the thing it is.
    /// See <see cref="EdgeColumn.SetCarried"/> for why it is the side it came
    /// from rather than the side it is over.
    /// </summary>
    private bool _carriedStubOnRight;

    private bool _buttonWasDown;
    private bool _skinApplied;

    // The resize gesture in flight, if any: which grip was taken hold of and
    // everything needed to answer a frame of pull or to put it all back. The
    // placements are the pick-up's, the drag rule — a pull applied to its own
    // output accumulates instead of answering.
    private (PanelKind Kind, PanelSide Side, bool HeightToo, Vector2 Start,
        float StartWidth, float StartHeight,
        IReadOnlyList<PanelPlacement> Before)? _resize;

    /// <summary>
    /// Whether this press has already been answered by a drag. A release ends a
    /// drag on the very control the press started from, and the engine reads
    /// that as a click — so a header would toggle and a pin would flip on top of
    /// the drop. It outlives the drag itself, because Escape cancels one and the
    /// release still arrives afterwards; only the button coming up clears it.
    /// </summary>
    private bool _pressAnswered;

    /// <summary>Whether the selection may be deleted; see the create states above.</summary>
    private bool _deleteEnabled;

    /// <summary>The row the open context menu was raised on.</summary>
    private SelectionTarget _menuTarget;

    private ControlBannerKind _bannerKind = ControlBannerKind.None;

    /// <summary>
    /// The editor's look, shared and repainted in place. Held so the settings
    /// form's answer can reach it; nothing here reads a colour out of it.
    /// </summary>
    private readonly EditorSkin _skin;
    private readonly ThemeVeil _veil = new();

    public ShellView(
        ISceneView scene,
        ViewportSurface surface,
        ViewCube viewCube,
        DragReadout dragReadout,
        EditorSkin skin,
        InjectedInputState injected,
        IInputState input,
        MotionTiming timing)
    {
        _timing = timing;
        _skin = skin;

        Theme theme = skin.Theme;

        // Not field initializers, because both are measured at the editor's own
        // pace like every transition is — and off when they are: a dwell before
        // a panel opens, and the settings card's own arrival.
        _hoverIntent = new PanelHoverIntent(timing);
        _settings = new SettingsView(timing);
        _render = new RenderView(timing);
        _inspector = new InspectorView(timing);
        _dialogs = new DialogHost(timing);

        Scene = scene;
        _viewCube = viewCube;
        _dragReadout = dragReadout;
        _input = input;

        // Handed the agent's pointer so its dimmer can stay open at exactly
        // that point while shielding the panels from the machine's hover.
        _controlBanner = new ControlBanner(injected, timing);

        // Named, and not for the inspector's benefit: a name is what a control's
        // agent-facing reference is spelled with, and an unnamed one falls back to
        // a positional "Button@2" that a sibling inserted above it would break.
        _undo = Ui.Button("Undo", () => UndoRequested?.Invoke()).Named("Undo");
        _redo = Ui.Button("Redo", () => RedoRequested?.Invoke()).Named("Redo");

        // The toolbar is icons only, and only the near-system actions a hand
        // reaches for between edits. Everything rarer stays behind the File
        // popup until a real menu exists — on macOS most likely the platform's
        // own menu bar, which is its ledger entry's plan.
        StyleIconButton(theme, _undo, EditorIconCatalog.Undo, "Undo");
        StyleIconButton(theme, _redo, EditorIconCatalog.Redo, "Redo");

        // Lit while they are drawn, which is the ordinary state — a toggle rather
        // than a pair of glyphs, so what it says is "references are on" rather than
        // "press me to do the other thing".
        StyleIconButton(
            theme,
            _referencesVisible,
            EditorIconCatalog.ReferenceImage,
            "Draw the references, or take them all off screen for a moment. It is not the "
            + "document: nothing is saved, nothing is undone, and no element's own eye is "
            + "touched.");

        _referencesVisible.ToggleMode = true;
        _referencesVisible.SetPressedNoSignal(true);
        _referencesVisible.Toggled += pressed => ReferencesVisibleRequested?.Invoke(pressed);

        // The platform's own menu bar where there is one — on macOS the strip
        // every Mac user looks at first. It owns the chords it shows, so the
        // shell stands down from reading them, and the in-window File button
        // goes away rather than saying the same thing twice.
        _nativeMenu = NativeMenuBar.TryBuild(BuildMenuActions());

        _fileMenuButton = CreateFileMenu(theme);
        _fileMenuButton.Visible = _nativeMenu == null;

        Control toolbarRow = Ui.HBox(
            _fileMenuButton,
            IconButton(theme, "Open", EditorIconCatalog.Open, "Open a project",
                () => OpenRequested?.Invoke()),
            IconButton(theme, "Save", EditorIconCatalog.Save, "Save the project",
                () => SaveRequested?.Invoke()),
            _undo,
            _redo,
            Spacer(),
            _referencesVisible,
            IconButton(theme, "Preferences", EditorIconCatalog.Preferences, "Preferences",
                () => PreferencesRequested?.Invoke())).Named("ToolbarRow");

        // Icon rows breathe a little wider than the global separation, and
        // never touch the pane's frame.
        toolbarRow.AddThemeConstantOverride("separation", 6);


        // Every panel roots under one full-rect Control that carries the shared
        // theme. Theme inheritance does not cross a CanvasLayer, so applying it
        // here — rather than on the window alone — is what actually reaches the
        // docks. The root itself ignores the mouse; each panel claims its own
        // share.
        Control uiRoot = _uiRoot = new Control { Name = "UiRoot" };
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        uiRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
        uiRoot.Theme = theme;

        // The viewport is a panel among panels, and this is where its share of
        // the window gets decided. The docks' width is theirs to know — this
        // seeds the offsets and ApplyStripInsets follows their live width from
        // there. Handing it a real rect is also what lets it draw a focus ring
        // the docks cannot cut in half.
        uiRoot.AddChild(FillBetweenDocks(surface));

        // Over the 3D and under the docks, in the viewport's own share of the
        // window rather than the window's — the cube belongs to the view it
        // describes, so it must not drift under a dock when one is resized. Its
        // area ignores the mouse; the cube inside it claims it.
        var viewCubeArea = new Control { Name = "ViewCubeArea" };
        viewCubeArea.MouseFilter = Control.MouseFilterEnum.Ignore;
        viewCubeArea.AddChild(_viewCube);
        uiRoot.AddChild(FillBetweenDocks(viewCubeArea));

        SetViewCubeCorner(ViewCubeCorner.TopRight);

        // The opposite corner of the same panel, on the same terms as the cube: it
        // describes the viewport, so it must not drift under a dock when one is
        // resized. Bottom-left is the far corner from the cube and the one a CAD
        // status readout is looked for in.
        var dragReadoutArea = new Control { Name = "DragReadoutArea" };
        dragReadoutArea.MouseFilter = Control.MouseFilterEnum.Ignore;
        dragReadoutArea.AddChild(_dragReadout);
        uiRoot.AddChild(FillBetweenDocks(dragReadoutArea));

        _dragReadout.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.BottomLeft,
            Control.LayoutPresetMode.KeepSize,
            DragReadoutInset);

        // It is pinned to a corner with no size of its own yet, so where it grows
        // when its text arrives is the whole question: up and to the right, away from
        // the two edges it is tucked into. Godot grows down and right by default,
        // which put the line under the bottom of the window — seen, not reasoned out.
        _dragReadout.GrowVertical = Control.GrowDirection.Begin;
        _dragReadout.GrowHorizontal = Control.GrowDirection.End;

        // Nothing here says whether the scene is listening, and that is a
        // decision rather than a gap — see the ledger. Two things already say
        // it where the eye is: whichever panel holds the keyboard is wearing a
        // focus ring, and a cell tool over the scene draws no preview until the
        // viewport has it back.

        // The four panels of docs/panels.md. Which column each stands in, and in
        // what state, arrives through SetPanelLayout — the presenter applies the
        // stored placements, so a freshly built shell holds its panels parked
        // and hidden until told.
        _surface = surface;

        CreatePanel(new ChromePanel(
            PanelKind.Toolbar,
            Localized("Toolbar"),
            Ui.Margin(toolbarRow, 4).Named("ToolbarBody"),
            timing,
            contentScrolls: false,
            heightResizable: false));
        CreatePanel(new ChromePanel(
            PanelKind.Project,
            Localized("Project"),
            Ui.VBox(
                _hierarchy.Named("HierarchyTree")).Named("ProjectBody"),
            timing,
            contentScrolls: true,
            TreeMinBody,
            () => TreeContentHeight.Measure(_hierarchy),
            () => TreeScrollExtent.Measure(_hierarchy)));
        CreatePanel(new ChromePanel(
            PanelKind.History,
            Localized("History"),
            _history.Named("HistoryList"),
            timing,
            contentScrolls: true,
            TreeMinBody,
            () => TreeContentHeight.Measure(_history),
            () => TreeScrollExtent.Measure(_history)));
        // Inset from the panel's own edges, unlike the two trees. A tree's rows
        // are meant to run the full width — that is what makes a row a band you
        // can click anywhere along — while the inspector's sections are cards,
        // and a card flush against the surface it sits on reads as part of it.
        CreatePanel(new ChromePanel(
            PanelKind.Inspector,
            Localized("Inspector"),
            Ui.Margin(_inspector.Named("Inspector"), InspectorInset).Named("InspectorBody"),
            timing,
            contentScrolls: false,
            InspectorMinBody));

        _leftColumn = new EdgeColumn(PanelSide.Left, timing);
        _rightColumn = new EdgeColumn(PanelSide.Right, timing);

        foreach (EdgeColumn column in new[] { _leftColumn, _rightColumn })
        {
            column.WidthChanged += ApplyStripInsets;
            uiRoot.AddChild(column);
        }

        // Parked hidden until the first SetPanelLayout files them into columns —
        // a node with no parent would never be freed with the shell.
        foreach (ChromePanel panel in _panelsByKindDictionary.Values)
        {
            panel.Visible = false;
            uiRoot.AddChild(panel);
        }

        // A temporary panel is a glance, and it ends when the hand moves on —
        // the viewport taking the keyboard, a press landing on the scene, or
        // Two of the three from the surface, because the surface is the one that
        // knows. Escape is answered here instead — see _Input.
        surface.FocusEntered += CloseTempPanel;
        surface.PointerPressed += CloseTempPanel;

        // The banner goes on last so its dimmer sits above the docks it suspends.
        // Above every panel and below the banner: a menu is chrome like the
        // rest, so the dimmer that suspends the interface has to cover it —
        // over the dimmer it stayed live to the machine's hover while an agent
        // held the editor, which is exactly what the dimmer exists to stop.
        uiRoot.AddChild(_hierarchyMenu);
        uiRoot.AddChild(_materialPickMenu);
        uiRoot.AddChild(_fileMenu);

        // Over the menu and under the banner, on the same terms: it is a
        // surface of the shell's, so the dimmer that suspends the interface has
        // to cover it too.
        uiRoot.AddChild(_settings);

        // Over the settings card on the same terms, and under the question: a
        // render is asked for from the menu and put away again, so it stands
        // where a form stands.
        uiRoot.AddChild(_render);

        // Front-most of the shell's own surfaces and still under the banner: a
        // question is asked over everything else, and an agent holding the
        // editor suspends it like anything else the shell draws.
        uiRoot.AddChild(_dialogs);

        var bannerLayer = new Control { Name = "BannerLayer" };
        bannerLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bannerLayer.MouseFilter = Control.MouseFilterEnum.Ignore;
        bannerLayer.AddChild(_controlBanner);
        uiRoot.AddChild(bannerLayer);

        // The one thing above the banner, and the one exception to the rule the
        // three comments above state. It covers the window for a third of a
        // second while the look is swapped, refusing every event from every
        // party while it does — so it is the dimmer's refusal widened rather
        // than a way past it, and the plaque is themed, so it has to be covered
        // like everything else or it flashes on its own.
        uiRoot.AddChild(_veil);

        AddChild(uiRoot);
        _hierarchyMenu.ItemChosen += OnHierarchyMenuPressed;
        _materialPickMenu.ItemChosen += OnMaterialPickChosen;
        _fileMenu.ItemChosen += OnFileMenuPressed;
        _hierarchy.ContextMenuRequested += OnHierarchyContextMenu;

        // Built last, over the finished tree: it walks what is above rather than
        // holding anything of its own.
        _uiDriver = new UiDriver(this, injected);

        _controlBanner.ActionPressed += OnBannerActionPressed;

        _inspector.InteractionBegan += OnInteractionBegan;
        _inspector.InteractionEnded += OnInteractionEnded;

        _dialogs.InteractionBegan += OnInteractionBegan;
        _dialogs.InteractionEnded += OnInteractionEnded;

        _settings.InteractionBegan += OnInteractionBegan;
        _settings.InteractionEnded += OnInteractionEnded;

        // A handle being dragged is as much a thing not to seize the editor
        // out of as a focused field or an open dialog. The scene view raises the
        // drag; turning it into the token every other interaction raises is the
        // shell's, which is the one place that owns the pair.
        Scene.GizmoDragBegan += OnInteractionBegan;
        Scene.GizmoDragEnded += OnInteractionEnded;
        Scene.GizmoDragCancelled += OnInteractionEnded;

        // A brush stroke is the same kind of thing: a button held over the scene,
        // with a record open under it.
        Scene.StrokeBegan += OnInteractionBegan;
        Scene.StrokeEnded += OnInteractionEnded;
    }

    public event Action? NewRequested;

    public event Action? OpenRequested;

    public event Action? RecoverRequested;

    public event Action? SaveRequested;

    public event Action? SaveAsRequested;

    public event Action? ImportRequested;

    public event Action? ExportRequested;

    public event Action? CloseRequested;

    public event Action? UndoRequested;

    public event Action? RedoRequested;

    public event Action<CreateKind>? CreateRequested;

    public event Action<IReadOnlyList<string>>? FilesDropped;

    public event Action<bool>? ReferencesVisibleRequested;

    public event Action? DeleteSelectionRequested;

    public event Action? CopyRequested;

    public event Action? CutRequested;

    public event Action? PasteRequested;

    public event Action? DuplicateRequested;

    public event Action<Guid, Guid>? ReplaceMaterialUsesRequested;

    public event Action? PreferencesRequested;

    public event Action? RenderRequested;

    public event Action<IReadOnlyList<PanelPlacement>>? PanelLayoutEdited;

    public event Action<PanelWidths>? PanelWidthsEdited;

    public event Action? CancelControlRequested;

    public event Action? StopControlRequested;

    public event Action? UserInteractionBegan;

    public event Action? UserInteractionEnded;

    public ISceneView Scene { get; }

    public IHierarchyView Hierarchy => _hierarchy;

    public IInspectorView Inspector => _inspector;

    public IHistoryView History => _history;

    public ISettingsView Settings => _settings;

    public IRenderView Render => _render;

    public IUiDriver UiDriver => _uiDriver;

    public override void _Ready()
    {
        // Otherwise the engine closes the window before anyone can ask whether
        // there is unsaved work in it.
        GetTree().AutoAcceptQuit = false;

        // The window's, not a control's: a file is dropped on the editor rather
        // than on any one panel of it, and where it lands is the presenter's — a
        // reference goes under whatever is selected, exactly as the menus place it.
        GetWindow().FilesDropped += OnFilesDropped;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            CloseRequested?.Invoke();

        // The platform's menus outlive this node unless they are taken down.
        if (what == NotificationPredelete)
            _nativeMenu?.Dispose();
    }

    /// <summary>
    /// The hover half of the temporary panels, polled per frame from the
    /// driving party's pointer — never from engine hover, which follows the
    /// machine's cursor and would let a bystander close an agent's panel. The
    /// rules live in <see cref="PanelHoverIntent"/>; this only gathers the
    /// facts and applies the answer.
    /// </summary>
    public override void _Process(double delta)
    {
        PanelKind? hoveredTab = null;
        var pointerOnOpenPanel = false;
        var interactionHeld = _interactionsHeldCount > 0;

        if (TickDrag())
            return;

        // A press in progress is not a glance. Without this, holding a tab down
        // for longer than the dwell opened it under the hand before the drag had
        // travelled far enough to be one — and the panel was then picked up at
        // whatever size that had reached.
        if (_drag.Held is not null)
            return;

        if (_input.TryGetPointer(out Vector2 pointer))
        {
            if (_leftColumn.TryGetTabAt(pointer, out PanelKind leftTab))
                hoveredTab = leftTab;
            else if (_rightColumn.TryGetTabAt(pointer, out PanelKind rightTab))
                hoveredTab = rightTab;

            if (_openTemp is { } open)
            {
                ChromePanel panel = _panelsByKindDictionary[open.Kind];

                pointerOnOpenPanel = panel.Visible && panel.GetGlobalRect().HasPoint(pointer);
                interactionHeld = interactionHeld
                    || panel.HoldsFocus()
                    || _input.IsButtonDown(MouseButton.Left);
            }
        }

        HoverDecision decision = _hoverIntent.Tick(
            (float)delta, hoveredTab, _openTemp?.Kind, pointerOnOpenPanel, interactionHeld);

        if (decision.Open is PanelKind kind && !FindPlacement(kind).Pinned)
        {
            CloseTempPanel();
            _openTemp = (kind, Held: false);
            ApplyPanelDistribution();
        }
        else if (decision.Close && _openTemp is { Held: false })
        {
            CloseTempPanel();
        }
    }

    /// <summary>
    /// Carrying a tab, polled per frame like the hover it takes precedence
    /// over. Answers whether a drag is in hand, so a glance cannot open a panel
    /// out from under one.
    /// </summary>
    /// <remarks>
    /// Polled, and deliberately not Godot's drag and drop: a `_GetDragData`
    /// handshake is reachable only by a real mouse, where this is a press, some
    /// motion and a release — which is exactly what `input-drag` sends, so the
    /// gesture an agent performs is the gesture a hand performs. The ledger's
    /// `drop_mode_flags` entry is the other half of the argument.
    /// </remarks>
    private bool TickDrag()
    {
        bool down = _input.IsButtonDown(MouseButton.Left);
        bool pressed = down && !_buttonWasDown;
        bool released = !down && _buttonWasDown;

        _buttonWasDown = down;

        // Cleared when a new gesture starts, never when this one ends. Godot
        // emits a Button's `pressed` on the button coming *up*, so a flag
        // cleared on the release frame is already down when the very click it
        // exists to swallow arrives — and a panel dropped back onto its own
        // header toggled itself open on top of the drop. Only a drag ever
        // raises it, so a plain click can never find it standing.
        if (pressed)
            _pressAnswered = false;

        if (!_input.TryGetPointer(out Vector2 pointer))
        {
            CancelDrag();
            CancelResize();

            return false;
        }

        // A grip press is a resize, never a panel drag: the grip lives in the
        // body, headers are the draggable, and checking here keeps the two
        // gestures from ever answering one press.
        if (pressed && TryBeginResize(pointer))
            _pressAnswered = true;

        if (_resize is not null)
        {
            TickResize(pointer, released);

            return true;
        }

        if (pressed)
            BeginDrag(pointer);

        if (_drag.Held is null)
            return false;

        _drag.MoveTo(pointer);

        if (released)
            return EndDrag();

        if (!_drag.IsDragging)
            return false;

        _pressAnswered = true;
        CarryTo(pointer);

        return true;
    }

    private void BeginDrag(Vector2 pointer)
    {
        // Any panel, by its header, so long as it has stopped moving: a tab by
        // its glyph and an open panel by its title bar are the same gesture.
        if (!_leftColumn.TryGetDraggable(pointer, out PanelKind kind)
            && !_rightColumn.TryGetDraggable(pointer, out kind))
            return;

        // A plain distance, because nothing about the panel changes while it is
        // carried: it travels as the thing it is, so the point taken hold of
        // stays the point under the hand. Measured against the whole surface
        // and not the header, since the surface is what is placed.
        _grabOffset = pointer - _panelsByKindDictionary[kind].GetGlobalRect().Position;
        _carriedStubOnRight = FindPlacement(kind).Side == PanelSide.Right;
        _placementsBeforeDrag = _panelPlacements;

        // The ruler the pointer is judged by, frozen for the length of the
        // gesture. The list was already taken from the moment of the pick-up;
        // the geometry it is measured against was not, and half a snapshot is
        // a decision fed from its own output. It does not merely drift — it
        // does not settle: the preview puts the carried panel's hole at the top
        // of the stack, DescribeDrop then leaves that panel out of the span it
        // reports, so the span begins *below the panel's own hole* and a
        // pointer parked in it reads as outside the stack. Next frame the
        // preview is withdrawn, the hole returns, and the answer flips back.
        // One frame per state, each of them a valid layout — invisible to a
        // screenshot and to a recording sampling one phase, and read by the eye
        // as one image doubled. Measured over a 691-unit column: 148 of 692
        // pointer positions never reach a fixed point (PanelDragLoopTests).
        _dropGeometryBeforeDrag = (
            _leftColumn.DescribeDrop(kind), _rightColumn.DescribeDrop(kind));

        _drag.Press(kind, pointer);
    }

    /// <summary>
    /// Puts the carried tab under the hand and the rest of the run where it
    /// would be if the tab were let go here. The parting is not animated by
    /// anything: the other tabs' targets moved, and the motion carries them.
    /// </summary>
    private void CarryTo(Vector2 pointer)
    {
        if (_drag.Held is not PanelKind kind)
            return;

        // Some *other* glance has no business standing while a panel is in the
        // air — but the one being carried stays open if it was open: a panel
        // travels as the thing it is, and lands as the thing it was.
        if (_openTemp is { } open && open.Kind != kind)
            CloseTempPanel();

        // Which block the hand is over decides what the panel is, and the
        // preview then draws it as that — dropped between the pinned ones it is
        // drawn pinned, carried back out it is a tab again, and nothing is
        // stored either way until the hand lets go.
        (PanelColumnDrop left, PanelColumnDrop right) =
            _dropGeometryBeforeDrag ?? (_leftColumn.DescribeDrop(kind), _rightColumn.DescribeDrop(kind));

        PanelDropTarget target = PanelDropTargets.Solve(
            pointer.X,
            pointer.Y,
            _rightColumn.GlobalPosition.X + _rightColumn.Size.X,
            left,
            right);

        // Solved from the placements as they were when the tab was picked up,
        // never from last frame's preview: applied to its own output, a move
        // would accumulate rather than answer.
        //
        // Applied but not raised, so nothing is stored until the hand lets go
        // and a cancelled drag needs no undo of its own.
        SetPanelLayout(PanelPlacementEdit.Move(
            _placementsBeforeDrag ?? _panelPlacements,
            kind,
            target.Side,
            target.Pinned,
            target.Index));

        Vector2 corner = pointer - _grabOffset;

        _leftColumn.SetCarried(kind, corner, _carriedStubOnRight);
        _rightColumn.SetCarried(kind, corner, _carriedStubOnRight);

        // The columns are siblings under one root and neither clips, while a
        // carried panel's rect is column-local and unbounded — so a panel filed
        // in the left column is drawn under the whole right column wherever it
        // reaches across, which is both the tail of a right-to-left drag and
        // the approach of a left-to-right one. Depth inside a column is the
        // column's own answer (PanelDepthOrder); this is the same rule one
        // level up.
        RaiseColumn(target.Side == PanelSide.Left ? _leftColumn : _rightColumn);

        // Both columns lit as places a tab may go, the one under the hand more
        // strongly — an area you can drop on should say so before you let go.
        _leftColumn.SetDropHint(target.Side == PanelSide.Left);
        _rightColumn.SetDropHint(target.Side == PanelSide.Right);
    }

    private bool EndDrag()
    {
        PanelKind? dropped = _drag.Held;
        PanelDragOutcome outcome = _drag.Release();

        ClearCarry();
        _placementsBeforeDrag = null;

        if (outcome != PanelDragOutcome.Drop || dropped is not PanelKind kind)
            return false;

        // The preview is already what the columns are showing, so the drop is
        // nothing but the moment it is written down. The hand is still resting
        // on the tab it just put down, so hover is latched off it on the same
        // terms as every other decision taken with the pointer in place.
        PanelLayoutEdited?.Invoke(_panelPlacements);
        _hoverIntent.SuppressUntilPointerLeaves(kind);

        // And a drop commits the glance that was carrying the panel. Where the
        // pointer comes to rest says nothing about whether the panel is still
        // wanted — the hand just moved it on purpose, which is at least what a
        // click on its tab means, and a click already lands it held.
        //
        // Without this the panel is drawn open at its new place for exactly one
        // frame and is a tab on the next, because the tick that decides its
        // fate cannot run until the columns have laid it out: nobody knows the
        // landing rect before then, so "is the pointer on it" has no answer on
        // the frame of the drop. With animation on that read as a retraction;
        // with animation off, and the grace at nought, it is a flash. A state
        // that lasts one frame is not a state.
        //
        // A panel dropped into the pinned stack never reaches here as the open
        // one — SetPanelLayout drops it the moment the preview pins it.
        if (_openTemp is { } open && open.Kind == kind)
            _openTemp = (kind, Held: true);

        return true;
    }

    private void CancelDrag()
    {
        if (_drag.Held is null)
            return;

        _drag.Cancel();
        ClearCarry();

        if (_placementsBeforeDrag is { } before)
            SetPanelLayout(before);

        _placementsBeforeDrag = null;
    }

    /// <summary>
    /// Takes hold of a resize grip, if the press is on one. What is remembered
    /// is everything a cancel must put back and everything a pull is measured
    /// from — the width the column had, the body height the panel showed, and
    /// the placements as they stood.
    /// </summary>
    private bool TryBeginResize(Vector2 pointer)
    {
        PanelSide side;
        PanelKind kind;
        bool heightToo;

        if (_leftColumn.TryGetResizeGrip(pointer, out kind, out heightToo))
            side = PanelSide.Left;
        else if (_rightColumn.TryGetResizeGrip(pointer, out kind, out heightToo))
            side = PanelSide.Right;
        else
            return false;

        EdgeColumn column = ColumnOf(side);

        column.SetResizing(true);

        _resize = (
            kind,
            side,
            heightToo,
            pointer,
            column.BodyWidth,
            _panelsByKindDictionary[kind].CurrentBodyHeight,
            _panelPlacements);

        return true;
    }

    /// <summary>
    /// One frame of a resize: the pull so far turned into a width for the side
    /// and — where the grip moves it — a height for the panel, both applied
    /// live and stored only on the release. The width is clamped to the same
    /// bounds the preferences hold a file to, the height floored at the panel's
    /// own minimum; the cascade already owns the ceiling.
    /// </summary>
    private void TickResize(Vector2 pointer, bool released)
    {
        if (_resize is not { } resize)
            return;

        EdgeColumn column = ColumnOf(resize.Side);

        column.SetBodyWidth(PanelResize.ClampWidth(
            resize.StartWidth + PanelResize.TowardScene(resize.Side, pointer.X - resize.Start.X)));

        if (resize.HeightToo)
        {
            float height = PanelResize.ClampHeight(
                resize.StartHeight + (pointer.Y - resize.Start.Y),
                _panelsByKindDictionary[resize.Kind].MinBody);

            // Applied but not raised, the drag's own bargain: nothing is stored
            // until the hand lets go, and a cancel needs no undo of its own.
            SetPanelLayout(WithHeight(resize.Before, resize.Kind, height));
        }

        if (!released)
            return;

        // Both payloads taken before either event is raised. Storing the first
        // raises the preferences' Changed, which synchronously pushes the
        // stored answer back through SetPanelLayout — so read after the raise,
        // the placements are the stored list again and the dragged height has
        // already been overwritten by its own echo. Measured: the width landed
        // and the height read back null.
        IReadOnlyList<PanelPlacement> edited = _panelPlacements;
        var widths = new PanelWidths(_leftColumn.BodyWidth, _rightColumn.BodyWidth);

        column.SetResizing(false);
        _resize = null;

        PanelWidthsEdited?.Invoke(widths);

        if (resize.HeightToo)
            PanelLayoutEdited?.Invoke(edited);
    }

    /// <summary>A pull let go of early puts everything back where it stood.</summary>
    private void CancelResize()
    {
        if (_resize is not { } resize)
            return;

        EdgeColumn column = ColumnOf(resize.Side);

        column.SetBodyWidth(resize.StartWidth);
        column.SetResizing(false);
        SetPanelLayout(resize.Before);

        _resize = null;
    }

    private static IReadOnlyList<PanelPlacement> WithHeight(
        IReadOnlyList<PanelPlacement> placements, PanelKind kind, float height)
    {
        var edited = new List<PanelPlacement>(placements.Count);

        foreach (PanelPlacement placement in placements)
        {
            edited.Add(placement.Kind == kind
                ? placement with { Height = height }
                : placement);
        }

        return edited;
    }

    private EdgeColumn ColumnOf(PanelSide side) =>
        side == PanelSide.Left ? _leftColumn : _rightColumn;

    /// <summary>Puts the column a drop is aimed at in front of the other.</summary>
    private void RaiseColumn(EdgeColumn column)
    {
        EdgeColumn other = column == _leftColumn ? _rightColumn : _leftColumn;

        if (column.GetIndex() < other.GetIndex())
            _uiRoot.MoveChild(column, other.GetIndex());
    }

    private void ClearCarry()
    {
        _dropGeometryBeforeDrag = null;

        // Back to the order they were built in. At rest the two overlap only in
        // a window narrower than a left overlay plus a wide right column, which
        // under canvas_items with no explicit base is reachable — so which of
        // them is later must not be left as a residue of the last drag.
        if (_leftColumn.GetIndex() > _rightColumn.GetIndex())
            _uiRoot.MoveChild(_leftColumn, _rightColumn.GetIndex());

        foreach (EdgeColumn column in new[] { _leftColumn, _rightColumn })
        {
            column.SetCarried(null, Vector2.Zero);
            column.SetDropHint(null);
        }
    }

    private bool TryGetTabAt(Vector2 point, out PanelKind kind) =>
        _leftColumn.TryGetTabAt(point, out kind)
        || _rightColumn.TryGetTabAt(point, out kind);

    private void OnInteractionBegan()
    {
        _interactionsHeldCount++;
        UserInteractionBegan?.Invoke();
    }

    private void OnInteractionEnded()
    {
        _interactionsHeldCount = Math.Max(0, _interactionsHeldCount - 1);
        UserInteractionEnded?.Invoke();
    }

    /// <summary>
    /// The shell's chords. Which modifier spells a command is
    /// <see cref="ShellShortcuts"/>'s answer, not a literal here — asking for
    /// <c>CtrlPressed</c> alone left Cmd+Z, the chord a macOS user actually
    /// presses, doing nothing.
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
            return;

        // The platform's menu bar answers these itself where it exists, before
        // the event ever reaches the window; reading them here as well would be
        // a second answer to one press.
        if (_nativeMenu is { OwnsShellShortcuts: true })
            return;

        // A question is modal, and modal has to mean the keyboard too: saving or
        // undoing behind an open dialog is the one thing its own buttons cannot
        // undo. Escape and Enter are the dialog's own and never reach here —
        // Escape comes through the viewport's report, Enter presses the focused
        // answer.
        if (_dialogs.IsAsking)
            return;

        ShellShortcut shortcut = ShellShortcuts.Resolve(
            key.Keycode,
            ShellShortcuts.IsCommand(key.IsCommandOrControlPressed(), key.CtrlPressed),
            key.ShiftPressed);

        switch (shortcut)
        {
            case ShellShortcut.Undo:
                UndoRequested?.Invoke();
                break;

            case ShellShortcut.Redo:
                RedoRequested?.Invoke();
                break;

            case ShellShortcut.New:
                NewRequested?.Invoke();
                break;

            case ShellShortcut.Open:
                OpenRequested?.Invoke();
                break;

            case ShellShortcut.Save:
                SaveRequested?.Invoke();
                break;

            case ShellShortcut.SaveAs:
                SaveAsRequested?.Invoke();
                break;

            case ShellShortcut.Copy:
                CopyRequested?.Invoke();
                break;

            case ShellShortcut.Cut:
                CutRequested?.Invoke();
                break;

            case ShellShortcut.Paste:
                PasteRequested?.Invoke();
                break;

            case ShellShortcut.Duplicate:
                DuplicateRequested?.Invoke();
                break;

            case ShellShortcut.Delete:
                DeleteSelectionRequested?.Invoke();
                break;

            case ShellShortcut.Preferences:
                PreferencesRequested?.Invoke();
                break;

            case ShellShortcut.Render:
                RenderRequested?.Invoke();
                break;

            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    public Task<string?> AskPathAsync(FileDialogRequest request) => _dialogs.AskPathAsync(request);

    public Task<ConfirmResult> AskConfirmAsync(ConfirmRequest request) =>
        _dialogs.AskConfirmAsync(request);

    public void ShowError(string title, string message) => _dialogs.ShowError(title, message);

    public void SetWindowTitle(string title) => GetWindow().Title = title;

    public void SetUndoState(bool canUndo, bool canRedo)
    {
        _undo.Disabled = !canUndo;
        _redo.Disabled = !canRedo;
    }

    public void SetCreateEnabled(CreateKind kind, bool enabled) =>
        _createEnabledByKindDictionary[kind] = enabled;

    /// <summary>
    /// Kept for the next time the menu is built — the in-window menu makes its
    /// rows on opening — and pushed to the platform's bar at once, whose items
    /// stand there between openings.
    /// </summary>
    public void SetRecoverEnabled(bool enabled)
    {
        _recoverEnabled = enabled;
        _nativeMenu?.SetRecoverEnabled(enabled);
    }

    public void SetDeleteEnabled(bool enabled) => _deleteEnabled = enabled;

    public Func<IReadOnlyList<MaterialChoice>>? MaterialChoices { get; set; }

    public Func<SelectionTarget, bool>? CanReplaceMaterialUses { get; set; }

    /// <summary>
    /// NoSignal: assigning the pressed state emits <c>Toggled</c>, and a button
    /// that answered its own reflection would ask the editor to do again whatever
    /// it had just been told about.
    /// </summary>
    public void SetReferencesVisible(bool visible) =>
        _referencesVisible.SetPressedNoSignal(visible);

    /// <summary>
    /// How fast the chrome moves. The view holds what the setting <em>means</em>
    /// — the multiplier per position — the way it holds what a corner means; the
    /// shell only says which one was picked.
    /// </summary>
    public void SetAnimation(bool enabled, AnimationSpeed speed) =>
        _timing.Set(enabled, speed);

    /// <summary>
    /// Which way round the chrome is drawn. A change of <em>theme</em> happens
    /// behind <see cref="ThemeVeil"/>; anything else is applied outright.
    /// </summary>
    /// <remarks>
    /// The accent is deliberately not covered. It is one colour moving, and a
    /// cover heavier than the change it hides would read as the editor
    /// hesitating over it — where a theme is every stylebox and every icon at
    /// once, which is a seam worth a third of a second to hide. Most calls here
    /// are neither: the display preferences are pushed whole on every write, so
    /// pinning a panel re-applies the look it already has, which the skin
    /// answers by doing nothing.
    /// </remarks>
    public void SetSkin(EditorTheme theme, string accent)
    {
        Color accentColor = EditorSkin.ReadAccent(accent);

        // The first application is the launch: the stored theme arriving over
        // the built-in default, before anything worth hiding is on screen. The
        // veil is a cover over a repaint the user is looking at, and a user
        // whose window has just opened is looking at nothing — found because a
        // stored light theme played the whole veil over every fresh launch.
        if (!_skinApplied || theme == _skin.Kind)
        {
            _skinApplied = true;

            _skin.Apply(theme, accentColor);

            return;
        }

        _veil.Cover(() => _skin.Apply(theme, accentColor));
    }

    /// <summary>
    /// Swapped without the veil, deliberately: the veil covers a repaint that
    /// leaves a seam — hundreds of styleboxes and every icon rewritten one after
    /// another — and this is one shader changing on one material, so every pane in
    /// the window turns on the same frame.
    /// </summary>
    public void SetGlassPanels(bool frosted) => GlassPanelMaterial.SetFrosted(frosted);

    /// <summary>
    /// Moves the navigation cube to one of the viewport's four corners. Anchors
    /// rather than a computed rect, so it stays put through a window resize with
    /// nobody recomputing anything — the same trick <see cref="FillBetweenDocks"/>
    /// uses one level up.
    /// </summary>
    public void SetViewCubeCorner(ViewCubeCorner corner) =>
        _viewCube.SetAnchorsAndOffsetsPreset(
            ToPreset(corner),
            Control.LayoutPresetMode.KeepSize,
            ViewCubeInset);

    private static Control.LayoutPreset ToPreset(ViewCubeCorner corner) => corner switch
    {
        ViewCubeCorner.TopLeft => Control.LayoutPreset.TopLeft,
        ViewCubeCorner.BottomRight => Control.LayoutPreset.BottomRight,
        ViewCubeCorner.BottomLeft => Control.LayoutPreset.BottomLeft,

        // Including anything a damaged preferences file made up: a display setting
        // must not be able to leave the widget nowhere.
        _ => Control.LayoutPreset.TopRight
    };

    public void Close() => GetTree().Quit();

    public void SetPanelWidths(PanelWidths widths)
    {
        // Left alone while a pull is in flight: the stored pair arriving back
        // through the preferences' own Changed is this very gesture's earlier
        // frames, and a live drag re-fed its own output would be the ruler bug
        // one subsystem over.
        if (_resize is not null)
            return;

        _leftColumn.SetBodyWidth(PanelResize.ClampWidth(widths.Left));
        _rightColumn.SetBodyWidth(PanelResize.ClampWidth(widths.Right));
    }

    public void SetPanelLayout(IReadOnlyList<PanelPlacement> placements)
    {
        _panelPlacements = placements.ToList();

        // A panel that just got pinned is no longer anybody's temporary.
        if (_openTemp is { } open && FindPlacement(open.Kind).Pinned)
            _openTemp = null;

        ApplyPanelDistribution();
    }

    /// <summary>
    /// Whether a window point is claimed by chrome laid over the viewport. The
    /// scene's polled affordances ask through this; it is the view cube's
    /// hard-coded exception, generalized.
    /// </summary>
    /// <remarks>
    /// The columns answer for themselves, because what stands over the scene is
    /// a panel and a panel belongs to a column even while it is being carried
    /// across the window. The strip the viewport occupies is already inset by
    /// the columns' own widths, so nothing here has to say anything about those.
    /// </remarks>
    public bool IsPointOverChrome(Vector2 point) =>
        _leftColumn.CoversPoint(point) || _rightColumn.CoversPoint(point);

    private void CreatePanel(ChromePanel panel)
    {
        _panelsByKindDictionary[panel.Kind] = panel;

        panel.HeaderPressed += () => OnPanelHeaderPressed(panel.Kind);
        panel.PinPressed += () => OnPanelPinPressed(panel.Kind);

        // Every hide path walks the keyboard home — the solver's auto-fold and
        // the rail transition included, which no explicit gesture covers.
        //
        // Deferred, and that is load-bearing: this one is raised from inside
        // the column's own layout pass, while it is walking its slots. Grabbing
        // focus there makes the viewport claim it, which closes the temporary
        // panel, which rebuilds the very list being walked. Same rule the panel
        // seam already keeps for row events — what a handler does must never be
        // constrained by which dispatch delivered it.
        panel.FocusEvicted += () => _surface.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void ApplyPanelDistribution()
    {
        DistributeSide(PanelSide.Left, _leftColumn);
        DistributeSide(PanelSide.Right, _rightColumn);
        ApplyStripInsets();
    }

    private void DistributeSide(PanelSide side, EdgeColumn column)
    {
        var entries = _panelPlacements
            .Where(placement => placement.Side == side
                && _panelsByKindDictionary.ContainsKey(placement.Kind))
            .OrderBy(placement => placement.Pinned ? 0 : 1)
            .ThenBy(placement => placement.Order)
            .Select(placement => (_panelsByKindDictionary[placement.Kind], placement))
            .ToList();

        PanelKind? openHere =
            _openTemp is { } open && FindPlacement(open.Kind).Side == side
                ? open.Kind
                : null;

        column.SetPanels(entries, openHere);
    }

    /// <summary>
    /// A header press folds a pinned panel. On an unpinned one the header is
    /// the tab — the same surface, shrunk to its glyph — so the one control
    /// carries both halves of the gesture: it grows the panel, and it runs that
    /// backwards.
    /// </summary>
    private void OnPanelHeaderPressed(PanelKind kind)
    {
        // A release that ends a drag lands on the header it started from, and
        // the engine reads that as a press. It was a drag; it is not also a
        // click — and it stays answered through an Escape that cancelled the
        // drag, because the release still arrives after it.
        if (_pressAnswered)
            return;

        PanelPlacement placement = FindPlacement(kind);

        if (!placement.Pinned)
        {
            ToggleTempPanel(kind);

            return;
        }

        // An auto-folded panel is displayed folded while its stored flag says
        // open, and the click means "open": toggling the flag would persist the
        // very fold being undone. What can free the space is dismissing a
        // temporary panel — so that is tried first, and only when there is none
        // does the click fall through to the ordinary toggle. It used to stop
        // here either way, which in a window simply too short for the stack was
        // a header that answered nothing at all.
        if (_panelsByKindDictionary[kind].ShownAutoFolded && _openTemp is not null)
        {
            CloseTempPanel();

            return;
        }

        if (!placement.Folded)
            HandOffFocus(kind);

        RaiseLayoutEdited(placement with { Folded = !placement.Folded });
    }

    /// <summary>
    /// A collapsed panel's header was pressed: it grows and <b>stays</b>.
    /// Hovering shows it for as long as the pointer is around; a click is the
    /// same thing asked for on purpose, so nothing but a dismissal puts it
    /// back — and pressing again on a panel already held is that dismissal.
    /// </summary>
    /// <remarks>
    /// The pointer is still on the control that answered, so hover is held off
    /// this panel until the hand leaves and returns: otherwise a dismissal
    /// hands the panel straight back as a glance and the click reads as having
    /// done nothing.
    /// </remarks>
    private void ToggleTempPanel(PanelKind kind)
    {
        bool alreadyHeld = _openTemp is { Held: true } held && held.Kind == kind;

        CloseTempPanel();

        if (alreadyHeld)
            return;

        _openTemp = (kind, Held: true);
        _hoverIntent.SuppressUntilPointerLeaves(kind);
        ApplyPanelDistribution();
    }

    /// <summary>
    /// The pin was pressed. Pinning puts the panel in the column's stack;
    /// unpinning takes it out of the stack and <b>leaves it open</b>.
    /// </summary>
    /// <remarks>
    /// Unpinning means "stop reserving room for this", not "put it away", and
    /// the pin is inside the panel's own header — so the hand is on the panel by
    /// construction and hiding it under the pointer is the one reading the
    /// gesture cannot have. It shipped doing exactly that: the latch that keeps
    /// hover off a panel the user has just dismissed was applied here too, so
    /// the panel collapsed into its tab and the hover that would have reopened
    /// it was refused.
    /// <para>
    /// What it becomes is what a click on its tab would have made it — open and
    /// held, until something dismisses it. Held rather than a glance because the
    /// panel <em>moves</em> as it unpins: it leaves the pinned lane for the run's,
    /// so the pointer that was on its header may not be on it a frame later, and
    /// a glance would end the moment it landed.
    /// </para>
    /// <para>
    /// And nothing hands the keyboard home on this path any more. That is there
    /// for a body about to be hidden; this one is not hidden, so unpinning while
    /// typing in a field no longer throws the caret out of it.
    /// </para>
    /// </remarks>
    private void OnPanelPinPressed(PanelKind kind)
    {
        // The pin sits inside the header a drag is picked up by, so the same
        // rule: a press already answered by a drag is not also a click. Stated
        // on both handlers rather than once, because the release is delivered
        // to whichever control the engine finds under it.
        if (_pressAnswered)
            return;

        PanelPlacement placement = FindPlacement(kind);

        // Through the one close, so the pair goes together and the latch is
        // applied: clearing the kind here by hand is what stranded "held".
        if (_openTemp?.Kind == kind)
            CloseTempPanel();

        _hoverIntent.SuppressUntilPointerLeaves(kind);

        // Unpinning is "stop reserving room for this", not "put it away", so the
        // panel goes on standing where it stood — as an ordinary glance, which
        // is the whole of what the hand expects next: it stays while the pointer
        // is on it and folds into its tab once the pointer leaves.
        //
        // A glance rather than held, which is what this was for one revision.
        // Held was the right answer while unpinning *moved* the panel out from
        // under the hand — a glance would have ended the moment it landed — and
        // is the wrong answer now that PanelPlacementEdit.Cross lands it at the
        // boundary it was already standing on. Held meant the click had no
        // undoing but another click.
        if (placement.Pinned)
            _openTemp = (kind, Held: false);

        SetPanelLayout(PanelPlacementEdit.Cross(_panelPlacements, kind));
        PanelLayoutEdited?.Invoke(_panelPlacements);
    }

    /// <summary>
    /// Applied locally first, so the click answers even if nobody is listening;
    /// the presenter persists the same list and pushes the stored truth back
    /// through <see cref="SetPanelLayout"/>, which re-applies it unchanged.
    /// </summary>
    private void RaiseLayoutEdited(PanelPlacement changed)
    {
        var updated = _panelPlacements
            .Select(placement => placement.Kind == changed.Kind ? changed : placement)
            .ToList();

        SetPanelLayout(updated);
        PanelLayoutEdited?.Invoke(_panelPlacements);
    }

    /// <summary>
    /// Escape, answered before anything else is offered the key.
    /// </summary>
    /// <remarks>
    /// It used to hang off the viewport's own unhandled-key route, which runs
    /// after GUI input — so a focused <see cref="SpinBox"/>'s line edit consumed
    /// the key first and every card in this chain was unreachable from the one
    /// place a hand is most likely to be: a field it has just typed into.
    /// <para>
    /// The fix this project wrote down for years was <c>_ShortcutInput</c>, on the
    /// strength of its name. <b>It is not before GUI input.</b> Godot offers an
    /// event to <c>_input</c>, then to the GUI, and only what the GUI left alone
    /// reaches shortcut and unhandled input — measured here, with a focused number
    /// field: <c>_Input</c> saw the press and the release, <c>_ShortcutInput</c>
    /// saw the release alone. So this is <c>_Input</c>, which is the only route
    /// that runs first, and the note is left because the wrong answer is the one
    /// the name suggests.
    /// </para>
    /// <para>
    /// The banner's own <c>_Input</c> runs before this one — a child is offered an
    /// event before its parent — so an agent holding the editor still swallows the
    /// user's Escape exactly as it swallows everything else.
    /// </para>
    /// </remarks>
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            return;

        // The keyboard goes back to the scene whatever was dismissed, which is
        // what the viewport's own route did before this and what makes Escape mean
        // "put this away and let me get on" rather than only "put this away".
        _surface.GrabFocus();

        AnswerEscape();

        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// The front-most surface answers first — a question over the settings card,
    /// the settings card over the chrome — and only otherwise does a temporary
    /// panel retract.
    /// </summary>
    private void AnswerEscape()
    {
        // A menu is summoned over everything and answered at once; it is the most
        // transient thing on screen, so it is the first thing Escape puts away.
        if (_materialPickMenu.TryDismiss() || _hierarchyMenu.TryDismiss() || _fileMenu.TryDismiss())
            return;

        if (_dialogs.TryDismiss())
            return;

        if (_render.TryDismiss())
            return;

        if (_settings.TryDismiss())
            return;

        // A gesture held in the scene — a stroke, an extrude pull, a handle drag —
        // owns the viewport for as long as it is held, which makes it the
        // front-most thing in the window; the cards above stand over the window,
        // and a card can be up while a button is held (the platform's own menu
        // bar answers a chord the surface would have swallowed). Asked here at
        // all because this route answers the key before GUI routing: the scene's
        // own input loops never see it, and each of the three used to answer
        // Escape in a loop the key had stopped reaching.
        if (Scene.TryAnswerEscape())
            return;

        // A tab in the air goes back where it came from, and nothing is stored:
        // the placements were only ever previewed while it was held.
        if (_drag.Held is not null)
        {
            CancelDrag();

            return;
        }

        if (_resize is not null)
        {
            CancelResize();

            return;
        }

        CloseTempPanel();
    }

    /// <summary>
    /// The one way a temporary panel is dismissed, whoever asked — Escape, a
    /// press on the scene, the viewport taking the keyboard, another tab, the
    /// header itself.
    /// </summary>
    /// <remarks>
    /// Every one of those is a decision taken with the pointer possibly still
    /// sitting on the tab, so the latch belongs here rather than beside the one
    /// gesture that first needed it: without it Escape closed the panel and the
    /// dwell handed it straight back, which reads as Escape doing nothing. The
    /// hand has to move for it to come back.
    /// </remarks>
    private void CloseTempPanel()
    {
        if (_openTemp is not { } open)
            return;

        // Cleared first, then the keyboard walked home — never the other way
        // round. Handing focus to the viewport makes it raise the very signal
        // this method is subscribed to, so with the state still standing the
        // close re-entered itself and did all its work twice.
        _openTemp = null;
        _hoverIntent.SuppressUntilPointerLeaves(open.Kind);
        HandOffFocus(open.Kind);
        ApplyPanelDistribution();
    }

    /// <summary>
    /// A hidden control keeps the keyboard and hands it to nobody — measured
    /// twice in this codebase — so whatever closes a panel walks the keyboard
    /// home to the viewport itself.
    /// </summary>
    private void HandOffFocus(PanelKind kind)
    {
        if (_panelsByKindDictionary[kind].HoldsFocus())
            _surface.GrabFocus();
    }

    private PanelPlacement FindPlacement(PanelKind kind)
    {
        foreach (PanelPlacement placement in _panelPlacements)
        {
            if (placement.Kind == kind)
                return placement;
        }

        return new PanelPlacement(kind, PanelSide.Left, 0, Pinned: true, Folded: false);
    }

    public void SetControlBanner(ControlBannerState state)
    {
        // Control changing hands breaks any gesture in flight. Without this an
        // agent that presses a panel and then loses the editor has its
        // half-finished drag *committed* on the next frame, because the user's
        // own button is up and that reads as a release; and the button state
        // carried across would let the other party's first frame synthesize a
        // press it never made.
        if (_bannerKind != state.Kind)
        {
            CancelDrag();
            CancelResize();
            _buttonWasDown = false;
            _pressAnswered = false;
        }

        _bannerKind = state.Kind;
        _controlBanner.Set(state);
    }

    /// <summary>
    /// Anchors a control to the strip the columns leave between them, and
    /// registers it so the strip follows a column collapsing to a rail or
    /// growing back. One list, one method — the surface, the cube's area and
    /// the readout's area cannot drift apart.
    /// </summary>
    private T FillBetweenDocks<T>(T control) where T : Control
    {
        control.AnchorRight = 1f;
        control.AnchorBottom = 1f;
        control.OffsetLeft = EdgeColumn.WideWidth;
        control.OffsetRight = -EdgeColumn.WideWidth;
        _stripControlsList.Add(control);

        return control;
    }

    private void ApplyStripInsets()
    {
        foreach (Control control in _stripControlsList)
        {
            control.OffsetLeft = _leftColumn.CurrentWidth;
            control.OffsetRight = -_rightColumn.CurrentWidth;
        }
    }

    private static Button IconButton(
        Theme theme,
        string name,
        string icon,
        string tooltip,
        Action onPressed)
    {
        Button button = Ui.Button(string.Empty, onPressed).Named(name);

        StyleIconButton(theme, button, icon, tooltip);

        return button;
    }

    /// <summary>
    /// The words move into the tooltip and the glyph does the talking — the
    /// theme is at hand in the constructor, which is why icons are fetched
    /// here rather than lazily the way the panels do it.
    /// </summary>
    private static void StyleIconButton(Theme theme, Button button, string icon, string tooltip)
    {
        button.Text = string.Empty;
        button.Icon = theme.GetIcon(icon, EditorThemeFactory.SemanticType);
        button.TooltipText = tooltip;
        button.ThemeTypeVariation = "ToolButton";
    }

    private static Control Spacer() => new()
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore
    };

    private void OnFilesDropped(string[] paths) => FilesDropped?.Invoke(paths);

    private ShellMenuActions BuildMenuActions() => new(
        () => NewRequested?.Invoke(),
        () => OpenRequested?.Invoke(),
        () => RecoverRequested?.Invoke(),
        () => SaveRequested?.Invoke(),
        () => SaveAsRequested?.Invoke(),
        () => CreateRequested?.Invoke(CreateKind.Reference),
        () => ImportRequested?.Invoke(),
        () => ExportRequested?.Invoke(),
        () => RenderRequested?.Invoke(),
        () => PreferencesRequested?.Invoke(),
        () => UndoRequested?.Invoke(),
        () => RedoRequested?.Invoke(),
        () => CutRequested?.Invoke(),
        () => CopyRequested?.Invoke(),
        () => PasteRequested?.Invoke(),
        () => DuplicateRequested?.Invoke(),
        () => DeleteSelectionRequested?.Invoke());

    /// <summary>
    /// The File button. Its menu is a glass card of ordinary buttons, not the
    /// engine's <see cref="PopupMenu"/> — the same three reasons the hierarchy's
    /// menu was brought across, and one more that is this menu's own: it stands
    /// over the chrome, and an opaque slab there is the one surface in the window
    /// that would not be made of what everything else is made of.
    /// </summary>
    private Button CreateFileMenu(Theme theme)
    {
        Button menu = IconButton(
            theme,
            "FileMenu",
            EditorIconCatalog.FileMenu,
            "File — new, open, save as, export",
            OpenFileMenu);

        return menu;
    }

    /// <summary>
    /// Opened under the button rather than at the pointer, which is what every
    /// menu hung off a control does and what tells it apart from a right-click
    /// menu that belongs to the row it was summoned on.
    /// </summary>
    private void OpenFileMenu()
    {
        _fileMenu.Open(
            _fileMenuButton!.GlobalPosition + new Vector2(0f, _fileMenuButton.Size.Y + 2f),
            BuildFileMenuItems());
    }

    private IReadOnlyList<ContextMenuItem> BuildFileMenuItems()
    {
        // The platform's own spelling, and the same question as which modifier
        // the chords carry — so it is asked once, of the engine, here.
        bool commandIsMeta = OS.GetName() == "macOS";

        return new List<ContextMenuItem>
        {
            FileItem("New", FileMenuItem.New, EditorIconCatalog.NewFile,
                ShellShortcut.New, commandIsMeta),
            FileItem("Open…", FileMenuItem.Open, EditorIconCatalog.Open,
                ShellShortcut.Open, commandIsMeta),

            // Live only while a snapshot is waiting: a row that opens a card
            // saying "nothing to recover" is a question with nothing behind it.
            FileItem("Recover unsaved work…", FileMenuItem.Recover, EditorIconCatalog.Open,
                ShellShortcut.None, commandIsMeta, _recoverEnabled),
            ContextMenuItem.Separator(),
            FileItem("Save", FileMenuItem.Save, EditorIconCatalog.Save,
                ShellShortcut.Save, commandIsMeta),
            FileItem("Save As…", FileMenuItem.SaveAs, EditorIconCatalog.Save,
                ShellShortcut.SaveAs, commandIsMeta),
            ContextMenuItem.Separator(),
            FileItem("Import reference…", FileMenuItem.ImportReference,
                EditorIconCatalog.CreateReference, ShellShortcut.None, commandIsMeta),
            FileItem("Import…", FileMenuItem.Import, EditorIconCatalog.Open,
                ShellShortcut.None, commandIsMeta),
            FileItem("Export…", FileMenuItem.Export, EditorIconCatalog.Export,
                ShellShortcut.None, commandIsMeta),
            FileItem("Render…", FileMenuItem.Render, EditorIconCatalog.Render,
                ShellShortcut.Render, commandIsMeta),
            ContextMenuItem.Separator(),

            // Its home is the Edit or the application menu, neither of which
            // exists while the File menu is a button in a dock. See the ledger.
            FileItem("Preferences…", FileMenuItem.Preferences, EditorIconCatalog.Preferences,
                ShellShortcut.Preferences, commandIsMeta)
        };
    }

    private static ContextMenuItem FileItem(
        string label,
        FileMenuItem id,
        string icon,
        ShellShortcut shortcut,
        bool commandIsMeta,
        bool enabled = true) =>
        new(
            (int)id,
            label,
            $"Menu{id}",
            icon,
            enabled,
            ShellShortcuts.Describe(shortcut, commandIsMeta));

    private void OnFileMenuPressed(int id)
    {
        var item = (FileMenuItem)id;

        switch (item)
        {
            case FileMenuItem.New:
                NewRequested?.Invoke();
                break;

            case FileMenuItem.Open:
                OpenRequested?.Invoke();
                break;

            case FileMenuItem.Recover:
                RecoverRequested?.Invoke();
                break;

            case FileMenuItem.Save:
                SaveRequested?.Invoke();
                break;

            case FileMenuItem.SaveAs:
                SaveAsRequested?.Invoke();
                break;

            case FileMenuItem.ImportReference:
                // The same request the tree's own menu makes, so where a reference
                // lands is decided in one place: under whatever is selected.
                CreateRequested?.Invoke(CreateKind.Reference);
                break;

            case FileMenuItem.Import:
                ImportRequested?.Invoke();
                break;

            case FileMenuItem.Export:
                ExportRequested?.Invoke();
                break;

            case FileMenuItem.Render:
                RenderRequested?.Invoke();
                break;

            case FileMenuItem.Preferences:
                PreferencesRequested?.Invoke();
                break;

            // Named rather than ignored. A row added to the menu and not
            // dispatched here would compile, draw, and do nothing when pressed —
            // the ledger's "a list of refusals rots as the enum grows", one enum
            // further on. This is what makes the next one fail at once.
            default:
                throw new ArgumentOutOfRangeException(nameof(item), item, null);
        }
    }

    /// <summary>
    /// The hierarchy's right-click menu: the same actions as the create row
    /// and the Delete button, enabled exactly as they stand — the buttons'
    /// own states are the single source, freshly pushed by the presenter for
    /// the row the right press just selected, which is why the view reports
    /// the selection before the menu request.
    /// </summary>
    private void OnHierarchyContextMenu(HierarchyMenuRequest request)
    {
        var items = new List<ContextMenuItem>
            {
                CreateItem("New group", HierarchyMenuItem.CreateGroup, CreateKind.Group),
                CreateItem("New voxel model", HierarchyMenuItem.CreateModel, CreateKind.VoxelModel),
                CreateItem("New camera", HierarchyMenuItem.CreateCamera, CreateKind.Camera),
                CreateItem(
                    "Import reference…",
                    HierarchyMenuItem.ImportReference,
                    CreateKind.Reference),
                CreateItem("New scene", HierarchyMenuItem.CreateScene, CreateKind.Scene),
                CreateItem("New material", HierarchyMenuItem.CreateMaterial, CreateKind.Material),
                CreateItem(
                    "New instance of prefab",
                    HierarchyMenuItem.CreateInstance,
                    CreateKind.PrefabInstance),
            };

        // The eye and the bin already sit on the row, and both are duplicated
        // here on purpose: a hover-revealed glyph is an accelerator, and a menu
        // that omitted what the row can do would be the shorter list of the two
        // places one looks.
        items.Add(ContextMenuItem.Separator());

        // A material row's own action, beside the row's eye and bin: the second
        // step — which material to give the cells — is a card of its own, opened
        // where this one stood, so the choice is a named control an agent can aim
        // at rather than a dialog with a list nobody can address.
        if (request.Target.Kind == SelectionKind.Material)
        {
            items.Add(new ContextMenuItem(
                (int)HierarchyMenuItem.ReplaceMaterialUses,
                "Replace uses…",
                "MenuReplaceMaterial",
                EditorIconCatalog.Material,
                CanReplaceMaterialUses?.Invoke(request.Target) == true));
        }

        if (request.CanToggleVisibility)
        {
            items.Add(new ContextMenuItem(
                (int)HierarchyMenuItem.ToggleVisibility,
                request.Hidden ? "Show" : "Hide",
                "MenuVisibility",
                request.Hidden ? EditorIconCatalog.EyeOpen : EditorIconCatalog.EyeClosed,
                Enabled: true));
        }

        items.Add(new ContextMenuItem(
            (int)HierarchyMenuItem.Delete,
            "Delete",
            "MenuDelete",
            EditorIconCatalog.Trash,
            _deleteEnabled));

        _menuTarget = request.Target;
        _menuPosition = request.Position;
        _hierarchyMenu.Open(request.Position, items);
    }

    /// <summary>
    /// The picker: every material the presenter offered but the row's own, in
    /// the order the project lists them. Opened where the first menu stood, since
    /// that is where the hand already is.
    /// </summary>
    private void OpenMaterialPicker()
    {
        IReadOnlyList<MaterialChoice> choices =
            MaterialChoices?.Invoke() ?? Array.Empty<MaterialChoice>();

        // Named before anything is dropped, so a name shared with the row's own
        // material still counts as shared — what makes two rows ambiguous is the
        // registry, not what this card happens to be showing.
        IReadOnlyList<string> labels = MaterialChoiceLabels.Compose(choices);

        _offeredChoicesList = new List<MaterialChoice>();
        var items = new List<ContextMenuItem>();

        for (var i = 0; i < choices.Count; i++)
        {
            if (choices[i].Id == _menuTarget.Id)
                continue;

            items.Add(new ContextMenuItem(
                _offeredChoicesList.Count,
                labels[i],
                $"ReplaceWith{_offeredChoicesList.Count}",
                string.Empty,
                Enabled: true));
            _offeredChoicesList.Add(choices[i]);
        }

        if (items.Count == 0)
            return;

        _materialPickMenu.Open(_menuPosition, items);
    }

    private void OnMaterialPickChosen(int id)
    {
        if (id < 0 || id >= _offeredChoicesList.Count)
            return;

        ReplaceMaterialUsesRequested?.Invoke(_menuTarget.Id, _offeredChoicesList[id].Id);
    }

    private ContextMenuItem CreateItem(string label, HierarchyMenuItem id, CreateKind kind) =>
        new(
            (int)id,
            label,
            $"Menu{kind}",
            EditorIconCatalog.ForCreate(kind),
            IsCreateEnabled(kind));

    private bool IsCreateEnabled(CreateKind kind) =>
        _createEnabledByKindDictionary.TryGetValue(kind, out bool enabled) && enabled;

    private void OnHierarchyMenuPressed(int id)
    {
        switch ((HierarchyMenuItem)id)
        {
            case HierarchyMenuItem.CreateGroup:
                CreateRequested?.Invoke(CreateKind.Group);
                break;

            case HierarchyMenuItem.CreateModel:
                CreateRequested?.Invoke(CreateKind.VoxelModel);
                break;

            case HierarchyMenuItem.CreateCamera:
                CreateRequested?.Invoke(CreateKind.Camera);
                break;

            case HierarchyMenuItem.ImportReference:
                CreateRequested?.Invoke(CreateKind.Reference);
                break;

            case HierarchyMenuItem.CreateScene:
                CreateRequested?.Invoke(CreateKind.Scene);
                break;

            case HierarchyMenuItem.CreateMaterial:
                CreateRequested?.Invoke(CreateKind.Material);
                break;

            case HierarchyMenuItem.CreateInstance:
                CreateRequested?.Invoke(CreateKind.PrefabInstance);
                break;

            case HierarchyMenuItem.ToggleVisibility:
                // Straight to the panel's own event, which is the eye's: one
                // route in, so the menu and the glyph cannot come to mean
                // different things.
                _hierarchy.RaiseVisibilityToggled(_menuTarget);
                break;

            case HierarchyMenuItem.Delete:
                DeleteSelectionRequested?.Invoke();
                break;

            case HierarchyMenuItem.ReplaceMaterialUses:
                OpenMaterialPicker();
                break;

            // Named rather than ignored, for the reason the File menu's own default
            // gives: a row added and not dispatched would compile, draw, and do
            // nothing when pressed.
            default:
                throw new ArgumentOutOfRangeException(nameof(id), id, null);
        }
    }

    private void OnBannerActionPressed()
    {
        // The banner shows one button at a time; which one depends on the phase.
        // Stop while the agent holds control — working or gone quiet — Cancel while
        // it is only asking.
        if (_bannerKind is ControlBannerKind.AgentWorking or ControlBannerKind.AgentSilent)
            StopControlRequested?.Invoke();
        else
            CancelControlRequested?.Invoke();
    }

    /// <summary>Ids the hierarchy's context menu carries; only its popup sees them.</summary>
    private enum HierarchyMenuItem
    {
        CreateGroup,
        CreateModel,
        CreateCamera,
        ImportReference,
        CreateScene,
        CreateMaterial,
        CreateInstance,
        ToggleVisibility,
        Delete,
        ReplaceMaterialUses
    }

    /// <summary>Ids the popup carries; the menu is the only thing that sees them.</summary>
    private enum FileMenuItem
    {
        New,
        Open,
        Recover,
        Save,
        SaveAs,
        ImportReference,
        Import,
        Export,
        Render,
        Preferences
    }
}
