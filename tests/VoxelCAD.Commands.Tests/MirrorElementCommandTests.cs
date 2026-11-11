using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>Mirror now, as an agent reaches it: images where empty, one record.</summary>
public sealed class MirrorElementCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public MirrorElementCommandTests()
    {
        _dispatcher.RunOk("new");
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("create-material name=Red color=1,0,0");

        // Cells 0..2 on x; the first edit puts the pivot at the middle, x=1.5.
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=2,0,0 material=Default");
        _dispatcher.RunOk("set-voxels element=Model cells=2,0,0 material=Red");
    }

    [Fact]
    public void AnExplicitPlaneMirrorsAcrossIt()
    {
        // Plane at pivot + 1.5 = x 3: cell 2 pairs with 3, 1 with 4, 0 with 5.
        CommandResult result = _dispatcher.RunOk("mirror-element element=Model axis=x plane=1.5");

        Assert.Equal(3, result.Field("writtenCells"));
        Assert.Equal(6, result.Field("voxels"));
        Assert.Equal(1.5f, result.Field("plane"));

        CommandResult slice = _dispatcher.RunOk("slice element=Model axis=y index=0");
        Assert.Equal("DDRRDD", Grid(slice)[0]);
    }

    [Fact]
    public void WithoutAPlaneTheSlotsOwnIsUsed()
    {
        _dispatcher.RunOk("set-symmetry element=Model axis=x plane=1.5");

        CommandResult result = _dispatcher.RunOk("mirror-element element=Model axis=x");

        Assert.Equal(3, result.Field("writtenCells"));
        Assert.Equal(1.5f, result.Field("plane"));
    }

    /// <summary>A slot switched off keeps its position, and the button on that row still means it.</summary>
    [Fact]
    public void ADisabledSlotStillNamesItsPlane()
    {
        _dispatcher.RunOk("set-symmetry element=Model axis=x plane=1.5");
        _dispatcher.RunOk("set-symmetry element=Model axis=x plane=off");

        CommandResult result = _dispatcher.RunOk("mirror-element element=Model axis=x");

        Assert.Equal(3, result.Field("writtenCells"));
    }

    /// <summary>An untouched slot mirrors through the pivot, as enabling it would.</summary>
    [Fact]
    public void AnUntouchedSlotMirrorsThroughThePivot()
    {
        // The pivot sits at x=1.5, the middle of the first fill, and does not move
        // for a later cell: 0..2 map onto themselves, and cell 3 images to -1.
        _dispatcher.RunOk("set-voxels element=Model cells=3,0,0 material=Red");

        CommandResult result = _dispatcher.RunOk("mirror-element element=Model axis=x");

        Assert.Equal(1, result.Field("writtenCells"));
        Assert.Equal(0f, result.Field("plane"));
        Assert.Equal(5, result.Field("voxels"));
    }

    [Fact]
    public void ExistingCellsWinAndASymmetricModelGainsNothing()
    {
        _dispatcher.RunOk("mirror-element element=Model axis=x plane=1.5");
        CommandResult before = _dispatcher.RunOk("history");
        int count = ((List<object?>)before.Field("records")!).Count;

        CommandResult result = _dispatcher.RunOk("mirror-element element=Model axis=x plane=1.5");

        Assert.Equal(0, result.Field("writtenCells"));
        CommandResult after = _dispatcher.RunOk("history");
        Assert.Equal(count, ((List<object?>)after.Field("records")!).Count);
    }

    [Fact]
    public void AMirrorIsOneRecordAndUndoesAsOne()
    {
        _dispatcher.RunOk("mirror-element element=Model axis=x plane=1.5");
        _dispatcher.RunOk("undo");

        CommandResult slice = _dispatcher.RunOk("slice element=Model axis=y index=0");
        Assert.Equal("DDR", Grid(slice)[0]);
    }

    /// <summary>
    /// 'off' is set-symmetry's word. Taken here it mirrored across the slot's
    /// stored local origin — a point nobody chose and no surface ever showed —
    /// rather than across the plane the row holds.
    /// </summary>
    [Fact]
    public void OffIsRefusedByName()
    {
        string error = _dispatcher.RunFails("mirror-element element=Model axis=x plane=off");

        Assert.Contains("'set-symmetry'", error);
        Assert.Contains("omit 'plane'", error);
    }

    [Fact]
    public void AHalfPlaneOffTheLatticeIsRefused()
    {
        Assert.Contains("multiple of 0.5",
            _dispatcher.RunFails("mirror-element element=Model axis=x plane=0.3"));
    }

    private static string[] Grid(CommandResult slice) =>
        ((List<object?>)slice.Field("grid")!).Cast<string>().ToArray();
}
