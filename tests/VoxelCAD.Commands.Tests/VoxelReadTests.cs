using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// Reading a voxel model back. These are the commands that tell an agent whether
/// it built what it meant to, so the shape of the answer is as much the subject
/// here as the numbers in it.
/// </summary>
public sealed class VoxelReadTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public VoxelReadTests()
    {
        _dispatcher.RunOk("new");
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
    }

    [Fact]
    public void BothReadCommandsAreMarkedReadOnly()
    {
        Assert.All(
            _dispatcher.Commands.Where(command => command.Name is "slice" or "bounds"),
            command => Assert.False(command.Mutates));
    }

    [Fact]
    public void ASliceDrawsTheLayerItWasAskedFor()
    {
        // An L lying on the floor: one arm along X, the other along Z.
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=2,0,0 material=Default");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=0,0,3 material=Default");

        IReadOnlyDictionary<string, object?> slice = Slice("axis=Y index=0");

        Assert.Equal("X", slice["columnAxis"]);
        Assert.Equal("Z", slice["rowAxis"]);
        Assert.Equal(new[] { "DDD", "D..", "D..", "D.." }, Grid(slice));
    }

    [Fact]
    public void AnEmptyCellIsQuiet()
    {
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=2,0,2 material=Default");
        _dispatcher.RunOk("set-voxels element=Model cells=1,0,1");

        Assert.Equal(new[] { "DDD", "D.D", "DDD" }, Grid(Slice("axis=Y index=0")));
        Assert.Equal(8, Slice("axis=Y index=0")["filled"]);
    }

    [Fact]
    public void TheLegendNamesEveryCharacterTheGridUses()
    {
        _dispatcher.RunOk("create-material name=Glass color=0.4,0.7,1,0.45");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=1,0,0 material=Default");
        _dispatcher.RunOk("set-voxels element=Model material=Glass cells=2,0,0");

        IReadOnlyDictionary<string, object?> slice = Slice("axis=Y index=0");
        var legend = ((List<object?>)slice["legend"]!)
            .Cast<IReadOnlyDictionary<string, object?>>()
            .ToList();

        Assert.Equal(new[] { "DDG" }, Grid(slice));
        Assert.Equal(".", slice["emptyChar"]);
        Assert.Equal(new[] { "D", "G" }, legend.Select(entry => entry["char"]));
        Assert.Equal(new[] { "Default", "Glass" }, legend.Select(entry => entry["material"]));
        Assert.Equal(new object?[] { 2, 1 }, legend.Select(entry => entry["cells"]));
    }

    /// <summary>
    /// The character stands for the material across the whole model, not the layer
    /// in front of it. Otherwise two slices of one model would disagree about what
    /// a letter means, which is worse than no letter at all.
    /// </summary>
    [Fact]
    public void ACharacterMeansTheSameThingInEverySlice()
    {
        // Same initial: the second material has to be given something else, and
        // has to keep it in a layer where the first one does not appear.
        _dispatcher.RunOk("create-material name=Stone color=0.5,0.5,0.5");
        _dispatcher.RunOk("create-material name=Steel color=0.6,0.6,0.7");
        _dispatcher.RunOk("set-voxels element=Model material=Stone cells=0,0,0");
        _dispatcher.RunOk("set-voxels element=Model material=Steel cells=0,1,0");

        Assert.Equal(new[] { "S" }, Grid(Slice("axis=Y index=0")));
        Assert.Equal(new[] { "T" }, Grid(Slice("axis=Y index=1")));
    }

    [Fact]
    public void AnElevationIsNotUpsideDown()
    {
        _dispatcher.RunOk("set-voxels element=Model material=Default cells=0,0,0");
        _dispatcher.RunOk("set-voxels element=Model material=Default cells=0,2,0");

        IReadOnlyDictionary<string, object?> slice = Slice("axis=Z index=0");

        // Rows run down the page, so the highest cell comes first and the ends of
        // the range say as much without the reader having to know the rule.
        Assert.Equal("Y", slice["rowAxis"]);
        Assert.Equal(2, slice["rowFrom"]);
        Assert.Equal(0, slice["rowTo"]);
        Assert.Equal(new[] { "D", ".", "D" }, Grid(slice));
    }

    [Fact]
    public void ASliceFramesTheModelRatherThanTheScene()
    {
        _dispatcher.RunOk("fill-box element=Model from=10,0,10 to=11,0,12 material=Default");

        IReadOnlyDictionary<string, object?> slice = Slice("axis=Y index=0");

        Assert.Equal(10, slice["columnFrom"]);
        Assert.Equal(11, slice["columnTo"]);
        Assert.Equal(10, slice["rowFrom"]);
        Assert.Equal(12, slice["rowTo"]);
        Assert.Equal(new[] { "DD", "DD", "DD" }, Grid(slice));
    }

    [Fact]
    public void AWindowReadsPartOfALayer()
    {
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=5,0,5 material=Default");

        IReadOnlyDictionary<string, object?> slice = Slice("axis=Y index=0 from=1,0,1 to=2,0,3");

        Assert.Equal(1, slice["columnFrom"]);
        Assert.Equal(2, slice["columnTo"]);
        Assert.Equal(new[] { "DD", "DD", "DD" }, Grid(slice));
    }

    [Fact]
    public void ASliceTooBigToReadSaysHowToWindowIt()
    {
        ICommandDispatcher wide = CommandFactory.CreateDispatcher(new CommandContext());
        wide.RunOk("new size=128,128,128");
        wide.RunOk("create-element name=Floor kind=VoxelModel");
        wide.RunOk("fill-box element=Floor from=0,0,0 to=127,0,127 material=Default");

        var error = wide.RunFails("slice element=Floor axis=Y index=0");

        Assert.Contains("128 by 128", error);
        Assert.Contains("'from' and 'to'", error);
        Assert.Contains("bounds", error);
    }

    [Fact]
    public void SlicingAnEmptyModelAnswersWithNothingRatherThanAWallOfDots()
    {
        IReadOnlyDictionary<string, object?> slice = Slice("axis=Y index=0");

        Assert.Equal(0, slice["filled"]);
        Assert.Empty((List<object?>)slice["grid"]!);
    }

    [Fact]
    public void BoundsReportTheOccupiedBoxNotTheScene()
    {
        _dispatcher.RunOk("fill-box element=Model from=4,1,2 to=6,1,9 material=Default");

        var bounds = (IReadOnlyDictionary<string, object?>)_dispatcher
            .RunOk("bounds element=Model").Value!;

        Assert.Equal(24, bounds["voxels"]);
        Assert.Equal(new[] { 4, 1, 2 }, bounds["min"]);
        Assert.Equal(new[] { 6, 1, 9 }, bounds["max"]);
        Assert.Equal(new[] { 3, 1, 8 }, bounds["size"]);
    }

    [Fact]
    public void BoundsOfAnEmptyModelHaveNoCorners()
    {
        var bounds = (IReadOnlyDictionary<string, object?>)_dispatcher
            .RunOk("bounds element=Model").Value!;

        Assert.Equal(0, bounds["voxels"]);
        Assert.False(bounds.ContainsKey("min"));
    }

    [Fact]
    public void ReadingAnElementThatHoldsNoVoxelsSaysWhatItIs()
    {
        _dispatcher.RunOk("create-element name=Group");

        Assert.Contains("not a voxel model", _dispatcher.RunFails("bounds element=Group"));
        Assert.Contains("not a voxel model", _dispatcher.RunFails("slice element=Group axis=Y index=0"));
    }

    [Fact]
    public void AnUnknownAxisListsTheOnesThereAre()
    {
        Assert.Contains("X, Y, Z", _dispatcher.RunFails("slice element=Model axis=W index=0"));
    }

    private IReadOnlyDictionary<string, object?> Slice(string arguments) =>
        (IReadOnlyDictionary<string, object?>)_dispatcher
            .RunOk($"slice element=Model {arguments}").Value!;

    private static string[] Grid(IReadOnlyDictionary<string, object?> slice) =>
        ((List<object?>)slice["grid"]!).Cast<string>().ToArray();
}
