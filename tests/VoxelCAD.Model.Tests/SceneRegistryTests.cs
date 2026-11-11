using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// Scenes are project-level the way materials are: created, renamed and deleted
/// through the project, undoable like anything else, and each keeping its own
/// elements.
/// </summary>
public sealed class SceneRegistryTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;

    public SceneRegistryTests()
    {
        _scene = _project.Scenes[0];
    }

    private IHistory History => _project.History;

    [Fact]
    public void ANewProjectHasOneNamedScene()
    {
        Assert.Single(_project.Scenes);
        Assert.Equal("Scene", _scene.Name);
    }

    [Fact]
    public void ASceneCanBeCreatedAndFoundById()
    {
        Guid id = _project.CreateScene("Housing", new Vector3I(8, 8, 8));

        Assert.True(_project.TryGetScene(id, out IScene created));
        Assert.Equal("Housing", created.Name);
        Assert.Equal(new Vector3I(8, 8, 8), created.Size);
        Assert.Equal(2, _project.Scenes.Count);
    }

    [Fact]
    public void CreatingASceneIsUndoable()
    {
        Guid id = _project.CreateScene("Housing", _scene.Size);

        History.Undo();

        Assert.Single(_project.Scenes);
        Assert.False(_project.TryGetScene(id, out _));

        History.Redo();

        Assert.Equal(2, _project.Scenes.Count);
        Assert.True(_project.TryGetScene(id, out _));
    }

    [Fact]
    public void TheLastSceneCannotBeDeleted()
    {
        Assert.Throws<InvalidOperationException>(() => _project.DeleteScene(_scene.Id));
    }

    /// <summary>
    /// The command holds the detached scene itself rather than a copy, so undo
    /// restores identity — the ids inside it are the ones history records name.
    /// </summary>
    [Fact]
    public void UndoingADeletionBringsBackTheSameSceneAndItsContents()
    {
        Guid sceneId = _project.CreateScene("Housing", _scene.Size);
        _project.TryGetScene(sceneId, out IScene housing);
        Guid element = housing.CreateElement(housing.Id, ComponentKind.None, "Bracket");

        _project.DeleteScene(sceneId);
        Assert.Single(_project.Scenes);

        History.Undo();

        Assert.True(_project.TryGetScene(sceneId, out IScene restored));
        Assert.Same(housing, restored);
        Assert.Equal(element, restored.Children[0].Id);
    }

    [Fact]
    public void RenamingASceneIsUndoableAndRecordsNothingWhenTheNameIsTheSame()
    {
        var before = History.Records.Count;

        _project.RenameScene(_scene.Id, "Scene");
        Assert.Equal(before, History.Records.Count);

        _project.RenameScene(_scene.Id, "Housing");
        Assert.Equal("Housing", _scene.Name);

        History.Undo();
        Assert.Equal("Scene", _scene.Name);
    }

    [Fact]
    public void ScenesAnnounceThemselvesOnTheProjectStream()
    {
        var changesList = new List<ProjectChange>();
        _project.Changed += changesList.Add;

        Guid id = _project.CreateScene("Housing", _scene.Size);
        _project.RenameScene(id, "Case");
        _project.DeleteScene(id);

        Assert.Equal(
            new[]
            {
                ProjectChangeKind.SceneAdded,
                ProjectChangeKind.SceneChanged,
                ProjectChangeKind.SceneRemoved
            },
            changesList.ConvertAll(change => change.Kind));

        Assert.All(changesList, change => Assert.Equal(id, change.TargetId));
        Assert.Equal(1, changesList[0].Index);
    }

    [Fact]
    public void EachSceneKeepsItsOwnElements()
    {
        Guid id = _project.CreateScene("Housing", _scene.Size);
        _project.TryGetScene(id, out IScene housing);

        Guid here = _scene.CreateElement(_scene.Id, ComponentKind.None, "Here");
        housing.CreateElement(housing.Id, ComponentKind.None, "There");

        Assert.Single(_scene.Children);
        Assert.Single(housing.Children);
        Assert.False(housing.TryGetElement(here, out _));
    }

    /// <summary>
    /// The editor follows this to decide which scene to show when something is
    /// undone, so an action that belongs to no scene has to say so as clearly as
    /// one that does.
    /// </summary>
    [Fact]
    public void RecordsCarryTheSceneTheActionHappenedIn()
    {
        Guid id = _project.CreateScene("Housing", _scene.Size);
        _project.TryGetScene(id, out IScene housing);

        housing.CreateElement(housing.Id, ComponentKind.None, "Bracket");
        _project.CreateMaterial("Steel", new Color(0.5f, 0.5f, 0.5f));

        Assert.Equal(id, History.Records[^3].SceneId);
        Assert.Equal(id, History.Records[^2].SceneId);
        Assert.Equal(Guid.Empty, History.Records[^1].SceneId);
    }

    [Fact]
    public void TheChangePayloadNamesTheRecordThatMoved()
    {
        Guid id = _project.CreateScene("Housing", _scene.Size);
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");

        var changesList = new List<HistoryChange>();
        History.Changed += changesList.Add;

        History.Undo();
        History.Undo();
        History.Redo();

        Assert.Equal(new[] { 1, 0, 0 }, changesList.ConvertAll(change => change.Index));
        Assert.Equal(
            new[] { _scene.Id, id, id },
            changesList.ConvertAll(change => change.SceneId));
    }

    /// <summary>A walk that moves nothing has no record to point at.</summary>
    [Fact]
    public void AHistoryWalkThatMovesNothingCarriesNoScene()
    {
        _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");

        var changesList = new List<HistoryChange>();
        History.Changed += changesList.Add;

        History.UndoTo(History.CurrentIndex);

        Assert.Equal(-1, changesList[0].Index);
        Assert.Equal(Guid.Empty, changesList[0].SceneId);
    }

    [Fact]
    public void AWalkAcrossScenesEndsUpInTheOneItStoppedIn()
    {
        Guid id = _project.CreateScene("Housing", _scene.Size);
        _project.TryGetScene(id, out IScene housing);

        _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");
        housing.CreateElement(housing.Id, ComponentKind.None, "Case");

        var changesList = new List<HistoryChange>();
        History.Changed += changesList.Add;

        // Back past the element in Housing to the one in the first scene.
        History.UndoTo(0);

        Assert.Equal(_scene.Id, changesList[0].SceneId);
    }
}
