using System.Collections.Generic;
using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// The prefab library over the command surface. An agent has to be able to make
/// one, place it, edit it and take it apart — and to see, before it is refused,
/// that what hangs under an instance is derived rather than editable.
/// </summary>
public sealed class PrefabCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public PrefabCommandTests()
    {
        _dispatcher.RunOk("new");
        _dispatcher.RunOk("create-element name=Wheel kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Wheel from=0,0,0 to=1,1,1 material=Default");
    }

    [Fact]
    public void MakingOneListsItAndLeavesAnInstanceBehind()
    {
        _dispatcher.RunOk("create-prefab element=Wheel");

        List<object?> prefabs = ListPrefabs();

        Assert.Single(prefabs);
        Assert.Equal("Wheel", Field(prefabs[0], "name"));
        Assert.Equal(true, Field(prefabs[0], "inUse"));
        Assert.Equal(false, Field(prefabs[0], "editing"));

        var element = (IReadOnlyDictionary<string, object?>)
            _dispatcher.RunOk("element element=Wheel").Value!;

        Assert.Equal("PrefabInstance", element["kind"]);
        Assert.Equal(Field(prefabs[0], "id"), element["source"]);
    }

    /// <summary>
    /// The read says why an edit will be refused, before it is refused: every
    /// element under an instance names the instance it belongs to.
    /// </summary>
    [Fact]
    public void TheContentsSayWhoseTheyAre()
    {
        _dispatcher.RunOk("create-prefab element=Wheel");

        var tree = (IReadOnlyDictionary<string, object?>)_dispatcher.RunOk("tree").Value!;
        object? instance = ((List<object?>)tree["elements"]!)[0];
        object? content = ((List<object?>)Field(instance, "children")!)[0];

        Assert.Equal(Field(instance, "id"), Field(content, "prefabContent"));
        Assert.Equal(8, Field(content, "voxels"));
    }

    [Fact]
    public void TheContentsRefuseAnEdit()
    {
        _dispatcher.RunOk("create-prefab element=Wheel");

        var tree = (IReadOnlyDictionary<string, object?>)_dispatcher.RunOk("tree").Value!;
        object? instance = ((List<object?>)tree["elements"]!)[0];
        object? content = ((List<object?>)Field(instance, "children")!)[0];

        var error = _dispatcher.RunFails($"rename-element element={Field(content, "id")} name=No");

        Assert.Contains("prefab instance content", error);
    }

    [Fact]
    public void EditingTheSourceReachesEveryInstance()
    {
        _dispatcher.RunOk("create-prefab element=Wheel");
        _dispatcher.RunOk("instantiate-prefab prefab=Wheel name=Second");
        _dispatcher.RunOk("edit-prefab prefab=Wheel");
        _dispatcher.RunOk("fill-box element=Wheel from=5,5,5 to=5,5,5 material=Default");
        _dispatcher.RunOk("activate-scene scene=Scene");

        var tree = (IReadOnlyDictionary<string, object?>)_dispatcher.RunOk("tree").Value!;

        foreach (object? instance in (List<object?>)tree["elements"]!)
        {
            object? content = ((List<object?>)Field(instance, "children")!)[0];

            Assert.Equal(9, Field(content, "voxels"));
        }
    }

    [Fact]
    public void EditingOpensTheLibraryEntryAsTheContext()
    {
        _dispatcher.RunOk("create-prefab element=Wheel");

        var opened = (IReadOnlyDictionary<string, object?>)
            _dispatcher.RunOk("edit-prefab prefab=Wheel").Value!;

        Assert.Equal("Wheel", opened["name"]);
        Assert.Equal(true, Field(ListPrefabs()[0], "editing"));

        // And the way back is the ordinary one.
        _dispatcher.RunOk("activate-scene scene=Scene");

        Assert.Equal(false, Field(ListPrefabs()[0], "editing"));
    }

    [Fact]
    public void UnpackingLeavesEditableElements()
    {
        _dispatcher.RunOk("create-prefab element=Wheel");
        _dispatcher.RunOk("unpack-prefab element=Wheel");

        var tree = (IReadOnlyDictionary<string, object?>)_dispatcher.RunOk("tree").Value!;
        object? group = ((List<object?>)tree["elements"]!)[0];
        object? content = ((List<object?>)Field(group, "children")!)[0];

        Assert.Equal("None", Field(group, "kind"));
        Assert.False(Has(content, "prefabContent"), "the contents are still marked derived");

        _dispatcher.RunOk($"rename-element element={Field(content, "id")} name=Mine");

        // The library entry stays, and nothing is using it any more.
        Assert.Equal(false, Field(ListPrefabs()[0], "inUse"));
    }

    [Fact]
    public void DeletingSaysHowManyItUnpacked()
    {
        _dispatcher.RunOk("create-prefab element=Wheel");
        _dispatcher.RunOk("instantiate-prefab prefab=Wheel name=Second");

        CommandResult result = _dispatcher.RunOk("delete-prefab prefab=Wheel");

        Assert.Equal(2, result.Field("unpacked"));
        Assert.Empty(ListPrefabs());
    }

    [Fact]
    public void APrefabCannotBePlacedInsideItself()
    {
        _dispatcher.RunOk("create-prefab element=Wheel");
        _dispatcher.RunOk("edit-prefab prefab=Wheel");

        var error = _dispatcher.RunFails("instantiate-prefab prefab=Wheel");

        Assert.Contains("contain itself", error);
    }

    [Fact]
    public void AnInstanceCanBeParentedUnderAnElement()
    {
        _dispatcher.RunOk("create-prefab element=Wheel");
        _dispatcher.RunOk("create-element name=Axle");
        _dispatcher.RunOk("instantiate-prefab prefab=Wheel parent=Axle name=Left");

        var element = (IReadOnlyDictionary<string, object?>)
            _dispatcher.RunOk("element element=Axle").Value!;

        Assert.Equal("Axle", element["name"]);

        var tree = (IReadOnlyDictionary<string, object?>)_dispatcher.RunOk("tree").Value!;

        foreach (object? row in (List<object?>)tree["elements"]!)
        {
            if (!Equals(Field(row, "name"), "Axle"))
                continue;

            object? child = ((List<object?>)Field(row, "children")!)[0];

            Assert.Equal("Left", Field(child, "name"));
            Assert.Equal("PrefabInstance", Field(child, "kind"));

            return;
        }

        Assert.Fail("no Axle row in the tree");
    }

    [Fact]
    public void RenamingTheLibraryEntryIsAddressableByName()
    {
        _dispatcher.RunOk("create-prefab element=Wheel");
        _dispatcher.RunOk("rename-prefab prefab=Wheel name=Roller");

        Assert.Equal("Roller", Field(ListPrefabs()[0], "name"));

        _dispatcher.RunOk("instantiate-prefab prefab=Roller");

        Assert.Equal(2, ((List<object?>)((IReadOnlyDictionary<string, object?>)
            _dispatcher.RunOk("tree").Value!)["elements"]!).Count);
    }

    /// <summary>The library is addressed by name, so a taken one is refused, not shared.</summary>
    [Fact]
    public void RenamingOntoATakenNameIsRefused()
    {
        _dispatcher.RunOk("create-prefab element=Wheel");
        _dispatcher.RunOk("create-element name=Axle kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Axle from=0,0,0 to=0,0,0 material=Default");
        _dispatcher.RunOk("create-prefab element=Axle");

        var error = _dispatcher.RunFails("rename-prefab prefab=Axle name=Wheel");

        Assert.Contains("already exists", error);
    }

    [Fact]
    public void AnUnknownPrefabIsRefusedByName()
    {
        Assert.Contains("No prefab called 'Nope'", _dispatcher.RunFails("edit-prefab prefab=Nope"));
    }

    private List<object?> ListPrefabs() => (List<object?>)_dispatcher.RunOk("prefabs").Value!;

    private static object? Field(object? row, string name) =>
        ((IReadOnlyDictionary<string, object?>)row!)[name];

    /// <summary>
    /// Absence is the answer for the keys that are only present when they mean
    /// something — an ordinary element carries no "prefabContent" at all.
    /// </summary>
    private static bool Has(object? row, string name) =>
        ((IReadOnlyDictionary<string, object?>)row!).ContainsKey(name);
}
