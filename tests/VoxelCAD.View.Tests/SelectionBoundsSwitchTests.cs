using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// One switch pulled by several parties: the bounds box is drawn while any reason
/// stands, and each party may raise and lower its own without knowing the others
/// exist.
/// </summary>
public sealed class SelectionBoundsSwitchTests
{
    [Fact]
    public void NobodyAskingLeavesItOff()
    {
        var boundsSwitch = new SelectionBoundsSwitch();

        Assert.False(boundsSwitch.IsOn);
        Assert.Equal(SelectionBoundsReason.None, boundsSwitch.Reasons);
    }

    [Fact]
    public void OneReasonTurnsItOnAndOff()
    {
        var boundsSwitch = new SelectionBoundsSwitch();

        Assert.True(boundsSwitch.Set(SelectionBoundsReason.TransformTool, raised: true));
        Assert.True(boundsSwitch.IsOn);

        Assert.True(boundsSwitch.Set(SelectionBoundsReason.TransformTool, raised: false));
        Assert.False(boundsSwitch.IsOn);
    }

    /// <summary>
    /// The reason it is a set of latches rather than a boolean: a tool going away
    /// under an inactive viewport must not take the box with it.
    /// </summary>
    [Fact]
    public void TheBoxStaysWhileAnyOtherReasonStands()
    {
        var boundsSwitch = new SelectionBoundsSwitch();

        boundsSwitch.Set(SelectionBoundsReason.TransformTool, raised: true);

        Assert.False(boundsSwitch.Set(SelectionBoundsReason.ViewportInactive, raised: true));
        Assert.True(boundsSwitch.IsOn);

        Assert.False(boundsSwitch.Set(SelectionBoundsReason.TransformTool, raised: false));
        Assert.True(boundsSwitch.IsOn);

        Assert.True(boundsSwitch.Set(SelectionBoundsReason.ViewportInactive, raised: false));
        Assert.False(boundsSwitch.IsOn);
    }

    /// <summary>
    /// The answer is about the box, not about the reason — a caller only rebuilds
    /// when the box itself changed state.
    /// </summary>
    [Fact]
    public void RaisingWhatIsAlreadyRaisedChangesNothing()
    {
        var boundsSwitch = new SelectionBoundsSwitch();

        Assert.True(boundsSwitch.Set(SelectionBoundsReason.ViewportInactive, raised: true));
        Assert.False(boundsSwitch.Set(SelectionBoundsReason.ViewportInactive, raised: true));
        Assert.False(boundsSwitch.Set(SelectionBoundsReason.TransformTool, raised: false));
    }

    [Fact]
    public void TheThreeTransformToolsAskForTheBox()
    {
        Assert.True(SelectionBoundsSwitch.RaisedByTool(ToolKind.Move));
        Assert.True(SelectionBoundsSwitch.RaisedByTool(ToolKind.Rotate));
        Assert.True(SelectionBoundsSwitch.RaisedByTool(ToolKind.Scale));
    }

    [Fact]
    public void TheCellToolsDoNot()
    {
        Assert.False(SelectionBoundsSwitch.RaisedByTool(ToolKind.Paint));
        Assert.False(SelectionBoundsSwitch.RaisedByTool(ToolKind.Eyedropper));
        Assert.False(SelectionBoundsSwitch.RaisedByTool(ToolKind.Extrude));
        Assert.False(SelectionBoundsSwitch.RaisedByTool(ToolKind.Fill));
    }
}
