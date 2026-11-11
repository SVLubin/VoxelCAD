using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;
using Godot;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The contract that makes swapping documents cheap: a presenter fills its view
/// from scratch when it is built, and stops touching it when it is disposed.
/// </summary>
public sealed class DocumentPresenterTests
{
    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakeSceneView _scene = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();

    [Fact]
    public void TheConstructorFillsTheViewsFromScratch()
    {
        // Whatever the previous document left behind.
        Guid staleId = Guid.NewGuid();
        var stale = new SelectionTarget(SelectionKind.Scene, staleId);
        _hierarchy.SetRoot(stale, "Old scene");
        _scene.SetRoot(staleId, new Vector3I(8, 8, 8));

        using DocumentPresenter document = Create();

        Assert.Equal(1, _hierarchy.ClearCount);
        Assert.Equal(1, _scene.ClearCount);
        Assert.Equal(document.Scene.Id, _hierarchy.SceneId);
        Assert.Equal(document.Scene.Id, _scene.RootId);
        Assert.DoesNotContain(stale, _hierarchy.Items.Keys);
    }

    /// <summary>
    /// A recovered snapshot matches nothing on disk, so it is modified from birth
    /// and stays so through every undo — until a save gives it a record to match.
    /// </summary>
    [Fact]
    public void ADocumentOpenedModifiedIsModifiedWithAnEmptyHistoryUntilItIsSaved()
    {
        using DocumentPresenter document = Create(openedModified: true);

        Assert.True(document.IsModified);

        _project.Scenes[0].CreateElement(_project.Scenes[0].Id, ComponentKind.None, "Group");
        _project.History.Undo();
        Assert.True(document.IsModified);

        document.MarkSaved();
        Assert.False(document.IsModified);
    }

    [Fact]
    public void ADisposedPresenterStopsDrivingItsViews()
    {
        DocumentPresenter document = Create();
        document.Dispose();

        _project.Scenes[0].CreateElement(_project.Scenes[0].Id, ComponentKind.None, "Group");

        // Only what the constructor put there: the project row, the three
        // categories, the one scene and the default material.
        Assert.Equal(6, _hierarchy.Items.Count);
        Assert.Empty(_history.Labels);
    }

    [Fact]
    public void DisposingTwiceUnsubscribesOnce()
    {
        DocumentPresenter document = Create();

        document.Dispose();
        document.Dispose();

        Assert.Equal(6, _hierarchy.Items.Count);
    }

    /// <summary>
    /// Switching scenes is the document swap one level down: the panels bound to
    /// a scene are rebuilt, and the selection goes with them because its ids
    /// address the scene being left.
    /// </summary>
    [Fact]
    public void ActivatingAnotherSceneRebuildsThePanelsAroundIt()
    {
        using DocumentPresenter document = Create();

        Guid elementId = document.Scene.CreateElement(document.Scene.Id, ComponentKind.None, "Here");
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, elementId));

        Guid sceneId = _project.CreateScene("Housing", new Vector3I(8, 8, 8));
        _project.TryGetScene(sceneId, out IScene housing);
        document.SetActiveScene(housing);

        Assert.Equal(sceneId, document.Scene.Id);
        Assert.Equal(sceneId, _hierarchy.SceneId);
        Assert.Equal(sceneId, _scene.RootId);
        Assert.Equal(default, _session.ActiveTarget);
        Assert.Empty(_session.Selection.Targets);
        Assert.DoesNotContain("Here", _hierarchy.Items.Values);
    }

    /// <summary>
    /// The history belongs to the project, not to a scene, so it must not be
    /// thrown away and rebuilt when the scene changes underneath it.
    /// </summary>
    [Fact]
    public void SwitchingScenesLeavesTheHistoryPanelAlone()
    {
        using DocumentPresenter document = Create();

        document.Scene.CreateElement(document.Scene.Id, ComponentKind.None, "Here");
        Guid sceneId = _project.CreateScene("Housing", new Vector3I(8, 8, 8));
        _project.TryGetScene(sceneId, out IScene housing);

        document.SetActiveScene(housing);

        Assert.Equal(new[] { "Create Here", "Create scene Housing" }, _history.Labels);
    }

    [Fact]
    public void AFreshDocumentHasNothingToLose()
    {
        using DocumentPresenter document = Create();

        Assert.False(document.IsModified);
    }

    [Fact]
    public void AnEditMakesItModified()
    {
        using DocumentPresenter document = Create();

        document.Scene.CreateElement(document.Scene.Id, ComponentKind.None, "Group");

        Assert.True(document.IsModified);
    }

    [Fact]
    public void UndoingBackToTheSavedPointClearsIt()
    {
        using DocumentPresenter document = Create();

        document.Scene.CreateElement(document.Scene.Id, ComponentKind.None, "Group");
        document.MarkSaved();
        document.Scene.CreateElement(document.Scene.Id, ComponentKind.None, "Another");

        Assert.True(document.IsModified);

        document.Project.History.Undo();

        Assert.False(document.IsModified);
    }

    /// <summary>
    /// The case an index comparison gets wrong. Undoing past the saved point and
    /// then editing again lands on the same history index with a different record
    /// in it, and the document is emphatically not the one on disk.
    /// </summary>
    [Fact]
    public void EditingOverAnUndoneRecordStaysModified()
    {
        using DocumentPresenter document = Create();

        document.Scene.CreateElement(document.Scene.Id, ComponentKind.None, "Saved");
        document.MarkSaved();

        document.Project.History.Undo();
        document.Scene.CreateElement(document.Scene.Id, ComponentKind.None, "Different");

        Assert.Equal(0, document.Project.History.CurrentIndex);
        Assert.True(document.IsModified);
    }

    private DocumentPresenter Create(bool openedModified = false)
    {
        _session.CurrentMaterialId = _project.Materials[0].Id;

        return new DocumentPresenter(
            _project,
            _project.Scenes[0],
            _hierarchy,
            _inspector,
            _history,
            _scene,
            _session,
            new FakeDialogService(),
            new FakePreferences(),
            _ => { },
            openedModified);
    }
}
