using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.CommonTypes;
using Xunit;
using Godot;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Deleting a selection of several. The point of it is the history: one record, so
/// that one undo takes back the gesture the user watched rather than one element of
/// it.
/// </summary>
public sealed class GroupDeleteTests : IDisposable
{
    private static readonly Vector2 Spot = new(400f, 300f);

    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    public GroupDeleteTests()
    {
        _shell = new ShellPresenter(_view, new FakePreferences());
        _view.SceneView.RaiseToolRequested(ToolKind.Select);
    }

    public void Dispose() => _shell.Dispose();

    [Fact]
    public void TheWholeSelectionGoes()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();

        SelectBoth(first, second);
        _view.RaiseDeleteSelection();

        Assert.DoesNotContain(Row(first), _view.HierarchyView.Items.Keys);
        Assert.DoesNotContain(Row(second), _view.HierarchyView.Items.Keys);
        Assert.Empty(_view.SceneView.HighlightedElements);
    }

    [Fact]
    public void AsOneRecord()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();
        int before = _view.HistoryView.Labels.Count;

        SelectBoth(first, second);
        _view.RaiseDeleteSelection();

        Assert.Equal(before + 1, _view.HistoryView.Labels.Count);
    }

    /// <summary>Which is the whole point: one undo brings the lot back.</summary>
    [Fact]
    public void AndOneUndoBringsThemBack()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();

        SelectBoth(first, second);
        _view.RaiseDeleteSelection();
        _view.RaiseUndo();

        Assert.Contains(Row(first), _view.HierarchyView.Items.Keys);
        Assert.Contains(Row(second), _view.HierarchyView.Items.Keys);
    }

    [Fact]
    public void TheRecordNamesTheGroupRatherThanOneOfIt()
    {
        Guid first = CreateModel();
        Guid second = CreateModel();

        SelectBoth(first, second);
        _view.RaiseDeleteSelection();

        Assert.Equal("Delete Model and 1 more", _view.HistoryView.Labels.Last());
    }

    /// <summary>
    /// One target keeps the plain record: a batch of one would read "and 0 more"
    /// and buy nothing.
    /// </summary>
    [Fact]
    public void OneElementIsStillNamedPlainly()
    {
        Guid model = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, model);
        _view.RaiseDeleteSelection();

        Assert.Equal("Delete Model", _view.HistoryView.Labels.Last());
    }

    /// <summary>
    /// A subtree goes with its root, so a selected child may already be gone when
    /// its own turn comes. That is not a refusal, and the whole thing still lands
    /// as one record.
    /// </summary>
    [Fact]
    public void AParentAndItsChildTogetherAreOneRecord()
    {
        _view.RaiseCreateGroup();
        Guid group = _view.HierarchyView.Selected.Id;

        _view.RaiseCreateVoxelModel();
        Guid child = _view.HierarchyView.Selected.Id;

        int before = _view.HistoryView.Labels.Count;

        SelectBoth(group, child);
        _view.RaiseDeleteSelection();

        Assert.Equal(before + 1, _view.HistoryView.Labels.Count);
        Assert.DoesNotContain(Row(group), _view.HierarchyView.Items.Keys);
        Assert.DoesNotContain(Row(child), _view.HierarchyView.Items.Keys);
    }

    private static SelectionTarget Row(Guid elementId) =>
        new(SelectionKind.Element, elementId);

    private void SelectBoth(Guid first, Guid second)
    {
        _view.HierarchyView.RaiseItemClicked(Row(first));
        _view.HierarchyView.RaiseItemClicked(Row(second), SelectionIntent.Toggle);
    }

    /// <summary>
    /// At the scene's root, not under whatever is selected: these are siblings, and
    /// the parent-and-child case has a test of its own.
    /// </summary>
    private Guid CreateModel()
    {
        _view.HierarchyView.RaiseItemClicked(
            new SelectionTarget(SelectionKind.Scene, _view.HierarchyView.SceneId));
        _view.RaiseCreateVoxelModel();

        return _view.HierarchyView.Selected.Id;
    }
}
