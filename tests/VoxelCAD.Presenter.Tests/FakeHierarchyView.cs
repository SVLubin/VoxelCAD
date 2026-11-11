using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;

namespace VoxelCAD.Presenter.Tests;

/// <summary>Remembers what the project panel was told, so a test can read it back.</summary>
internal sealed class FakeHierarchyView : IHierarchyView
{
    private readonly Dictionary<SelectionTarget, string> _namesByTargetDictionary = new();
    private readonly Dictionary<SelectionTarget, SelectionTarget> _parentsByTargetDictionary = new();
    private readonly Dictionary<SelectionTarget, List<SelectionTarget>> _childrenByTargetDictionary = new();
    private readonly Dictionary<SelectionTarget, Color> _swatchesByTargetDictionary = new();
    private readonly Dictionary<SelectionTarget, ComponentKind> _componentsByTargetDictionary = new();
    private readonly Dictionary<SelectionTarget, RowVisibility> _visibilityByTargetDictionary = new();
    private readonly HashSet<SelectionTarget> _deletableHashSet = new();
    private readonly HashSet<SelectionTarget> _emphasizedHashSet = new();

    public event Action<HierarchySelection>? SelectionRequested;

    public event Action<SelectionTarget>? ItemActivated;

    public event Action<ToolKind>? ToolRequested;

    public event Action<HierarchyDrop>? ItemDropped;

    public event Action<SelectionTarget>? VisibilityToggled;

    public event Action<SelectionTarget>? DeleteRequested;

    public Func<HierarchyDrop, bool>? CanDrop { get; set; }

    public Func<SelectionTarget, bool>? CanDrag { get; set; }

    public Func<SelectionTarget, bool>? CanDelete { get; set; }

    public int ClearCount { get; private set; }

    public SelectionTarget Root { get; private set; }

    /// <summary>The row drawn as the active one.</summary>
    public SelectionTarget Selected { get; private set; }

    /// <summary>Every row drawn selected, in the order the panel was told.</summary>
    public IReadOnlyList<SelectionTarget> Selection { get; private set; } =
        Array.Empty<SelectionTarget>();

    /// <summary>The last row the panel was asked to bring into view; default if none.</summary>
    public SelectionTarget ScrolledTo { get; private set; }

    /// <summary>How many times a scroll was asked for, so a bulk fill can be told from a live add.</summary>
    public int ScrollCount { get; private set; }

    public IReadOnlyDictionary<SelectionTarget, string> Items => _namesByTargetDictionary;

    public IReadOnlyDictionary<SelectionTarget, Color> Swatches => _swatchesByTargetDictionary;

    /// <summary>The row drawn as current — the scene being worked on.</summary>
    public SelectionTarget Emphasized => _emphasizedHashSet.SingleOrDefault();

    /// <summary>Which scene the panel is showing the contents of.</summary>
    public Guid SceneId => Emphasized.Id;

    public void Clear()
    {
        ClearCount++;
        Root = default;
        Selected = default;
        Selection = Array.Empty<SelectionTarget>();
        _namesByTargetDictionary.Clear();
        _parentsByTargetDictionary.Clear();
        _childrenByTargetDictionary.Clear();
        _swatchesByTargetDictionary.Clear();
        _componentsByTargetDictionary.Clear();
        _visibilityByTargetDictionary.Clear();
        _deletableHashSet.Clear();
        _emphasizedHashSet.Clear();
    }

    public void SetRoot(SelectionTarget target, string name)
    {
        Root = target;
        _namesByTargetDictionary[target] = name;
    }

    public void AddItem(
        SelectionTarget target,
        SelectionTarget parent,
        string name,
        int index,
        ComponentKind component)
    {
        if (!_namesByTargetDictionary.ContainsKey(parent))
            throw new ArgumentException($"No row {parent} to add under.", nameof(parent));

        _namesByTargetDictionary[target] = name;
        _parentsByTargetDictionary[target] = parent;
        _componentsByTargetDictionary[target] = component;

        List<SelectionTarget> siblingsList = ChildListOf(parent);
        siblingsList.Insert(Math.Clamp(index, 0, siblingsList.Count), target);
    }

    public void RemoveItem(SelectionTarget target)
    {
        foreach (SelectionTarget child in ChildrenOf(target))
            RemoveItem(child);

        if (_parentsByTargetDictionary.Remove(target, out SelectionTarget parent))
            ChildListOf(parent).Remove(target);

        _namesByTargetDictionary.Remove(target);
        _childrenByTargetDictionary.Remove(target);
        _swatchesByTargetDictionary.Remove(target);
        _componentsByTargetDictionary.Remove(target);
        _visibilityByTargetDictionary.Remove(target);
        _deletableHashSet.Remove(target);
        _emphasizedHashSet.Remove(target);
    }

    public void RenameItem(SelectionTarget target, string name) =>
        _namesByTargetDictionary[target] = name;

    public void ScrollTo(SelectionTarget target)
    {
        ScrolledTo = target;
        ScrollCount++;
    }

    public void SetItemEmphasized(SelectionTarget target, bool emphasized)
    {
        if (emphasized)
            _emphasizedHashSet.Add(target);
        else
            _emphasizedHashSet.Remove(target);
    }

    public void SetItemSwatch(SelectionTarget target, Color color) =>
        _swatchesByTargetDictionary[target] = color;

    public void SetItemVisibility(SelectionTarget target, RowVisibility visibility) =>
        _visibilityByTargetDictionary[target] = visibility;

    public void SetItemDeletable(SelectionTarget target, bool deletable)
    {
        if (deletable)
            _deletableHashSet.Add(target);
        else
            _deletableHashSet.Remove(target);
    }

    public void SetSelection(IReadOnlyList<SelectionTarget> targets, SelectionTarget active)
    {
        Selection = targets.ToList();
        Selected = active;
    }

    /// <summary>What the panel was told sits in an element row's component slot.</summary>
    public ComponentKind ComponentOf(SelectionTarget target) =>
        _componentsByTargetDictionary.TryGetValue(target, out ComponentKind component)
            ? component
            : ComponentKind.None;

    /// <summary>Which row an item was added under. Default for the root.</summary>
    public SelectionTarget ParentOf(SelectionTarget target) =>
        _parentsByTargetDictionary.TryGetValue(target, out SelectionTarget parent) ? parent : default;

    /// <summary>In the order the panel was told to show them.</summary>
    public IReadOnlyList<SelectionTarget> ChildrenOf(SelectionTarget target) =>
        _childrenByTargetDictionary.TryGetValue(target, out List<SelectionTarget>? children)
            ? children.ToList()
            : Array.Empty<SelectionTarget>();

    public string NameOf(SelectionTarget target) =>
        _namesByTargetDictionary.TryGetValue(target, out string? name) ? name : string.Empty;

    /// <summary>The one row with this name, or the default target when there is none.</summary>
    public SelectionTarget Find(string name) =>
        _namesByTargetDictionary
            .Where(item => item.Value == name)
            .Select(item => item.Key)
            .SingleOrDefault();

    /// <summary>A plain click on one row, as the engine's own selection reports it.</summary>
    public void RaiseItemClicked(SelectionTarget target) =>
        RaiseSelectionRequested(
            new HierarchySelection(new[] { target }, target, SelectionIntent.Replace));

    /// <summary>A modifier click on one row.</summary>
    public void RaiseItemClicked(SelectionTarget target, SelectionIntent intent) =>
        RaiseSelectionRequested(new HierarchySelection(new[] { target }, target, intent));

    /// <summary>A Shift range: the run the panel walked, and the row clicked.</summary>
    public void RaiseRangeClicked(SelectionTarget active, params SelectionTarget[] run) =>
        RaiseSelectionRequested(
            new HierarchySelection(run, active, SelectionIntent.Extend));

    public void RaiseSelectionRequested(HierarchySelection selection) =>
        SelectionRequested?.Invoke(selection);

    public void RaiseItemActivated(SelectionTarget target) => ItemActivated?.Invoke(target);

    /// <summary>A tool key was pressed while the panel held the keyboard.</summary>
    public void RaiseToolRequested(ToolKind tool) => ToolRequested?.Invoke(tool);

    /// <summary>What the panel would ask before showing a drop indicator.</summary>
    public bool AskCanDrop(HierarchyDrop drop) => CanDrop?.Invoke(drop) == true;

    /// <summary>What the panel would ask before starting a drag at all.</summary>
    public bool AskCanDrag(SelectionTarget target) => CanDrag?.Invoke(target) == true;

    /// <summary>What the panel would ask as the pointer arrives on a row with a bin.</summary>
    public bool AskCanDelete(SelectionTarget target) => CanDelete?.Invoke(target) == true;

    /// <summary>The bin at the right edge of a row was clicked.</summary>
    public void RaiseDeleteRequested(SelectionTarget target) => DeleteRequested?.Invoke(target);

    /// <summary>Whether the row carries a bin at all — a fact about what kind of row it is.</summary>
    public bool HasBin(SelectionTarget target) => _deletableHashSet.Contains(target);

    public void RaiseItemDropped(HierarchyDrop drop) => ItemDropped?.Invoke(drop);

    /// <summary>The eye at the right edge of a row was clicked.</summary>
    public void RaiseVisibilityToggled(SelectionTarget target) => VisibilityToggled?.Invoke(target);

    /// <summary>
    /// Whether the row wears an eye at all, which is how a test tells an element
    /// row from everything else the panel holds.
    /// </summary>
    public bool HasEye(SelectionTarget target) =>
        _visibilityByTargetDictionary.ContainsKey(target);

    /// <summary>
    /// What the row was last told to say about visibility. <see cref="RowVisibility.Shown"/>
    /// for a row that carries no eye at all.
    /// </summary>
    public RowVisibility VisibilityOf(SelectionTarget target) =>
        _visibilityByTargetDictionary.TryGetValue(target, out RowVisibility visibility)
            ? visibility
            : RowVisibility.Shown;

    /// <summary>Whether the row's own eye reads as shown — its element's own flag.</summary>
    public bool IsVisible(SelectionTarget target) =>
        VisibilityOf(target) != RowVisibility.Hidden;

    /// <summary>Whether the row has receded: it reaches neither viewport nor file.</summary>
    public bool HasReceded(SelectionTarget target) =>
        VisibilityOf(target) != RowVisibility.Shown;

    private List<SelectionTarget> ChildListOf(SelectionTarget parent)
    {
        if (!_childrenByTargetDictionary.TryGetValue(parent, out List<SelectionTarget>? children))
        {
            children = new List<SelectionTarget>();
            _childrenByTargetDictionary[parent] = children;
        }

        return children;
    }
}
