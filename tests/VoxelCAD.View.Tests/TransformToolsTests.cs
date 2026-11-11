using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The one answer to "does this tool get transform handles". The gizmo places
/// them by it, and the bounds box asks a neighbouring question that used to have
/// the same answer.
/// </summary>
/// <remarks>
/// The default matters more than the members: the gizmo used to refuse tools by
/// name, and every tool added after the list — the pin, the extruder — fell
/// through it into invisible, click-eating handles. These tests hold the
/// inverse: a tool is a no here unless it was deliberately made a yes.
/// </remarks>
public sealed class TransformToolsTests
{
    [Fact]
    public void TheThreeTransformToolsQualify()
    {
        Assert.True(TransformTools.IsTransformTool(ToolKind.Move));
        Assert.True(TransformTools.IsTransformTool(ToolKind.Rotate));
        Assert.True(TransformTools.IsTransformTool(ToolKind.Scale));
    }

    /// <summary>
    /// Every tool that is not one of the three is a no — enumerated over the
    /// whole enum, so a tool added tomorrow is a no the day it appears.
    /// </summary>
    [Fact]
    public void EveryOtherToolDoesNot()
    {
        foreach (ToolKind tool in Enum.GetValues<ToolKind>())
        {
            if (tool is ToolKind.Move or ToolKind.Rotate or ToolKind.Scale)
                continue;

            Assert.False(TransformTools.IsTransformTool(tool));
        }
    }

    /// <summary>
    /// The two questions are neighbours, not the same question: the box is drawn
    /// for the tools aimed at whole elements, handles for the tools that move one.
    /// The cursor is the only tool the two answer differently about — it must show
    /// what it selected, and must never grow a handle to drag it by — and every
    /// other tool is a no on both, enumerated so a tool added tomorrow is a no the
    /// day it appears.
    /// </summary>
    [Fact]
    public void TheBoundsSwitchDiffersOnlyForTheCursor()
    {
        Assert.True(SelectionBoundsSwitch.RaisedByTool(ToolKind.Select));
        Assert.False(TransformTools.IsTransformTool(ToolKind.Select));

        foreach (ToolKind tool in Enum.GetValues<ToolKind>())
        {
            if (tool == ToolKind.Select)
                continue;

            Assert.Equal(
                TransformTools.IsTransformTool(tool),
                SelectionBoundsSwitch.RaisedByTool(tool));
        }
    }
}
