using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// Copying as an agent reaches it. The editor's clipboard is session state and has
/// no command form — the same reasoning that keeps the selection off the surface —
/// so what an agent gets is the whole gesture in one step: copy this, put it there.
/// </summary>
public sealed class DuplicateCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public DuplicateCommandTests()
    {
        _dispatcher.RunOk("new");
    }

    [Fact]
    public void AnElementIsCopiedBesideItself()
    {
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=1,1,1 material=Default");

        CommandResult result = _dispatcher.RunOk("duplicate-element element=Model");

        Assert.Equal("Model 2", result.Field("name"));
        Assert.Equal("Scene", result.Field("scene"));
        Assert.Equal(new object?[] { "Model", "Model 2" }, TopLevelNames());
    }

    /// <summary>Deep: the cells come with it, and they are the copy's own.</summary>
    [Fact]
    public void TheCopyHasItsOwnCells()
    {
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=1,1,1 material=Default");
        _dispatcher.RunOk("duplicate-element element=Model");

        Assert.Equal(8, Filled("Model 2"));

        _dispatcher.RunOk("set-voxels element=Model cells=0,0,0 material=none");

        Assert.Equal(7, Filled("Model"));
        Assert.Equal(8, Filled("Model 2"));
    }

    [Fact]
    public void TheWholeSubtreeComesWithIt()
    {
        _dispatcher.RunOk("create-element name=Assembly");
        _dispatcher.RunOk("create-element name=Wheel kind=VoxelModel parent=Assembly");

        _dispatcher.RunOk("duplicate-element element=Assembly");

        // Only the root is renamed, so the two wheels are ambiguous by name — which
        // is the honest answer, and the reason ids exist.
        Assert.Contains("matches 2 elements", _dispatcher.RunFails("element element=Wheel"));
        Assert.Equal(1, Children("Assembly 2"));
    }

    [Fact]
    public void ACopyCanBeAskedForByName()
    {
        _dispatcher.RunOk("create-element name=Model");

        CommandResult result = _dispatcher.RunOk("duplicate-element element=Model name=Mirror");

        Assert.Equal("Mirror", result.Field("name"));
    }

    [Fact]
    public void ACopyCanBePutUnderAParent()
    {
        _dispatcher.RunOk("create-element name=Model");
        _dispatcher.RunOk("create-element name=Group");

        _dispatcher.RunOk("duplicate-element element=Model parent=Group");

        Assert.Equal(1, Children("Group"));
        Assert.Equal(new object?[] { "Model", "Group" }, TopLevelNames());
    }

    /// <summary>
    /// The parent is read in the scene the copy lands in — resolving it against the
    /// active one would find the wrong tree, or none.
    /// </summary>
    [Fact]
    public void ACopyCanCrossIntoAnotherScene()
    {
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=1,1,1 material=Default");
        _dispatcher.RunOk("create-scene name=Housing");
        _dispatcher.RunOk("create-element name=Shelf");
        _dispatcher.RunOk("activate-scene scene=Scene");

        CommandResult result = _dispatcher.RunOk(
            "duplicate-element element=Model scene=Housing parent=Shelf");

        Assert.Equal("Housing", result.Field("scene"));

        // The name was free over there, so it kept it.
        Assert.Equal("Model", result.Field("name"));

        _dispatcher.RunOk("activate-scene scene=Housing");

        Assert.Equal(8, Filled("Model"));
        Assert.Equal(1, Children("Shelf"));
    }

    [Fact]
    public void CopyingAnElementIsOneUndoableStep()
    {
        _dispatcher.RunOk("create-element name=Assembly");
        _dispatcher.RunOk("create-element name=Wheel kind=VoxelModel parent=Assembly");

        _dispatcher.RunOk("duplicate-element element=Assembly");
        _dispatcher.RunOk("undo");

        Assert.Equal(new object?[] { "Assembly" }, TopLevelNames());
    }

    [Fact]
    public void AnUnknownElementIsRefusedRatherThanGuessed()
    {
        Assert.Contains(
            "No element called 'Ghost'", _dispatcher.RunFails("duplicate-element element=Ghost"));
    }

    [Fact]
    public void ASceneIsCopiedWithEverythingInIt()
    {
        _dispatcher.RunOk("create-scene name=Housing size=8,9,10");
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=1,1,1 material=Default");

        CommandResult result = _dispatcher.RunOk("duplicate-scene scene=Housing");

        Assert.Equal("Housing 2", result.Field("name"));

        List<object?> scenes = ListScenes();

        Assert.Equal(3, scenes.Count);
        Assert.Equal(1, Field(scenes[2], "elements"));
        Assert.Equal(new[] { 8, 9, 10 }, Field(scenes[2], "size"));
    }

    /// <summary>
    /// The same as a created one, and said rather than left to happen: in the editor
    /// the history takes the user to the scene a record belongs to, so without this
    /// the console and the window would quietly disagree about where the caller is.
    /// </summary>
    [Fact]
    public void ACopiedSceneBecomesTheActiveOne()
    {
        _dispatcher.RunOk("create-scene name=Housing");
        _dispatcher.RunOk("duplicate-scene scene=Housing");

        List<object?> scenes = ListScenes();

        Assert.Equal(new object?[] { false, false, true }, scenes.Select(scene => Field(scene, "active")));
    }

    [Fact]
    public void CopyingASceneIsOneUndoableStep()
    {
        _dispatcher.RunOk("create-scene name=Housing");
        _dispatcher.RunOk("duplicate-scene scene=Housing");
        _dispatcher.RunOk("undo");

        Assert.Equal(2, ListScenes().Count);
    }

    [Fact]
    public void AMaterialIsCopiedAsOneOfItsOwn()
    {
        _dispatcher.RunOk("create-material name=Paint color=0.2,0.4,0.6");
        _dispatcher.RunOk("set-material-appearance material=Paint roughness=0.25");

        CommandResult result = _dispatcher.RunOk("duplicate-material material=Paint");

        Assert.Equal("Paint 2", result.Field("name"));

        object? copy = ListMaterials().Single(material => Equals(Field(material, "name"), "Paint 2"));

        Assert.Equal(new[] { 0.2f, 0.4f, 0.6f, 1f }, Field(copy, "color"));
        Assert.Equal(0.25f, Field(Field(copy, "appearance"), "roughness"));
    }

    /// <summary>
    /// Voxels name a material by id, and the copy's id is not the one they name.
    /// Recolouring the copy is how a variant is started, not how the original is
    /// changed by accident.
    /// </summary>
    [Fact]
    public void ACopiedMaterialRepaintsNothing()
    {
        _dispatcher.RunOk("create-material name=Paint color=1,0,0");
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=0,0,0 material=Paint");

        _dispatcher.RunOk("duplicate-material material=Paint");
        _dispatcher.RunOk("set-material-color material=\"Paint 2\" color=0,0,1");

        object? original = ListMaterials().Single(material => Equals(Field(material, "name"), "Paint"));

        Assert.Equal(new[] { 1f, 0f, 0f, 1f }, Field(original, "color"));
        Assert.Equal(true, Field(original, "inUse"));
        Assert.Equal(
            false,
            Field(ListMaterials().Single(m => Equals(Field(m, "name"), "Paint 2")), "inUse"));
    }

    private int Filled(string element) =>
        (int)_dispatcher.RunOk($"bounds element=\"{element}\"").Field("voxels")!;

    /// <summary>Counted off the tree, which is the read that goes deep.</summary>
    private int Children(string element) =>
        ((List<object?>)Field(Find(TopLevel(), element), "children")!).Count;

    private object? Find(IEnumerable<object?> rows, string name)
    {
        foreach (object? row in rows)
        {
            if (Equals(Field(row, "name"), name))
                return row;

            if (Find((List<object?>)Field(row, "children")!, name) is { } found)
                return found;
        }

        return null;
    }

    private List<object?> TopLevel() => (List<object?>)_dispatcher.RunOk("tree").Field("elements")!;

    private IEnumerable<object?> TopLevelNames() =>
        TopLevel().Select(element => Field(element, "name"));

    private List<object?> ListScenes() => (List<object?>)_dispatcher.RunOk("scenes").Value!;

    private List<object?> ListMaterials() => (List<object?>)_dispatcher.RunOk("materials").Value!;

    private static object? Field(object? row, string name) =>
        ((IReadOnlyDictionary<string, object?>)row!)[name];
}
