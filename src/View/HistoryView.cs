using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <inheritdoc cref="IHistoryView"/>
/// <remarks>
/// A <see cref="Tree"/> rather than the flat list this used to be, for one
/// reason: an agent's session has to fold away. Everything a connection did
/// arrives as one run of records, and a run long enough to be worth folding is
/// also long enough to bury whatever the user did before it.
/// </remarks>
internal sealed partial class HistoryView : Tree, IHistoryView
{
    /// <summary>Which record each selectable row stands for; -1 is the empty project.</summary>
    private readonly Dictionary<TreeItem, int> _indicesByItemDictionary = new();

    /// <summary>Which session each group row heads, so folding one can be remembered.</summary>
    private readonly Dictionary<TreeItem, int> _groupsByItemDictionary = new();

    /// <summary>
    /// Sessions the user has folded away. Kept here because the panel is rebuilt
    /// from scratch on every history change, and a group that sprang open again
    /// each time an agent filed another record would be unfoldable in practice.
    /// It is appearance, so it belongs to the view rather than to the presenter.
    /// </summary>
    private readonly HashSet<int> _foldedSessionsHashSet = new();

    private bool _reporting = true;

    public HistoryView()
    {
        Columns = 1;
        HideRoot = true;
        AllowReselect = true;
        SelectMode = SelectModeEnum.Single;

        // It lives on a glass panel, and the pane should read through the rows.
        ThemeTypeVariation = "GlassTree";
        this.Expand().ContainsArrowFocus();

        AddChild(new FocusRingOverlay());

        ItemSelected += OnItemSelected;
        ItemCollapsed += OnItemCollapsed;
    }

    public event Action<int>? RecordClicked;

    /// <summary>
    /// The folds go with the rows, and that is the whole point of this existing:
    /// a session number identifies a run of records inside one document's history,
    /// so carrying a fold into the next document would collapse whatever happened
    /// to land on the same number.
    /// </summary>
    /// <remarks>
    /// Explicitly implemented, and not for taste: <see cref="Tree"/> has a
    /// <c>Clear</c> of its own that this class calls to empty its rows, and a
    /// method hiding it would silently turn every one of those calls into "and
    /// forget the folds too".
    /// </remarks>
    void IHistoryView.Clear()
    {
        _reporting = false;

        Clear();
        _indicesByItemDictionary.Clear();
        _groupsByItemDictionary.Clear();
        _foldedSessionsHashSet.Clear();

        _reporting = true;
    }

    public void SetRecords(IReadOnlyList<HistoryRow> rows, int currentIndex)
    {
        _reporting = false;

        // The shared "present, not in effect" pair — the same one a hidden
        // element's row in the project tree wears.
        var receded = new RowRecession(
            GetThemeColor("text_receded", EditorThemeFactory.SemanticType),
            GetThemeColor("icon_receded", EditorThemeFactory.SemanticType));
        int iconSize = GetThemeConstant("icon_size", EditorThemeFactory.SemanticType);

        Clear();
        _indicesByItemDictionary.Clear();
        _groupsByItemDictionary.Clear();

        TreeItem root = CreateItem();
        TreeItem origin = AddRecord(
            root, -1, "— empty project —", EditorIconCatalog.HistoryOrigin, iconSize);

        TreeItem? group = null;
        var openGroup = 0;
        TreeItem? current = currentIndex < 0 ? origin : null;

        for (var i = 0; i < rows.Count; i++)
        {
            HistoryRow row = rows[i];

            // A run of equal ids is one session: control is exclusive, so the
            // records of one grant cannot have anybody else's interleaved.
            if (row.GroupId != openGroup)
            {
                openGroup = row.GroupId;
                group = row.GroupId == 0
                    ? null
                    : AddGroup(root, row.GroupId, row.GroupLabel ?? string.Empty, iconSize);

                // A session whose first record is already undone is undone whole,
                // and has to say so on its own row: folded away, the rows that
                // would have shown it receding are not on screen to be read.
                if (group is not null && i > currentIndex)
                    receded.Apply(group);
            }

            TreeItem item = AddRecord(
                group ?? root, i, row.Label, EditorIconCatalog.ForAction(row.Kind), iconSize);

            if (i > currentIndex)
                receded.Apply(item);

            if (i == currentIndex)
                current = item;
        }

        SelectCurrent(current);

        _reporting = true;
    }

    private TreeItem AddRecord(TreeItem parent, int index, string label, string icon, int iconSize)
    {
        TreeItem item = CreateItem(parent);

        item.SetText(0, label);
        item.SetIcon(0, GetThemeIcon(icon, EditorThemeFactory.SemanticType));
        item.SetIconMaxWidth(0, iconSize);

        _indicesByItemDictionary[item] = index;

        return item;
    }

    /// <summary>
    /// The session's own row. Not selectable on purpose: it stands for a stretch
    /// of history rather than for a point in it, so clicking it folds and does
    /// nothing else — walking the document to some record inside would be a
    /// destructive answer to a gesture that only asked to see less.
    /// </summary>
    private TreeItem AddGroup(TreeItem parent, int session, string label, int iconSize)
    {
        TreeItem item = CreateItem(parent);

        item.SetText(0, label);
        item.SetIcon(0, GetThemeIcon(EditorIconCatalog.AgentSession, EditorThemeFactory.SemanticType));
        item.SetIconMaxWidth(0, iconSize);
        item.SetSelectable(0, false);
        item.Collapsed = _foldedSessionsHashSet.Contains(session);

        _groupsByItemDictionary[item] = session;

        return item;
    }

    /// <summary>
    /// Puts the cursor on the record the document currently sits at and brings it
    /// into view — the newest row is at the end while work is being added, and
    /// selecting alone does not scroll. A record inside a folded session has no
    /// row on screen to select, and the group stays folded: the user folded it.
    /// </summary>
    private void SelectCurrent(TreeItem? current)
    {
        DeselectAll();

        if (current is null || !IsRowVisible(current))
            return;

        current.Select(0);
        ScrollToItem(current);
    }

    private static bool IsRowVisible(TreeItem item)
    {
        for (TreeItem? parent = item.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent.Collapsed)
                return false;
        }

        return true;
    }

    private void OnItemSelected()
    {
        if (!_reporting)
            return;

        TreeItem? selected = GetSelected();

        if (selected is null || !_indicesByItemDictionary.TryGetValue(selected, out int index))
            return;

        // Deferred, and that is load-bearing: answering the click undoes,
        // undoing rebuilds this very tree, and the engine refuses that during
        // the click's own dispatch. See DeferredReport.
        DeferredReport.Raise(() => RecordClicked?.Invoke(index));
    }

    /// <summary>
    /// Remembers the fold so the next rebuild honours it. Godot raises this for
    /// the item that changed, whichever way it went.
    /// </summary>
    private void OnItemCollapsed(TreeItem item)
    {
        if (!_groupsByItemDictionary.TryGetValue(item, out int session))
            return;

        if (item.Collapsed)
            _foldedSessionsHashSet.Add(session);
        else
            _foldedSessionsHashSet.Remove(session);
    }
}
