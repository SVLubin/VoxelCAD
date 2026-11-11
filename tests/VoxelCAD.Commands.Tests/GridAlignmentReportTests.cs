using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// The alignment an element reports over the command surface. It is worth
/// reporting because it is not derivable from the three transform values an agent
/// can already read: it is the world transform that decides it, so a parent's
/// tilt reaches a child that has no rotation of its own. What it changes is what
/// a click can do — the region's walls stop being a surface the brush places
/// against when the target's cells no longer sit on the scene's grid.
/// </summary>
public sealed class GridAlignmentReportTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public GridAlignmentReportTests()
    {
        _dispatcher.RunOk("new");
    }

    [Fact]
    public void ANewElementIsOnTheGrid()
    {
        _dispatcher.RunOk("create-element name=Block kind=VoxelModel");

        Assert.Equal(true, Aligned("Block"));
    }

    [Fact]
    public void ARightAngleKeepsItOnTheGrid()
    {
        _dispatcher.RunOk("create-element name=Block kind=VoxelModel");
        _dispatcher.RunOk("set-transform element=Block channel=Rotation value=0,90,0");

        Assert.Equal(true, Aligned("Block"));
    }

    [Fact]
    public void AFreeAngleTakesItOff()
    {
        _dispatcher.RunOk("create-element name=Block kind=VoxelModel");
        _dispatcher.RunOk("set-field-mode element=Block channel=Rotation mode=Float");
        _dispatcher.RunOk("set-transform element=Block channel=Rotation value=0,30,0");

        Assert.Equal(false, Aligned("Block"));
    }

    [Fact]
    public void AScaleOtherThanOneTakesItOff()
    {
        _dispatcher.RunOk("create-element name=Block kind=VoxelModel");
        _dispatcher.RunOk("set-transform element=Block channel=Scale value=2,2,2");

        Assert.Equal(false, Aligned("Block"));
    }

    /// <summary>The reason it is the world transform and not the local one.</summary>
    [Fact]
    public void AParentsTiltReachesAChildThatIsStraightItself()
    {
        _dispatcher.RunOk("create-element name=Rig");
        _dispatcher.RunOk("set-field-mode element=Rig channel=Rotation mode=Float");
        _dispatcher.RunOk("set-transform element=Rig channel=Rotation value=0,30,0");

        _dispatcher.RunOk("create-element name=Block kind=VoxelModel parent=Rig");

        Assert.Equal(false, Aligned("Rig"));
        Assert.Equal(false, Aligned("Block"));
    }

    private object? Aligned(string element) =>
        _dispatcher.RunOk($"element element={element}").Field("gridAligned");
}
