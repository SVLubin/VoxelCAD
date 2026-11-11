using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Commands;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>Replacing a material as an agent reaches it, over each of the three scopes.</summary>
public sealed class ReplaceMaterialCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public ReplaceMaterialCommandTests()
    {
        _dispatcher.RunOk("new");
        _dispatcher.RunOk("create-material name=Red color=1,0,0");
        _dispatcher.RunOk("create-material name=Blue color=0,0,1");
        _dispatcher.RunOk("create-element name=A kind=VoxelModel");
        _dispatcher.RunOk("create-element name=B kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=A from=0,0,0 to=1,0,0 material=Red");
        _dispatcher.RunOk("fill-box element=B from=5,0,0 to=6,0,0 material=Red");
    }

    /// <summary>
    /// Every element of the scene, and <b>no other scene</b> — which needs a second
    /// scene to say at all: with one, a walk over the whole project would pass
    /// this test exactly as a walk over the scene does.
    /// </summary>
    [Fact]
    public void TheDefaultScopeIsTheSceneAndStopsAtIt()
    {
        _dispatcher.RunOk("create-scene name=Other size=8,8,8");
        _dispatcher.RunOk("activate-scene scene=Other");
        _dispatcher.RunOk("create-element name=C kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=C from=0,0,0 to=0,0,0 material=Red");
        _dispatcher.RunOk("activate-scene scene=Scene");

        CommandResult result = _dispatcher.RunOk("replace-material from=Red to=Blue");

        Assert.Equal("BB", Row("A"));
        Assert.Equal("BB", Row("B"));

        // The other scene's cell still wears it, so the material is still in use —
        // which is also what says a scene-scope replace does not make it deletable.
        Assert.Equal(true, result.Field("stillInUse"));

        _dispatcher.RunOk("activate-scene scene=Other");

        Assert.Equal("R", Row("C"));
    }

    [Fact]
    public void ElementScopeNeedsAnElementAndStopsThere()
    {
        Assert.Contains("needs 'element'",
            _dispatcher.RunFails("replace-material from=Red to=Blue scope=element"));

        CommandResult result = _dispatcher.RunOk(
            "replace-material from=Red to=Blue scope=element element=A");

        Assert.Equal(true, result.Field("stillInUse"));
        Assert.Equal("BB", Row("A"));
        Assert.Equal("RR", Row("B"));
    }

    [Fact]
    public void ProjectScopeReachesAnotherScene()
    {
        _dispatcher.RunOk("create-scene name=Other size=8,8,8");
        _dispatcher.RunOk("activate-scene scene=Other");
        _dispatcher.RunOk("create-element name=C kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=C from=0,0,0 to=0,0,0 material=Red");

        CommandResult result = _dispatcher.RunOk("replace-material from=Red to=Blue scope=project");

        Assert.Equal(false, result.Field("stillInUse"));
        Assert.Equal("B", Row("C"));

        // And the old one can go now, which is the point of the project scope.
        _dispatcher.RunOk("delete-material material=Red");
    }

    [Fact]
    public void AShippedMaterialWorksOnEitherSide()
    {
        // The library is installed by the composition root, so a test that wants
        // it says so; it resolves by id and by name like the project's own.
        StandardMaterials.Install();

        CommandResult shipped = _dispatcher.RunOk("standard-materials");
        var list = (List<object?>)shipped.Field("materials")!;
        var first = (IReadOnlyDictionary<string, object?>)list[0]!;
        var id = (string)first["id"]!;

        _dispatcher.RunOk($"replace-material from=Red to={id}");
        Assert.Equal(id, MaterialOf("A"));

        _dispatcher.RunOk($"replace-material from={id} to=Blue");

        Assert.Equal("BB", Row("A"));
    }

    /// <summary>
    /// 'element' narrows nothing outside scope=element, and taken silently it
    /// would turn a replace meant for one model into one over the whole scene.
    /// Refused by name, and nothing is written.
    /// </summary>
    [Fact]
    public void AnElementOutsideItsOwnScopeIsRefusedAndNothingIsWritten()
    {
        Assert.Contains("scope=element",
            _dispatcher.RunFails("replace-material from=Red to=Blue element=A"));
        Assert.Contains("scope=element",
            _dispatcher.RunFails("replace-material from=Red to=Blue scope=project element=A"));

        Assert.Equal("RR", Row("A"));
        Assert.Equal("RR", Row("B"));
    }

    [Fact]
    public void AnUnknownScopeIsRefusedByName()
    {
        Assert.Contains("element, scene or project",
            _dispatcher.RunFails("replace-material from=Red to=Blue scope=everywhere"));
    }

    [Fact]
    public void AReplaceIsOneRecordAndUndoesAsOne()
    {
        CommandResult before = _dispatcher.RunOk("history");
        int count = ((List<object?>)before.Field("records")!).Count;

        _dispatcher.RunOk("replace-material from=Red to=Blue");

        CommandResult after = _dispatcher.RunOk("history");
        Assert.Equal(count + 1, ((List<object?>)after.Field("records")!).Count);

        _dispatcher.RunOk("undo");

        Assert.Equal("RR", Row("A"));
        Assert.Equal("RR", Row("B"));
    }

    private string Row(string element)
    {
        CommandResult slice = _dispatcher.RunOk($"slice element={element} axis=y index=0");

        return Grid(slice)[0];
    }

    private static string[] Grid(CommandResult slice) =>
        ((List<object?>)slice.Field("grid")!).Cast<string>().ToArray();

    private string MaterialOf(string element)
    {
        CommandResult slice = _dispatcher.RunOk($"slice element={element} axis=y index=0");
        var legend = (List<object?>)slice.Field("legend")!;

        return (string)((IReadOnlyDictionary<string, object?>)legend[0]!)["id"]!;
    }
}
