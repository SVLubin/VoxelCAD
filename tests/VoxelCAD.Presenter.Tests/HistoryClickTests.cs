using System;
using System.Linq;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// A click on a history row walks the document there, and every panel keeps
/// telling the truth afterwards. Pinned after being broken live: the real
/// panel's rebuild used to run inside the click's own engine dispatch, which
/// the engine refuses — the model undid while the panel kept its stale rows
/// and then swallowed every later click. The engine half of the fix
/// (DeferredReport) can only be verified by driving; what these hold is the
/// whole answer a click is owed once it is allowed to arrive.
/// </summary>
public sealed class HistoryClickTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    public HistoryClickTests()
    {
        _shell = new ShellPresenter(_view, new FakePreferences());

        _view.RaiseCreateVoxelModel();
        _view.RaiseCreateGroup();
    }

    public void Dispose() => _shell.Dispose();

    [Fact]
    public void ClickingARecordWalksTheDocumentThere()
    {
        _view.HistoryView.RaiseRecordClicked(0);

        Assert.Equal(0, _view.HistoryView.CurrentIndex);
    }

    /// <summary>The panel must be re-fed after its own click, not just the model moved.</summary>
    [Fact]
    public void TheClicksAnswerComesBackToThePanel()
    {
        int rowsBefore = _view.HistoryView.Rows.Count;

        _view.HistoryView.RaiseRecordClicked(0);

        Assert.Equal(rowsBefore, _view.HistoryView.Rows.Count);
        Assert.Equal(0, _view.HistoryView.CurrentIndex);
    }

    [Fact]
    public void TheOriginRowUndoesEverything()
    {
        _view.HistoryView.RaiseRecordClicked(-1);

        Assert.Equal(-1, _view.HistoryView.CurrentIndex);
    }

    /// <summary>
    /// The project tree follows a history click: the element undone away has no
    /// row any more. This is the symptom the breakage actually showed — a model
    /// that rolled back under panels still showing the old state.
    /// </summary>
    [Fact]
    public void TheProjectTreeFollowsAHistoryClick()
    {
        SelectionTarget scene = new(SelectionKind.Scene, _view.HierarchyView.SceneId);
        Assert.NotEmpty(_view.HierarchyView.ChildrenOf(scene));

        _view.HistoryView.RaiseRecordClicked(-1);

        Assert.Empty(_view.HierarchyView.ChildrenOf(scene));
    }

    /// <summary>Clicking forward of the current record re-applies — redo by click.</summary>
    [Fact]
    public void ClickingAnUndoneRecordReappliesIt()
    {
        SelectionTarget scene = new(SelectionKind.Scene, _view.HierarchyView.SceneId);
        int elementsBefore = _view.HierarchyView.ChildrenOf(scene).Count;

        _view.HistoryView.RaiseRecordClicked(-1);
        _view.HistoryView.RaiseRecordClicked(1);

        Assert.Equal(1, _view.HistoryView.CurrentIndex);
        Assert.Equal(elementsBefore, _view.HierarchyView.ChildrenOf(scene).Count);
    }

    /// <summary>
    /// The panel stays alive across its own clicks — the live breakage's second
    /// half was a first click wedging the panel so no later one reported.
    /// </summary>
    [Fact]
    public void ASecondClickStillWalks()
    {
        _view.HistoryView.RaiseRecordClicked(0);
        _view.HistoryView.RaiseRecordClicked(1);
        _view.HistoryView.RaiseRecordClicked(-1);

        Assert.Equal(-1, _view.HistoryView.CurrentIndex);
    }

    /// <summary>What a click leaves behind reads back as undone rows, not fewer rows.</summary>
    [Fact]
    public void UndoneRecordsKeepTheirRows()
    {
        _view.HistoryView.RaiseRecordClicked(-1);

        Assert.Equal(2, _view.HistoryView.Rows.Count);
        Assert.True(_view.HistoryView.Rows.All(row => row.Label.Length > 0));
    }
}
