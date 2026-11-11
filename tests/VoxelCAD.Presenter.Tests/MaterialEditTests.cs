using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Editing a material's colour is a gesture, not a stream of edits: dragging
/// across the picker's gradient reports a colour every frame, and undo has to
/// take back the drag rather than one frame of it.
/// </summary>
public sealed class MaterialEditTests : IDisposable
{
    private static readonly SelectionTarget Materials = new(SelectionKind.MaterialsCategory, Guid.Empty);

    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    public MaterialEditTests()
    {
        _shell = new ShellPresenter(_view, new FakePreferences());
        _view.HierarchyView.RaiseItemClicked(Material);
    }

    public void Dispose() => _shell.Dispose();

    [Fact]
    public void AWholeDragIsOneRecord()
    {
        _view.InspectorView.RaiseColorGesture(
            new Color(0.1f, 0.1f, 0.1f),
            new Color(0.4f, 0.2f, 0.1f),
            new Color(0.9f, 0.3f, 0.2f));

        Assert.Equal(new[] { "Change material Default" }, _view.HistoryView.Labels);
    }

    [Fact]
    public void UndoTakesBackTheWholeDrag()
    {
        Color before = _view.SceneView.Materials[Material.Id];

        _view.InspectorView.RaiseColorGesture(
            new Color(0.1f, 0.1f, 0.1f),
            new Color(0.9f, 0.3f, 0.2f));

        Assert.Equal(new Color(0.9f, 0.3f, 0.2f), _view.SceneView.Materials[Material.Id]);

        _view.RaiseUndo();

        Assert.Equal(before, _view.SceneView.Materials[Material.Id]);
        Assert.False(_view.CanUndo);
    }

    [Fact]
    public void RedoPutsItBack()
    {
        _view.InspectorView.RaiseColorGesture(new Color(0.9f, 0.3f, 0.2f));

        _view.RaiseUndo();
        _view.RaiseRedo();

        Assert.Equal(new Color(0.9f, 0.3f, 0.2f), _view.SceneView.Materials[Material.Id]);
    }

    /// <summary>Opening the picker and closing it again is not an edit.</summary>
    [Fact]
    public void AGestureThatChangedNothingLeavesNoRecord()
    {
        _view.InspectorView.RaiseColorGesture();

        Assert.Empty(_view.HistoryView.Labels);

        _view.InspectorView.RaiseColorGesture(_view.SceneView.Materials[Material.Id]);

        Assert.Empty(_view.HistoryView.Labels);
    }

    [Fact]
    public void TwoDragsAreTwoRecords()
    {
        _view.InspectorView.RaiseColorGesture(new Color(0.9f, 0.3f, 0.2f));
        _view.InspectorView.RaiseColorGesture(new Color(0.2f, 0.3f, 0.9f));

        Assert.Equal(2, _view.HistoryView.Labels.Count);
    }

    /// <summary>
    /// A colour edit belongs to the project, not to a scene, so undoing one has
    /// nowhere to take the editor — and must not move it.
    /// </summary>
    [Fact]
    public void UndoingAColourEditDoesNotSwitchScenes()
    {
        SelectionTarget scene = _view.HierarchyView.Emphasized;

        _view.RaiseCreate(CreateKind.Scene);
        SelectionTarget second = _view.HierarchyView.Emphasized;

        _view.HierarchyView.RaiseItemClicked(Material);
        _view.InspectorView.RaiseColorGesture(new Color(0.9f, 0.3f, 0.2f));
        _view.RaiseUndo();

        Assert.NotEqual(scene, second);
        Assert.Equal(second, _view.HierarchyView.Emphasized);
    }

    /// <summary>
    /// The scope lives across frames, so anything that starts a new action has to
    /// close it first. An open batch goes on collecting: the scene created here
    /// would otherwise be filed inside a record called "change material", and
    /// disappear from the history as an entry of its own.
    /// </summary>
    [Fact]
    public void ANewActionClosesAnUnfinishedGestureInsteadOfJoiningIt()
    {
        _view.InspectorView.RaiseColorEditBegan();
        _view.InspectorView.RaiseColorEdited(new Color(0.9f, 0.3f, 0.2f));

        _view.RaiseCreate(CreateKind.Scene);

        _view.HierarchyView.RaiseItemClicked(Material);
        _view.InspectorView.RaiseColorGesture(new Color(0.2f, 0.3f, 0.9f));

        Assert.Equal(
            new[] { "Change material Default", "Create scene Scene 2", "Change material Default" },
            _view.HistoryView.Labels);
    }

    /// <summary>
    /// The worst of the cases the scope has to be closed for: undo walking into
    /// a record that is still being written.
    /// </summary>
    [Fact]
    public void UndoDuringAGestureCommitsItAndThenReversesIt()
    {
        Color before = _view.SceneView.Materials[Material.Id];

        _view.InspectorView.RaiseColorEditBegan();
        _view.InspectorView.RaiseColorEdited(new Color(0.9f, 0.3f, 0.2f));

        _view.RaiseUndo();

        Assert.Equal(before, _view.SceneView.Materials[Material.Id]);
        Assert.Single(_view.HistoryView.Labels);
        Assert.False(_view.CanUndo);
        Assert.True(_view.CanRedo);
    }

    private SelectionTarget Material => _view.HierarchyView.ChildrenOf(Materials)[0];
}
