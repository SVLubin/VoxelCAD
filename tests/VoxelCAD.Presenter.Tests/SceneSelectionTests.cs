using System;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The scene row selects and inspects like anything else in the project tree.
/// What it is not is an element: it has no transform to edit, and while it is the
/// only scene there is nothing to delete it into.
/// </summary>
public sealed class SceneSelectionTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    public SceneSelectionTests() => _shell = new ShellPresenter(_view, new FakePreferences());

    public void Dispose() => _shell.Dispose();

    [Fact]
    public void ClickingTheSceneRowSelectsIt()
    {
        _view.HierarchyView.RaiseItemClicked(Scene);

        Assert.Equal(Scene, _view.HierarchyView.Selected);
    }

    [Fact]
    public void TheInspectorShowsTheBuildRegionRatherThanGoingBlank()
    {
        _view.HierarchyView.RaiseItemClicked(Scene);

        Assert.NotNull(_view.InspectorView.SceneSize);
        Assert.Equal("Scene", _view.InspectorView.TargetName);
    }

    [Fact]
    public void SelectingAnElementAfterwardsGoesBackToItsTransform()
    {
        _view.HierarchyView.RaiseItemClicked(Scene);
        _view.RaiseCreateGroup();

        Assert.Null(_view.InspectorView.SceneSize);
        Assert.Equal("Group", _view.InspectorView.TargetName);
    }

    /// <summary>
    /// Delete acts on the selection, and a project always keeps somewhere to
    /// build — so the last scene's row offers nothing to delete.
    /// </summary>
    [Fact]
    public void TheLastSceneCannotBeDeleted()
    {
        _view.RaiseCreateGroup();
        Assert.True(_view.DeleteEnabled);

        _view.HierarchyView.RaiseItemClicked(Scene);

        Assert.False(_view.DeleteEnabled);
    }

    /// <summary>
    /// The shell drops a selection whose element has gone. A scene is never an
    /// element, so the same test applied to it would drop the selection on the
    /// next change to anything at all.
    /// </summary>
    [Fact]
    public void ASceneSelectionSurvivesEditsElsewhere()
    {
        _view.HierarchyView.RaiseItemClicked(Scene);
        _view.RaiseCreateGroup();
        _view.HierarchyView.RaiseItemClicked(Scene);

        _view.RaiseUndo();

        Assert.Equal(Scene, _view.HierarchyView.Selected);
        Assert.NotNull(_view.InspectorView.SceneSize);
    }

    /// <summary>A new element still parents to whatever container is selected.</summary>
    [Fact]
    public void CreatingWithTheSceneSelectedParentsToTheScene()
    {
        _view.HierarchyView.RaiseItemClicked(Scene);
        _view.RaiseCreateGroup();

        Assert.Equal(Scene, _view.HierarchyView.ParentOf(_view.HierarchyView.Selected));
    }

    private SelectionTarget Scene =>
        new(SelectionKind.Scene, _view.HierarchyView.SceneId);
}
