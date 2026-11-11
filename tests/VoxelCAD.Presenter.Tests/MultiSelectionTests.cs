using System;
using VoxelCAD.CommonTypes;
using Xunit;
using Godot;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Selecting several things at once, through the shell and its fakes. The rule
/// itself is <c>SelectionEdit</c>'s and tested there; what is here is that both
/// surfaces reach it, that the two agree, and that what the panels are told about
/// a selection of several is the whole of it.
/// </summary>
public sealed class MultiSelectionTests : IDisposable
{
    private static readonly Vector2 Spot = new(400f, 300f);

    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    public MultiSelectionTests()
    {
        _shell = new ShellPresenter(_view, new FakePreferences());
        _view.SceneView.RaiseToolRequested(ToolKind.Select);
    }

    public void Dispose() => _shell.Dispose();

    [Fact]
    public void TheCommandModifierAddsInTheViewport()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, first);
        _view.SceneView.RaiseElementPicked(SelectionIntent.Toggle, Elsewhere, second);

        Assert.Equal(new[] { first, second }, _view.SceneView.HighlightedElements);
    }

    [Fact]
    public void AndTakesOutAgainWhatItPutIn()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, first);
        _view.SceneView.RaiseElementPicked(SelectionIntent.Toggle, Elsewhere, second);
        _view.SceneView.RaiseElementPicked(SelectionIntent.Toggle, Elsewhere, second);

        Assert.Equal(new[] { first }, _view.SceneView.HighlightedElements);
    }

    /// <summary>
    /// Walking a stack by clicking the same pixel again is a plain click's affair.
    /// A modifier click means the thing you can see, and clicking it a second time
    /// with the command modifier has to take it back out rather than reach past it
    /// — or the modifier stops meaning toggle.
    /// </summary>
    [Fact]
    public void AModifierClickDoesNotWalkAStack()
    {
        Guid near = CreateModel();
        Guid far = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, near, far);
        _view.SceneView.RaiseElementPicked(SelectionIntent.Toggle, Spot, near, far);

        Assert.Empty(_view.SceneView.HighlightedElements);
    }

    /// <summary>
    /// And the plain click after one starts from the front rather than resuming a
    /// walk nobody was on.
    /// </summary>
    [Fact]
    public void APlainClickAfterAModifierOneStartsOver()
    {
        Guid near = CreateModel();
        Guid far = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, near, far);
        _view.SceneView.RaiseElementPicked(SelectionIntent.Toggle, Spot, near, far);
        _view.SceneView.RaiseElementPicked(Spot, near, far);

        Assert.Equal(new[] { near }, _view.SceneView.HighlightedElements);
    }

    /// <summary>
    /// A modifier click that crossed nothing leaves the selection alone: the hand
    /// was adding to it and missed. A plain one still lands on the room.
    /// </summary>
    [Fact]
    public void AModifierClickPastEverythingLeavesTheSelectionAlone()
    {
        Guid model = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, model);
        _view.SceneView.RaiseElementPicked(SelectionIntent.Toggle, Elsewhere);

        Assert.Equal(new[] { model }, _view.SceneView.HighlightedElements);
        Assert.Equal(
            new SelectionTarget(SelectionKind.Element, model), _view.HierarchyView.Selected);
    }

    /// <summary>
    /// Every selected element wears a border; only the last one clicked carries the
    /// handles. A transform drag edits one channel of one element, and there is no
    /// honest answer yet for what pulling on several at once would write.
    /// </summary>
    [Fact]
    public void OnlyTheActiveOneCarriesTheHandles()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, first);
        _view.SceneView.RaiseElementPicked(SelectionIntent.Toggle, Elsewhere, second);

        Assert.Equal(second, _view.SceneView.HighlightTarget);
    }

    /// <summary>
    /// The project tree draws the whole selection, whichever surface made it —
    /// clicking a model in the viewport is the same event as clicking its row,
    /// arriving from the other side.
    /// </summary>
    [Fact]
    public void TheProjectTreeShowsWhatTheViewportSelected()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, first);
        _view.SceneView.RaiseElementPicked(SelectionIntent.Toggle, Elsewhere, second);

        Assert.Equal(
            new[]
            {
                new SelectionTarget(SelectionKind.Element, first),
                new SelectionTarget(SelectionKind.Element, second)
            },
            _view.HierarchyView.Selection);
    }

    [Fact]
    public void AModifierClickInTheTreeAddsToTheSelection()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();

        _view.HierarchyView.RaiseItemClicked(Row(first));
        _view.HierarchyView.RaiseItemClicked(Row(second), SelectionIntent.Toggle);

        Assert.Equal(new[] { Row(first), Row(second) }, _view.HierarchyView.Selection);
        Assert.Equal(second, _view.SceneView.HighlightTarget);
    }

    /// <summary>
    /// A Shift range: the panel walks the run of rows it draws between the anchor
    /// and the row clicked, and the presenter takes the lot.
    /// </summary>
    [Fact]
    public void AShiftRangeInTheTreeTakesTheWholeRun()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();
        Guid third = CreateModel();

        _view.HierarchyView.RaiseItemClicked(Row(first));
        _view.HierarchyView.RaiseRangeClicked(
            Row(third), Row(first), Row(second), Row(third));

        Assert.Equal(
            new[] { Row(first), Row(second), Row(third) }, _view.HierarchyView.Selection);
        Assert.Equal(third, _view.SceneView.HighlightTarget);
    }

    /// <summary>
    /// A run that crossed the tree's categories keeps the kind of the row actually
    /// clicked, and the panel is told what the session made of it rather than what
    /// the click asked for.
    /// </summary>
    [Fact]
    public void ARangeAcrossCategoriesKeepsOneKind()
    {
        Guid model = CreateModel();
        SelectionTarget scene = new(SelectionKind.Scene, _view.HierarchyView.SceneId);

        _view.HierarchyView.RaiseRangeClicked(Row(model), scene, Row(model));

        Assert.Equal(new[] { Row(model) }, _view.HierarchyView.Selection);
    }

    /// <summary>
    /// The panel follows what was picked: a model clicked in the viewport may name
    /// a row scrolled out of sight, and a panel that says nothing about it is a
    /// panel the user has to go looking in.
    /// </summary>
    [Fact]
    public void AViewportClickBringsTheRowIntoView()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, second);
        _view.SceneView.RaiseElementPicked(Elsewhere, first);

        Assert.Equal(Row(first), _view.HierarchyView.ScrolledTo);
    }

    /// <summary>
    /// And it follows the one just added rather than the one the selection started
    /// from — the freshly picked row is what the user is looking for.
    /// </summary>
    [Fact]
    public void AModifierClickBringsTheNewlyAddedRowIntoView()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, first);
        _view.SceneView.RaiseElementPicked(SelectionIntent.Toggle, Elsewhere, second);

        Assert.Equal(Row(second), _view.HierarchyView.ScrolledTo);
    }

    /// <summary>
    /// A row's own bin deletes that row and nothing else — it names a row, not the
    /// selection. What goes is dropped from the selection and the rest stands: the
    /// other parts a user picked out are still perfectly good.
    /// </summary>
    [Fact]
    public void DeletingOneRowLeavesTheRestOfTheSelection()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, first);
        _view.SceneView.RaiseElementPicked(SelectionIntent.Toggle, Elsewhere, second);
        _view.HierarchyView.RaiseDeleteRequested(Row(second));

        Assert.Equal(new[] { first }, _view.SceneView.HighlightedElements);
        Assert.Equal(new[] { Row(first) }, _view.HierarchyView.Selection);
    }

    /// <summary>Somewhere the cycle will not read as the same spot.</summary>
    private static Vector2 Elsewhere => Spot + new Vector2(120f, 60f);

    private static SelectionTarget Row(Guid elementId) =>
        new(SelectionKind.Element, elementId);

    private Guid CreateModel()
    {
        _view.RaiseCreateVoxelModel();

        return _view.HierarchyView.Selected.Id;
    }
}
