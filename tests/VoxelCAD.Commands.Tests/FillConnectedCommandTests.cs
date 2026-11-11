using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>Filling as an agent reaches it: the connected volume, recoloured as one record.</summary>
public sealed class FillConnectedCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public FillConnectedCommandTests()
    {
        _dispatcher.RunOk("new");
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("create-material name=Red color=1,0,0");

        // Two bars of Default, a gap between them.
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=2,0,0 material=Default");
        _dispatcher.RunOk("fill-box element=Model from=5,0,0 to=6,0,0 material=Default");
    }

    [Fact]
    public void OneRegionIsRecolouredAndTheOtherIsNot()
    {
        CommandResult result = _dispatcher.RunOk("fill-connected element=Model at=1,0,0 material=Red");

        Assert.Equal(3, result.Field("volumeCells"));
        Assert.Equal(5, result.Field("voxels"));

        CommandResult slice = _dispatcher.RunOk("slice element=Model axis=y index=0");
        var grid = Grid(slice);

        Assert.Equal("RRR..DD", grid[0]);
    }

    [Fact]
    public void AFillIsOneRecordAndUndoesAsOne()
    {
        CommandResult before = _dispatcher.RunOk("history");
        int count = ((List<object?>)before.Field("records")!).Count;

        _dispatcher.RunOk("fill-connected element=Model at=0,0,0 material=Red");

        CommandResult after = _dispatcher.RunOk("history");
        Assert.Equal(count + 1, ((List<object?>)after.Field("records")!).Count);

        _dispatcher.RunOk("undo");

        CommandResult slice = _dispatcher.RunOk("slice element=Model axis=y index=0");
        Assert.Equal("DDD..DD", (Grid(slice))[0]);
    }

    /// <summary>
    /// A mirror plane applies — each image of the same material seeds a volume of
    /// its own — which is what makes the command and the Fill tool answer with the
    /// same cells. Literal would be the other answer, and then the two surfaces
    /// would disagree.
    /// </summary>
    [Fact]
    public void AMirrorPlaneSeedsTheVolumeOnTheOtherSideToo()
    {
        // Plane at x=3.5 measured from the pivot: cells 0..2 pair with 7..5.
        _dispatcher.RunOk("fill-box element=Model from=5,0,0 to=7,0,0 material=Default");
        _dispatcher.RunOk("set-symmetry element=Model axis=x plane=2.5");

        CommandResult result = _dispatcher.RunOk("fill-connected element=Model at=1,0,0 material=Red");

        Assert.Equal(6, result.Field("volumeCells"));
        Assert.Equal("RRR..RRR", Grid(_dispatcher.RunOk("slice element=Model axis=y index=0"))[0]);
    }

    /// <summary>
    /// An image that lands on another material seeds nothing: the region on the
    /// other side is not the clicked region's twin, and it is left as it was.
    /// </summary>
    [Fact]
    public void AnImageOnAnotherMaterialIsLeftAlone()
    {
        _dispatcher.RunOk("create-material name=Blue color=0,0,1");
        _dispatcher.RunOk("fill-box element=Model from=5,0,0 to=7,0,0 material=Blue");
        _dispatcher.RunOk("set-symmetry element=Model axis=x plane=2.5");

        CommandResult result = _dispatcher.RunOk("fill-connected element=Model at=1,0,0 material=Red");

        Assert.Equal(3, result.Field("volumeCells"));
        Assert.Equal("RRR..BBB", Grid(_dispatcher.RunOk("slice element=Model axis=y index=0"))[0]);
    }

    [Fact]
    public void AnEmptySeedIsRefusedByName()
    {
        string error = _dispatcher.RunFails("fill-connected element=Model at=3,0,0 material=Red");

        Assert.Contains("no cell at 3,0,0", error);
    }

    [Fact]
    public void TheSameMaterialRecordsNothing()
    {
        CommandResult before = _dispatcher.RunOk("history");
        int count = ((List<object?>)before.Field("records")!).Count;

        _dispatcher.RunOk("fill-connected element=Model at=0,0,0 material=Default");

        CommandResult after = _dispatcher.RunOk("history");
        Assert.Equal(count, ((List<object?>)after.Field("records")!).Count);
    }

    private static string[] Grid(CommandResult slice) =>
        ((List<object?>)slice.Field("grid")!).Cast<string>().ToArray();
}
