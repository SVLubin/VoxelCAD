using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// What happens to the editor when the document underneath it is replaced —
/// whether the user opened a file or an agent did.
/// </summary>
public sealed class DocumentSwapTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"voxelcad-{Guid.NewGuid():N}.vcad");

    public DocumentSwapTests()
    {
        _shell = new ShellPresenter(_view, new FakePreferences());
    }

    public void Dispose()
    {
        _shell.Dispose();

        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void ThePanelsAreClearedAndRefilledFromScratch()
    {
        int hierarchyClears = _view.HierarchyView.ClearCount;
        int sceneClears = _view.SceneView.ClearCount;
        Guid before = _view.HierarchyView.SceneId;

        _view.RaiseNew();

        Assert.Equal(hierarchyClears + 1, _view.HierarchyView.ClearCount);
        Assert.Equal(sceneClears + 1, _view.SceneView.ClearCount);
        Assert.NotEqual(before, _view.HierarchyView.SceneId);
        Assert.Equal(_view.HierarchyView.SceneId, _view.SceneView.RootId);
    }

    /// <summary>
    /// The history panel holds one thing a rebuild does not touch — which of an
    /// agent's sessions the user folded away — and a session number means nothing
    /// in the next document's history. So it is emptied like every other panel
    /// rather than merely refilled.
    /// </summary>
    [Fact]
    public void TheHistoryPanelIsEmptiedAndNotOnlyRefilled()
    {
        int clears = _view.HistoryView.ClearCount;

        _view.RaiseNew();

        Assert.Equal(clears + 1, _view.HistoryView.ClearCount);
    }

    [Fact]
    public void TheSelectionIsDroppedBecauseItsIdsMeanNothingHere()
    {
        _view.RaiseCreateGroup();
        Assert.NotEqual(default, _view.HierarchyView.Selected);
        Assert.True(_view.DeleteEnabled);

        Discard();
        _view.RaiseNew();

        Assert.Equal(default, _view.HierarchyView.Selected);
        Assert.False(_view.DeleteEnabled);
    }

    [Fact]
    public void TheBrushPicksUpTheNewProjectsMaterial()
    {
        Guid before = _view.SceneView.BrushMaterialId;

        _view.RaiseNew();

        Assert.NotEqual(before, _view.SceneView.BrushMaterialId);
        Assert.Contains(_view.SceneView.BrushMaterialId, _view.SceneView.Materials.Keys);
    }

    [Fact]
    public void UndoStartsOverWithTheNewDocument()
    {
        _view.RaiseCreateGroup();
        Assert.True(_view.CanUndo);

        Discard();
        _view.RaiseNew();

        Assert.False(_view.CanUndo);
        Assert.Empty(_view.HistoryView.Labels);
    }

    /// <summary>
    /// The agent drives the editor the user has, so its <c>open</c> has to land in
    /// the window rather than swapping a project out from behind it.
    /// </summary>
    [Fact]
    public void AnAgentsOpenRebindsTheEditor()
    {
        ICommandDispatcher dispatcher = TakeControl();

        dispatcher.Execute("create-element", new Dictionary<string, string> { ["name"] = "Widget" });
        dispatcher.Execute("save", new Dictionary<string, string> { ["path"] = _path });
        dispatcher.Execute("new", new Dictionary<string, string>());

        Assert.DoesNotContain("Widget", _view.HierarchyView.Items.Values);

        CommandResult result = dispatcher.Execute(
            "open",
            new Dictionary<string, string> { ["path"] = _path });

        Assert.True(result.Ok, result.Error);
        Assert.Contains("Widget", _view.HierarchyView.Items.Values);
        Assert.Equal($"{Path.GetFileName(_path)} — VoxelCAD", _view.WindowTitle);
    }

    [Fact]
    public void AnAgentsSaveClearsTheUnsavedMark()
    {
        _view.RaiseCreateGroup();
        Assert.StartsWith("•", _view.WindowTitle);

        TakeControl().Execute("save", new Dictionary<string, string> { ["path"] = _path });

        Assert.Equal($"{Path.GetFileName(_path)} — VoxelCAD", _view.WindowTitle);
    }

    /// <summary>
    /// Control is held over the editor, not over a document. Opening a file must
    /// not quietly hand it back.
    /// </summary>
    [Fact]
    public void AnAgentKeepsControlAcrossTheSwap()
    {
        ICommandDispatcher dispatcher = TakeControl();

        dispatcher.Execute("new", new Dictionary<string, string>());
        CommandResult result = dispatcher.Execute(
            "create-element",
            new Dictionary<string, string> { ["name"] = "Widget" });

        Assert.True(result.Ok, result.Error);
        Assert.Contains("Create Widget", _view.HistoryView.Labels);
    }

    private ICommandDispatcher TakeControl()
    {
        ICommandDispatcher dispatcher = _shell.CreateAgentDispatchers().ForClient("test-client");

        dispatcher.Execute("take-control", new Dictionary<string, string> { ["name"] = "Claude" });
        _shell.Tick(10.0);

        return dispatcher;
    }

    private void Discard() => _view.OnAskConfirm = _ => Task.FromResult(ConfirmResult.Deny);
}
