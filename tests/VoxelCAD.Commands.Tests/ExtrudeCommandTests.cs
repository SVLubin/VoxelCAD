using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// Extruding as an agent reaches it: the same face patch the tool takes hold of,
/// named by a cell and a direction, pushed out or carved in by whole layers.
/// </summary>
public sealed class ExtrudeCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public ExtrudeCommandTests()
    {
        _dispatcher.RunOk("new");
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");

        // A 3x1x1 bar, every cell showing its top.
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=2,0,0 material=Default");
    }

    [Fact]
    public void PushingAFaceOutAddsALayerOverTheWholePatch()
    {
        CommandResult result = _dispatcher.RunOk("extrude element=Model cell=1,0,0 face=+y layers=1");

        Assert.Equal(3, result.Field("patchCells"));
        Assert.Equal(3, result.Field("writtenCells"));
        Assert.Equal(6, result.Field("voxels"));
    }

    [Fact]
    public void MoreLayersAddMoreCells()
    {
        CommandResult result = _dispatcher.RunOk("extrude element=Model cell=0,0,0 face=+y layers=2");

        Assert.Equal(6, result.Field("writtenCells"));
        Assert.Equal(9, result.Field("voxels"));
    }

    [Fact]
    public void NegativeLayersCarveTheFaceIn()
    {
        CommandResult result = _dispatcher.RunOk(
            "extrude element=Model cell=0,0,0 face=+y layers=-1");

        Assert.Equal(0, result.Field("voxels"));
    }

    [Fact]
    public void OneExtrusionIsOneUndoableRecord()
    {
        _dispatcher.RunOk("extrude element=Model cell=1,0,0 face=+y layers=1");
        _dispatcher.RunOk("undo");

        CommandResult tree = _dispatcher.RunOk("bounds element=Model");

        Assert.Equal(3, tree.Field("voxels"));
    }

    [Fact]
    public void AnEmptyCellIsRefusedWithAnExplanation()
    {
        var error = _dispatcher.RunFails("extrude element=Model cell=9,9,9 face=+y layers=1");

        Assert.Contains("no cell at", error);
    }

    [Fact]
    public void ABuriedFaceIsRefused()
    {
        // Put a cell on top of the middle one, so its +Y face has something against it.
        _dispatcher.RunOk("set-voxels element=Model cells=1,1,0 material=Default");

        var error = _dispatcher.RunFails("extrude element=Model cell=1,0,0 face=+y layers=1");

        Assert.Contains("buried", error);
    }

    [Fact]
    public void OneFaceOnly()
    {
        var error = _dispatcher.RunFails("extrude element=Model cell=1,0,0 face=+y,-y layers=1");

        Assert.Contains("names one face", error);
    }

    /// <summary>
    /// The same bargain 'fill-box' strikes: a mistyped layer count is a refusal
    /// naming its size, never an out-of-memory crash — and it is refused before
    /// anything the size of the answer is allocated.
    /// </summary>
    [Fact]
    public void AnAbsurdLayerCountIsRefused()
    {
        var error = _dispatcher.RunFails(
            "extrude element=Model cell=1,0,0 face=+y layers=500000000");

        Assert.Contains("Reduce 'layers'", error);
    }

    /// <summary>Mirrors multiply the write, so they multiply the estimate.</summary>
    [Fact]
    public void MirrorsCountTowardsTheCap()
    {
        _dispatcher.RunOk("set-symmetry element=Model axis=X plane=0");
        _dispatcher.RunOk("set-symmetry element=Model axis=Y plane=0");
        _dispatcher.RunOk("set-symmetry element=Model axis=Z plane=0");

        // 3 patch cells x 8 mirror images x this many layers crosses the million
        // where the unmirrored write would not.
        var error = _dispatcher.RunFails(
            "extrude element=Model cell=1,0,0 face=+y layers=50000");

        Assert.Contains("Reduce 'layers'", error);
    }

    /// <summary>The mirror applies to an agent's extrusion exactly as it does to the brush.</summary>
    [Fact]
    public void AMirrorExtrudesBothSides()
    {
        _dispatcher.RunOk("delete-element element=Model");
        _dispatcher.RunOk("create-element name=Bar kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Bar from=3,0,0 to=5,0,0 material=Default");

        // The model's cells sit at 3..5; its pivot is the middle of them, so a
        // mirror offset of 0 runs straight through the bar.
        _dispatcher.RunOk("set-symmetry element=Bar axis=X plane=0");

        CommandResult result = _dispatcher.RunOk("extrude element=Bar cell=3,0,0 face=+y layers=1");

        Assert.Equal(3, result.Field("writtenCells"));
        Assert.Equal(6, result.Field("voxels"));
    }
}
