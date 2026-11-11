using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <inheritdoc cref="IHierarchyView"/>
internal sealed partial class HierarchyView : Tree, IHierarchyView
{
    /// <summary>Side of a material's colour chip, in pixels.</summary>
    private const int SwatchSize = 12;

    /// <summary>
    /// What the engine carries between the drag's start and its drop. Deliberately
    /// only a marker: the row itself is remembered here, because identity in this
    /// document is a <see cref="Guid"/> and a Guid is not Variant-compatible —
    /// the same wall that keeps the panels' events out of Godot's signals.
    /// </summary>
    private const string DragMarker = "voxelcad_hierarchy_row";

    /// <summary>Godot's answer for "the pointer is not over any row".</summary>
    private const int NoDropSection = -100;

    /// <summary>
    /// Which of a row's cell buttons the eye is. Button ids are per row, so one
    /// constant covers the whole tree.
    /// </summary>
    private const int VisibilityButtonId = 1;

    /// <summary>
    /// The bin. Godot draws a cell's buttons right to left in reverse order, so
    /// the bin sits right of the eye by being <b>added after</b> it — which is the
    /// presenter's call order, not a number here.
    /// </summary>
    private const int DeleteButtonId = 2;

    /// <summary>
    /// How strongly an offered glyph is drawn — the open eye, the bin. Fainter
    /// than the crossed eye on purpose: an offer the pointer brought with it is
    /// not something the document is saying.
    /// </summary>
    private const float OfferedAlpha = 0.55f;

    /// <summary>
    /// What a glyph nobody is pointing at is drawn in. The button keeps its place
    /// in the cell either way, so nothing in the row moves when the pointer
    /// arrives — and it stays clickable, which costs nothing: being over it is
    /// what makes it visible.
    /// </summary>
    private static readonly Color Transparent = new(1f, 1f, 1f, 0f);

    private readonly Dictionary<SelectionTarget, TreeItem> _itemsByTargetDictionary = new();
    private readonly Dictionary<TreeItem, SelectionTarget> _targetsByItemDictionary = new();

    /// <summary>
    /// The row being worked on, remembered rather than only written — see
    /// <see cref="SetItemEmphasized"/> for why a colour on a row is the one
    /// theme lookup a repaint cannot reach.
    /// </summary>
    private SelectionTarget? _emphasized;

    /// <summary>
    /// The rows that carry an eye, and what it says. Membership is the flag: a row
    /// the presenter never handed a visibility to has no eye and gets none here.
    /// </summary>
    private readonly Dictionary<TreeItem, RowVisibility> _visibilityByItemDictionary = new();

    /// <summary>
    /// The rows that carry a bin. Whether it is live is asked per hover, so this
    /// is only about which rows have one at all.
    /// </summary>
    private readonly HashSet<TreeItem> _deletableItemsHashSet = new();

    private Action<SelectionTarget>? _itemActivated;
    private bool _reporting = true;
    private SelectionTarget _dragging;
    private TreeItem? _hoveredItem;

    /// <summary>
    /// The row a Shift range is measured from: the one the presenter last called
    /// active. Held rather than read back from <see cref="Tree.GetSelected"/>
    /// because the engine's own focused row moves with a plain click before this
    /// panel has been told what the selection became.
    /// </summary>
    private TreeItem? _activeItem;

    /// <summary>
    /// Whether a report of the engine's own selection is already on its way. A
    /// gesture that changes several rows emits once per row, and the answer is the
    /// whole set either way, so the first emission books the report and the rest
    /// ride on it.
    /// </summary>
    private bool _reportPending;

    public HierarchyView()
    {
        Columns = 1;
        HideRoot = false;
        AllowReselect = true;

        // It lives on a glass panel, and the pane should read through the rows.
        ThemeTypeVariation = "GlassTree";

        // Multi so that several rows can be drawn selected. What the modifiers
        // mean is not left to the engine — see TryModifierSelect — but a plain
        // click still is, along with the arrows, the drag and the cell buttons.
        SelectMode = SelectModeEnum.Multi;
        this.Expand().ContainsArrowFocus();

        AddChild(new FocusRingOverlay());

        // Both, because the engine tells the two halves of one story on different
        // signals: MultiSelected says a row's selected flag changed — what a click
        // does — and CellSelected says the cursor moved onto a row, which is all an
        // arrow key does under Multi. Bound to one answer, coalesced per frame.
        MultiSelected += OnMultiSelected;
        CellSelected += OnCellSelected;
        ItemActivated += OnItemActivated;
        ButtonClicked += OnButtonClicked;
    }

    public event Action<HierarchySelection>? SelectionRequested;

    public event Action<ToolKind>? ToolRequested;

    public event Action<HierarchyDrop>? ItemDropped;

    public event Action<SelectionTarget>? VisibilityToggled;

    public event Action<SelectionTarget>? DeleteRequested;

    /// <summary>
    /// A right press asked for a row's context menu, at this canvas position
    /// and with what the row itself offers. The row is already on its way to
    /// being the selection — reported first, so whoever builds the menu reads
    /// states that describe this row. Not on <see cref="IHierarchyView"/>: the
    /// menu is the shell's furniture, built from the enabled states the
    /// presenter already pushes.
    /// </summary>
    /// <remarks>
    /// The visibility half comes from here rather than from the presenter
    /// because it is the row's own drawn state — the same answer the eye
    /// button wears — and a menu that disagreed with the eye beside it would
    /// be two truths about one element.
    /// </remarks>
    public event Action<HierarchyMenuRequest>? ContextMenuRequested;

    /// <summary>
    /// Asks for the same thing the row's eye asks for. The menu duplicates the
    /// glyph, so it goes through the glyph's own event rather than a second
    /// path to the presenter.
    /// </summary>
    public void RaiseVisibilityToggled(SelectionTarget target) =>
        VisibilityToggled?.Invoke(target);

    /// <inheritdoc/>
    public Func<HierarchyDrop, bool>? CanDrop { get; set; }

    /// <inheritdoc/>
    public Func<SelectionTarget, bool>? CanDrag { get; set; }

    /// <inheritdoc/>
    public Func<SelectionTarget, bool>? CanDelete { get; set; }

    /// <summary>
    /// Implemented explicitly because <see cref="Tree"/> already has a signal of
    /// this name. Both are wanted — the engine's says a row was double-clicked,
    /// this one says which — and keeping them apart means neither can be reached
    /// by mistake.
    /// </summary>
    event Action<SelectionTarget>? IHierarchyView.ItemActivated
    {
        add => _itemActivated += value;
        remove => _itemActivated -= value;
    }

    /// <summary>
    /// The pointer left the panel, so no row is under it and every open eye goes
    /// away. This one hangs off the engine's own hover, which it recomputes from
    /// the machine's cursor whatever else is going on — the same resident the view
    /// cube has, and the same cost: a lost highlight, never a changed click.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit)
            SetHovered(null);

        // A row's colour is written once, as the row is built, so a repainted
        // theme cannot reach it — the emphasized row has to be dressed again.
        if ((long)what == NotificationThemeChanged && IsInsideTree() && _emphasized is not null)
            SetItemEmphasized(_emphasized.Value, emphasized: true);
    }

    /// <summary>
    /// Two things arrive here. A tool key is reported, not acted on — the same
    /// bargain the viewport makes with the same keys. What it is for: a row is
    /// picked here and the tool is about to act on it, so switching tools should
    /// not have to be preceded by a click into the scene. And the pointer's own
    /// motion decides which row is offered its eye.
    /// </summary>
    /// <remarks>
    /// A key reaches a control's GUI input only while that control is the focused
    /// one, which is what makes "not while a row is being renamed" structural
    /// rather than a check: Godot's row editor is a <see cref="LineEdit"/> of its
    /// own and holds the keyboard for as long as it is open, so nothing arrives
    /// here meanwhile.
    /// <para>
    /// A key is claimed whether or not anything comes of it, for the reason the
    /// viewport claims its own: an unclaimed key is the engine's to read as
    /// something else, and here that something else is the tree's type-to-search
    /// jumping to a row beginning with the same letter. Motion is the opposite —
    /// it is read and passed on, because the tree needs it for its own drop
    /// indicator and scrollbar.
    /// </para>
    /// <para>
    /// Both are the <b>driving party's</b> input rather than the machine's: while
    /// an agent holds the editor the banner swallows the user's events before GUI
    /// routing, so what reaches this method is whatever is actually driving.
    /// </para>
    /// </remarks>
    public override void _GuiInput(InputEvent @event)
    {
        // Which row the pointer is on decides which eye is offered. Not accepted:
        // the tree wants its own motion for the drop indicator and the scrollbar.
        if (@event is InputEventMouseMotion motion)
        {
            SetHovered(GetItemAtPosition(motion.Position));

            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            if (TryModifierSelect(click))
                AcceptEvent();

            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } rightClick)
        {
            if (TryContextMenu(rightClick))
                AcceptEvent();

            return;
        }

        if (@event is not InputEventKey key)
            return;

        if (!ToolActions.TryResolve(key, out ToolKind tool))
            return;

        // An echo is the OS repeating a key that already switched the tool.
        if (key.Pressed && !key.Echo)
            ToolRequested?.Invoke(tool);

        AcceptEvent();
    }

    /// <summary>
    /// A click carrying a modifier, which this panel answers itself. Everything
    /// else is left to the engine — a plain click, the arrows, the drag, the
    /// expand arrows and the cell buttons all keep working because nothing is
    /// taken away from them.
    /// </summary>
    /// <remarks>
    /// The engine's own <see cref="Tree.SelectModeEnum.Multi"/> would answer both
    /// modifiers, and close to the way this does. It is intercepted anyway, for one
    /// reason: the viewport has no engine to fall back on, so the rule for what a
    /// modifier means has to exist here in any case, and having the tree quietly
    /// use a second copy of it is exactly the drift <see cref="SelectionModifiers"/>
    /// exists to prevent.
    /// <para>
    /// What is reported is the rows the click <em>named</em> and what it asked for,
    /// never the selection it should result in — the panel could work one out from
    /// the rows it draws selected, and then the rule for what a toggle means would
    /// live in two places.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A right press selects the row under it, then asks for its menu — in that
    /// order, both deferred like every row event, and the order is the point:
    /// the selection lands, the presenters refresh what may be created and
    /// deleted here, and only then is the menu built from those states. On
    /// macOS a Ctrl+left arrives here already translated to a right press by
    /// the engine's window, which is exactly the convention wanted.
    /// </summary>
    private bool TryContextMenu(InputEventMouseButton click)
    {
        TreeItem? item = GetItemAtPosition(click.Position);

        if (item == null || !_targetsByItemDictionary.TryGetValue(item, out SelectionTarget target))
            return false;

        Vector2 position = click.GlobalPosition;
        bool hasEye = item.GetButtonById(0, VisibilityButtonId) >= 0;
        bool hidden = _visibilityByItemDictionary.TryGetValue(item, out RowVisibility visibility)
            && visibility == RowVisibility.Hidden;

        DeferredReport.Raise(() =>
            SelectionRequested?.Invoke(
                new HierarchySelection(new[] { target }, target, SelectionIntent.Replace)));
        DeferredReport.Raise(() =>
            ContextMenuRequested?.Invoke(
                new HierarchyMenuRequest(position, target, hasEye, hidden)));

        return true;
    }

    private bool TryModifierSelect(InputEventMouseButton click)
    {
        SelectionIntent intent = SelectionModifiers.Resolve(click);

        if (intent == SelectionIntent.Replace)
            return false;

        TreeItem? item = GetItemAtPosition(click.Position);

        if (item == null || !_targetsByItemDictionary.TryGetValue(item, out SelectionTarget target))
            return false;

        IReadOnlyList<SelectionTarget> targets =
            intent == SelectionIntent.Extend && _activeItem != null
                ? CollectRange(_activeItem, item)
                : new[] { target };

        // Deferred like every row event: answering this refills the tree.
        DeferredReport.Raise(() =>
            SelectionRequested?.Invoke(new HierarchySelection(targets, target, intent)));

        return true;
    }

    /// <summary>
    /// The run of rows from the anchor to the row clicked, in the order they are
    /// drawn — which is what makes a Shift range mean what it means everywhere
    /// else. Collapsed subtrees are stepped over rather than swept up: a range is
    /// what the eye can see between two rows.
    /// </summary>
    /// <remarks>
    /// Walked forwards from each end in turn rather than compared by depth or
    /// index, because "before" in a tree is not a number anybody holds — the
    /// engine's own visible order is the only authority on it.
    /// </remarks>
    private IReadOnlyList<SelectionTarget> CollectRange(TreeItem anchor, TreeItem clicked)
    {
        return WalkTo(anchor, clicked)
            ?? WalkTo(clicked, anchor)
            ?? new List<SelectionTarget> { _targetsByItemDictionary[clicked] };
    }

    /// <summary>
    /// The rows from one to the other going down, or null when the second is not
    /// below the first at all.
    /// </summary>
    private List<SelectionTarget>? WalkTo(TreeItem from, TreeItem to)
    {
        var run = new List<SelectionTarget>();

        for (TreeItem? item = from; item != null; item = item.GetNextVisible())
        {
            if (_targetsByItemDictionary.TryGetValue(item, out SelectionTarget target))
                run.Add(target);

            if (ReferenceEquals(item, to))
                return run;
        }

        return null;
    }

    /// <summary>
    /// Starts a drag from the row under the pointer. The row travels in a field
    /// rather than in the returned data — see <see cref="DragMarker"/> — and the
    /// preview is the row's own text, so what is being carried is legible while it
    /// is in the air.
    /// </summary>
    public override Variant _GetDragData(Vector2 atPosition)
    {
        TreeItem? item = GetItemAtPosition(atPosition);

        // An empty answer is how a drag never starts: a row the presenter will
        // not let go anywhere should not ride the pointer to a refusal.
        if (item == null
            || !_targetsByItemDictionary.TryGetValue(item, out SelectionTarget target)
            || CanDrag?.Invoke(target) != true)
        {
            return default;
        }

        // Re-asserted at the start of every drag, never trusted to the
        // constructor: Godot's Tree zeroes drop_mode_flags on NOTIFICATION_DRAG_END
        // — by design, the engine's own docks set it per drag — so a value set
        // once served exactly the first drag of a session, and every later one
        // found each section reading -100 and refused. That shipped, and read as
        // "drag and drop stopped working". Both flags, because a drop means two
        // different things: onto a row is "become its child", between rows is
        // "take that place among its siblings".
        DropModeFlags = (int)(DropModeFlagsEnum.OnItem | DropModeFlagsEnum.Inbetween);

        _dragging = target;

        SetDragPreview(new Label { Text = item.GetText(0) });

        return DragMarker;
    }

    /// <summary>
    /// Whether the drop would mean anything. The panel answers the part it can see
    /// — that this is one of its own drags, and that it is over a row — and asks
    /// <see cref="CanDrop"/> for the rest, because whether an element may live
    /// under a material is a question about the document and this panel has never
    /// heard of one.
    /// </summary>
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return data.VariantType == Variant.Type.String
            && data.AsString() == DragMarker
            && TryResolveDrop(atPosition, out HierarchyDrop drop)
            && CanDrop?.Invoke(drop) == true;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!TryResolveDrop(atPosition, out HierarchyDrop drop) || CanDrop?.Invoke(drop) != true)
            return;

        // Deferred like every row event: answering this rebuilds the very subtree
        // the drop landed in, which the engine refuses while it is delivering the
        // mouse event that caused it. See DeferredReport.
        DeferredReport.Raise(() => ItemDropped?.Invoke(drop));
    }

    /// <summary>Hides <see cref="Tree.Clear"/> on purpose — the lookups have to go with it.</summary>
    public new void Clear()
    {
        base.Clear();
        _itemsByTargetDictionary.Clear();
        _targetsByItemDictionary.Clear();
        _visibilityByItemDictionary.Clear();
        _deletableItemsHashSet.Clear();
        _dragging = default;
        _hoveredItem = null;

        // The anchor a Shift range measures from is a row of the tree being
        // emptied — see the canon on what a Clear owes: what a refill does not
        // touch is exactly what survives a document it had no business outliving.
        _activeItem = null;
        _emphasized = null;
    }

    public void SetRoot(SelectionTarget target, string name)
    {
        Clear();
        Register(target, CreateItem(), name);
        SetItemIcon(target, ComponentKind.None);
    }

    public void AddItem(
        SelectionTarget target,
        SelectionTarget parent,
        string name,
        int index,
        ComponentKind component)
    {
        if (!_itemsByTargetDictionary.TryGetValue(parent, out TreeItem? parentItem))
            throw new ArgumentException($"No row {parent} to add under.", nameof(parent));

        Register(target, CreateItem(parentItem, index), name);
        SetItemIcon(target, component);
    }

    public void RemoveItem(SelectionTarget target)
    {
        if (!_itemsByTargetDictionary.TryGetValue(target, out TreeItem? item))
            return;

        Forget(item);
        item.Free();
    }

    public void RenameItem(SelectionTarget target, string name)
    {
        if (_itemsByTargetDictionary.TryGetValue(target, out TreeItem? item))
            item.SetText(0, name);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The folds above the row are opened first. A row inside a folded group has
    /// no place on screen for the engine to scroll to, so without this "bring it
    /// into view" would quietly mean nothing in exactly the case where the user
    /// most needs it — a model picked in the viewport that lives inside a group
    /// they had collapsed. Only the <em>ancestors</em>: unfolding the row's own
    /// subtree would be a second thing happening that nobody asked for.
    /// </remarks>
    public void ScrollTo(SelectionTarget target)
    {
        if (!_itemsByTargetDictionary.TryGetValue(target, out TreeItem? item))
            return;

        for (TreeItem? ancestor = item.GetParent(); ancestor != null; ancestor = ancestor.GetParent())
            ancestor.Collapsed = false;

        ScrollToItem(item);
    }

    /// <summary>
    /// Marks the row being worked on — the open scene, the open prefab. The
    /// target is remembered as well as written, because a colour set on a
    /// <see cref="TreeItem"/> is the one kind of theme lookup a repaint cannot
    /// reach: rows are dressed once, as they are built.
    /// </summary>
    /// <remarks>
    /// Left unremembered this shipped as a row that <b>disappeared</b> on a theme
    /// change — the light theme's accent-as-text is a dark navy, and on a dark
    /// panel that is very nearly the panel. The class rule is the ledger's:
    /// anything read from the theme once must be re-read on
    /// <c>NotificationThemeChanged</c>.
    /// </remarks>
    public void SetItemEmphasized(SelectionTarget target, bool emphasized)
    {
        if (emphasized)
            _emphasized = target;
        else if (_emphasized == target)
            _emphasized = null;

        if (!_itemsByTargetDictionary.TryGetValue(target, out TreeItem? item))
            return;

        if (emphasized)
            item.SetCustomColor(
                0, GetThemeColor("accent_text", EditorThemeFactory.SemanticType));
        else
            item.ClearCustomColor(0);
    }

    public void SetItemSwatch(SelectionTarget target, Color color)
    {
        if (!_itemsByTargetDictionary.TryGetValue(target, out TreeItem? item))
            return;

        Image image = Image.CreateEmpty(SwatchSize, SwatchSize, false, Image.Format.Rgba8);
        image.Fill(color);

        item.SetIcon(0, ImageTexture.CreateFromImage(image));
        item.SetIconMaxWidth(0, SwatchSize);
    }

    /// <inheritdoc/>
    public void SetItemVisibility(SelectionTarget target, RowVisibility visibility)
    {
        if (!_itemsByTargetDictionary.TryGetValue(target, out TreeItem? item))
            return;

        // The button is added once and dressed from then on. Erasing and re-adding
        // it per state would work and would also make the eye a thing that comes
        // and goes; the cell keeps its width instead, so nothing in the row moves
        // when the pointer arrives.
        if (!_visibilityByItemDictionary.ContainsKey(item))
        {
            item.AddButton(
                0,
                GetThemeIcon(EditorIconCatalog.EyeOpen, EditorThemeFactory.SemanticType),
                VisibilityButtonId);
        }

        _visibilityByItemDictionary[item] = visibility;

        if (visibility == RowVisibility.Shown)
            RowRecession.Clear(item);
        else
            Recession.Apply(item);

        DressEye(item);
    }

    /// <inheritdoc/>
    public void SetItemDeletable(SelectionTarget target, bool deletable)
    {
        if (!_itemsByTargetDictionary.TryGetValue(target, out TreeItem? item))
            return;

        if (!deletable)
        {
            if (_deletableItemsHashSet.Remove(item))
                EraseButton(item, DeleteButtonId);

            return;
        }

        // Added once and dressed from then on, like the eye — and after it, which
        // is what puts the bin on the outside: Godot lays a cell's buttons out
        // from the right edge inwards, in reverse order.
        if (_deletableItemsHashSet.Add(item))
        {
            item.AddButton(
                0,
                GetThemeIcon(EditorIconCatalog.Trash, EditorThemeFactory.SemanticType),
                DeleteButtonId);
        }

        DressBin(item);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The active row is selected <b>last</b>, and that is not cosmetic: selecting a
    /// row makes it the engine's focused one, and the focused row is what the next
    /// Shift range is measured from.
    /// </remarks>
    public void SetSelection(IReadOnlyList<SelectionTarget> targets, SelectionTarget active)
    {
        _reporting = false;

        DeselectAll();
        _activeItem = null;

        for (var index = 0; index < targets.Count; index++)
        {
            SelectionTarget target = targets[index];

            if (target != active && _itemsByTargetDictionary.TryGetValue(target, out TreeItem? item))
                item.Select(0);
        }

        if (_itemsByTargetDictionary.TryGetValue(active, out TreeItem? activeItem))
        {
            activeItem.Select(0);
            _activeItem = activeItem;
        }

        _reporting = true;
    }

    private void Register(SelectionTarget target, TreeItem item, string name)
    {
        item.SetText(0, name);
        _itemsByTargetDictionary[target] = item;
        _targetsByItemDictionary[item] = target;
    }

    /// <summary>
    /// Dresses a row in its catalogue icon. Material rows get none here — their
    /// chip is the colour swatch the presenter sets separately.
    /// </summary>
    private void SetItemIcon(SelectionTarget target, ComponentKind component)
    {
        string? icon = EditorIconCatalog.ForRow(target.Kind, component);

        if (icon is null)
            return;

        TreeItem item = _itemsByTargetDictionary[target];

        item.SetIcon(0, GetThemeIcon(icon, EditorThemeFactory.SemanticType));
        item.SetIconMaxWidth(0, GetThemeConstant("icon_size", EditorThemeFactory.SemanticType));
    }

    /// <summary>
    /// The look of a row that reaches nothing, read from the theme rather than
    /// held: rows are dressed one at a time as the panel is filled, which is when
    /// every other theme lookup here happens too.
    /// </summary>
    private RowRecession Recession => new(
        GetThemeColor("text_receded", EditorThemeFactory.SemanticType),
        GetThemeColor("icon_receded", EditorThemeFactory.SemanticType));

    /// <summary>
    /// Which of the three states the row's eye is in: crossed and plain to see, an
    /// open one offered under the pointer, or nothing at all. The two glyphs are
    /// the theme's; what separates the second state from the third is the alpha,
    /// so the button holds its place in the cell either way.
    /// </summary>
    /// <remarks>
    /// The eye reads the element's <b>own</b> flag, which is why
    /// <see cref="RowVisibility.HiddenByAncestor"/> wears the open one: the row has
    /// receded, and clicking its eye would hide it in its own right rather than
    /// show it. The crossed eye keeps full strength on a receded row — it is the
    /// reason the row receded, and has to stay legible.
    /// </remarks>
    private void DressEye(TreeItem item)
    {
        if (!_visibilityByItemDictionary.TryGetValue(item, out RowVisibility visibility))
            return;

        var index = item.GetButtonById(0, VisibilityButtonId);

        if (index < 0)
            return;

        bool hidden = visibility == RowVisibility.Hidden;

        item.SetButton(
            0,
            index,
            GetThemeIcon(
                hidden ? EditorIconCatalog.EyeClosed : EditorIconCatalog.EyeOpen,
                EditorThemeFactory.SemanticType));

        var alpha = hidden ? 1f : ReferenceEquals(item, _hoveredItem) ? OfferedAlpha : 0f;

        item.SetButtonColor(0, index, new Color(1f, 1f, 1f, alpha));
        item.SetButtonTooltipText(0, index, hidden ? "Show" : "Hide");
    }

    /// <summary>
    /// The bin: nothing at all until the pointer is on the row, then offered, or
    /// greyed where the deletion would be refused.
    /// </summary>
    /// <remarks>
    /// The greying is Godot's own disabled drawing, which ignores the button's
    /// colour — so the flag is raised <b>only while the row is hovered</b>. Left
    /// standing, it would hold the bin visible on every row that refuses it, which
    /// is exactly the row that should show nothing until asked. Being disabled is
    /// also what refuses the click, so the grey and the refusal are one thing
    /// rather than a look and a rule that could part company.
    /// </remarks>
    private void DressBin(TreeItem item)
    {
        if (!_deletableItemsHashSet.Contains(item))
            return;

        var index = item.GetButtonById(0, DeleteButtonId);

        if (index < 0)
            return;

        if (!ReferenceEquals(item, _hoveredItem))
        {
            item.SetButtonDisabled(0, index, false);
            item.SetButtonColor(0, index, Transparent);

            return;
        }

        bool allowed = _targetsByItemDictionary.TryGetValue(item, out SelectionTarget target)
            && CanDelete?.Invoke(target) == true;

        // Full strength when it is live, and Godot's own half-strength disabled
        // drawing when it is not. The colour below is ignored in the second case —
        // measured, not assumed — which is why the live one cannot be the eye's
        // quiet OfferedAlpha: 0.55 against the engine's 0.5 is not a difference
        // anybody can see, and the grey has to be legible as a refusal.
        item.SetButtonDisabled(0, index, !allowed);
        item.SetButtonColor(0, index, Colors.White);
        item.SetButtonTooltipText(0, index, allowed ? "Delete" : "Cannot be deleted");
    }

    private static void EraseButton(TreeItem item, int id)
    {
        var index = item.GetButtonById(0, id);

        if (index >= 0)
            item.EraseButton(0, index);
    }

    /// <summary>
    /// The row the pointer is on. Both the row it left and the row it arrived at
    /// are redressed, because an eye is offered to exactly one row at a time.
    /// </summary>
    private void SetHovered(TreeItem? item)
    {
        if (ReferenceEquals(item, _hoveredItem))
            return;

        TreeItem? previous = _hoveredItem;
        _hoveredItem = item;

        Dress(previous);
        Dress(item);
    }

    /// <summary>Everything on a row that answers to where the pointer is.</summary>
    private void Dress(TreeItem? item)
    {
        if (item == null)
            return;

        DressEye(item);
        DressBin(item);
    }

    /// <summary>
    /// The eye was pressed. Deferred like every other row event — see
    /// <see cref="DeferredReport"/> — and reported as the row it happened on
    /// rather than as a new state: what the element is currently set to is the
    /// presenter's to read.
    /// </summary>
    private void OnButtonClicked(TreeItem item, long column, long id, long mouseButtonIndex)
    {
        if (mouseButtonIndex != (long)MouseButton.Left
            || !_targetsByItemDictionary.TryGetValue(item, out SelectionTarget target))
        {
            return;
        }

        switch (id)
        {
            case VisibilityButtonId:
                DeferredReport.Raise(() => VisibilityToggled?.Invoke(target));
                break;

            case DeleteButtonId:
                DeferredReport.Raise(() => DeleteRequested?.Invoke(target));
                break;
        }
    }

    /// <summary>
    /// Godot frees a tree item's children with it, so the lookups have to be
    /// cleaned up for the whole subtree before the item goes.
    /// </summary>
    private void Forget(TreeItem item)
    {
        foreach (TreeItem child in item.GetChildren())
            Forget(child);

        // A freed item must not stay the hovered one: the next pointer move would
        // redress a row that no longer exists. Nor the anchor, which the next
        // Shift range would walk from.
        if (ReferenceEquals(item, _hoveredItem))
            _hoveredItem = null;

        if (ReferenceEquals(item, _activeItem))
            _activeItem = null;

        _visibilityByItemDictionary.Remove(item);
        _deletableItemsHashSet.Remove(item);

        if (_targetsByItemDictionary.Remove(item, out SelectionTarget target))
            _itemsByTargetDictionary.Remove(target);
    }

    /// <summary>
    /// A row's selected flag changed — what a click does. One gesture emits this
    /// once per row it touched, so the first books a report and the rest ride on
    /// it: the answer is the focused row either way.
    /// </summary>
    private void OnMultiSelected(TreeItem item, long column, bool selected) => BookReport();

    /// <summary>
    /// The cursor moved onto a row. Under <see cref="Tree.SelectModeEnum.Multi"/>
    /// this is the <b>whole</b> of what an arrow key does — the engine moves its
    /// focus and emits only this — so without it the keyboard moved a ring around
    /// the panel and changed nothing. Found by driving it.
    /// </summary>
    private void OnCellSelected() => BookReport();

    private void BookReport()
    {
        if (!_reporting || _reportPending)
            return;

        _reportPending = true;

        // Deferred like every row event — see DeferredReport. It is also what
        // makes the coalescing work: the rest of the gesture's emissions land
        // before the frame this is read on, and a click emits both signals.
        DeferredReport.Raise(ReportSelection);
    }

    /// <summary>
    /// Which row the engine has moved to, reported as
    /// <see cref="SelectionIntent.Replace"/> — one row becoming the whole
    /// selection.
    /// </summary>
    /// <remarks>
    /// The <b>focused</b> row, deliberately, rather than the set of rows the engine
    /// currently draws selected. Under <see cref="Tree.SelectModeEnum.Multi"/> an
    /// arrow key moves the focus and selects the row it lands on <em>without
    /// clearing the one it left</em>, so reading the set back made keyboard
    /// navigation accumulate a selection instead of moving it — click a row, press
    /// Down, and two rows were selected. Caught by driving it.
    /// <para>
    /// Reading the focus alone is also the more honest answer, not merely the
    /// convenient one: the only gestures that legitimately select several rows are
    /// the modifier clicks, and those never reach here, having been answered before
    /// the engine saw them. So the engine's own set is never something this panel
    /// should be repeating back — whatever it holds, the presenter's push-back is
    /// what the rows end up showing.
    /// </para>
    /// <para>
    /// What it costs: Shift+arrow does not extend the selection from the keyboard.
    /// It never did — the panel was single-select before this — and buying it back
    /// means carrying the key's own modifiers across the deferred report.
    /// </para>
    /// </remarks>
    private void ReportSelection()
    {
        _reportPending = false;

        if (!TryGetSelectedTarget(out SelectionTarget focused))
            return;

        SelectionRequested?.Invoke(new HierarchySelection(
            new[] { focused }, focused, SelectionIntent.Replace));
    }

    /// <summary>
    /// A double click selects the row first, so what is under the pointer is what
    /// the engine has already made current.
    /// </summary>
    private void OnItemActivated()
    {
        // Deferred, and here it is load-bearing: activating a scene row
        // rebuilds the scene-bound presenters, whose constructors refill this
        // very tree — which the engine refuses during the click's dispatch.
        if (TryGetSelectedTarget(out SelectionTarget target))
            DeferredReport.Raise(() => _itemActivated?.Invoke(target));
    }

    /// <summary>
    /// Turns the pointer's position into what the drop is asking for: onto a row
    /// means "become its last child", between rows means "take that place among
    /// its siblings". The index is in the tree as it stands — nothing has moved
    /// yet — which is what <see cref="HierarchyDrop.Index"/> promises.
    /// </summary>
    private bool TryResolveDrop(Vector2 atPosition, out HierarchyDrop drop)
    {
        drop = default;

        TreeItem? item = GetItemAtPosition(atPosition);
        int section = GetDropSectionAtPosition(atPosition);

        if (item == null
            || section == NoDropSection
            || !_targetsByItemDictionary.TryGetValue(item, out SelectionTarget over))
        {
            return false;
        }

        if (section == 0)
        {
            drop = new HierarchyDrop(_dragging, over, item.GetChildCount());

            return true;
        }

        TreeItem? parent = item.GetParent();

        if (parent == null
            || !_targetsByItemDictionary.TryGetValue(parent, out SelectionTarget parentTarget))
        {
            return false;
        }

        // -1 is above the row, 1 below it.
        drop = new HierarchyDrop(
            _dragging, parentTarget, item.GetIndex() + (section < 0 ? 0 : 1));

        return true;
    }

    private bool TryGetSelectedTarget(out SelectionTarget target)
    {
        TreeItem? selected = GetSelected();

        if (selected != null)
            return _targetsByItemDictionary.TryGetValue(selected, out target);

        target = default;

        return false;
    }
}
