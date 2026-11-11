using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// Scenes over the command surface. An agent has to be able to see and reach
/// every scene of a project, and to say which one it is working in — otherwise
/// the tree it reads and the tree the user sees are different documents.
/// </summary>
public sealed class SceneCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public SceneCommandTests()
    {
        _dispatcher.RunOk("new");
    }

    [Fact]
    public void ANewProjectListsOneActiveScene()
    {
        List<object?> scenes = ListScenes();

        Assert.Single(scenes);
        Assert.Equal("Scene", Field(scenes[0], "name"));
        Assert.Equal(true, Field(scenes[0], "active"));
    }

    [Fact]
    public void CreatingASceneMakesItTheOneCommandsWorkIn()
    {
        _dispatcher.RunOk("create-scene name=Housing");
        _dispatcher.RunOk("create-element name=Bracket");

        List<object?> scenes = ListScenes();

        Assert.Equal(new object?[] { false, true }, scenes.Select(scene => Field(scene, "active")));
        Assert.Equal(0, Field(scenes[0], "elements"));
        Assert.Equal(1, Field(scenes[1], "elements"));
    }

    [Fact]
    public void ASceneCanBeActivatedByName()
    {
        _dispatcher.RunOk("create-scene name=Housing");
        _dispatcher.RunOk("activate-scene scene=Scene");
        _dispatcher.RunOk("create-element name=Bracket");

        List<object?> scenes = ListScenes();

        Assert.Equal(1, Field(scenes[0], "elements"));
        Assert.Equal(0, Field(scenes[1], "elements"));
    }

    [Fact]
    public void AnUnknownSceneIsRefusedRatherThanGuessed()
    {
        Assert.Contains("No scene called 'Housing'", _dispatcher.RunFails("activate-scene scene=Housing"));
    }

    [Fact]
    public void AnAmbiguousSceneNameIsRefusedWithTheCandidates()
    {
        _dispatcher.RunOk("create-scene name=Housing");
        _dispatcher.RunOk("rename-scene scene=Scene name=Housing");

        Assert.Contains("matches 2 scenes", _dispatcher.RunFails("activate-scene scene=Housing"));
    }

    [Fact]
    public void ASceneTakesTheActiveOnesRegionUnlessToldOtherwise()
    {
        _dispatcher.RunOk("new size=8,9,10");
        _dispatcher.RunOk("create-scene name=Housing");

        Assert.Equal(new[] { 8, 9, 10 }, (int[])Field(ListScenes()[1], "size")!);
    }

    [Fact]
    public void DeletingTheLastSceneIsRefused()
    {
        Assert.Contains("at least one scene", _dispatcher.RunFails("delete-scene scene=Scene"));
    }

    /// <summary>
    /// The deleted scene was the active one, so something else has to be — the
    /// alternative is a dispatcher pointing at a scene that no longer exists.
    /// </summary>
    [Fact]
    public void DeletingTheActiveSceneFallsBackToAnother()
    {
        _dispatcher.RunOk("create-scene name=Housing");
        _dispatcher.RunOk("delete-scene scene=Housing");

        List<object?> scenes = ListScenes();

        Assert.Single(scenes);
        Assert.Equal(true, Field(scenes[0], "active"));
    }

    [Fact]
    public void ElementsBelongToTheSceneTheyWereMadeIn()
    {
        _dispatcher.RunOk("create-element name=Here");
        _dispatcher.RunOk("create-scene name=Housing");

        Assert.Contains("No element called 'Here'", _dispatcher.RunFails("delete-element element=Here"));
    }

    /// <summary>
    /// The editor follows this to decide which scene to show when something is
    /// undone, so the record has to carry it out to a transport as well.
    /// </summary>
    [Fact]
    public void HistoryRecordsSayWhichSceneTheyBelongTo()
    {
        string sceneId = _dispatcher.RunOk("create-scene name=Housing").Id();
        _dispatcher.RunOk("create-element name=Bracket");
        _dispatcher.RunOk("create-material name=Steel color=0.5,0.5,0.5");

        var history = (IReadOnlyDictionary<string, object?>)_dispatcher.RunOk("history").Value!;
        var records = (List<object?>)history["records"]!;

        Assert.Equal(sceneId, Field(records[1], "scene"));
        Assert.Null(Field(records[2], "scene"));
    }

    [Fact]
    public void TheTreeSaysWhichSceneItIsDescribing()
    {
        _dispatcher.RunOk("create-scene name=Housing");

        var tree = (IReadOnlyDictionary<string, object?>)_dispatcher.RunOk("tree").Value!;

        Assert.Equal("Housing", tree["name"]);
    }

    private List<object?> ListScenes() => (List<object?>)_dispatcher.RunOk("scenes").Value!;

    private static object? Field(object? row, string name) =>
        ((IReadOnlyDictionary<string, object?>)row!)[name];
}
