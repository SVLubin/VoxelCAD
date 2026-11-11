using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The one answer to "may a click under this tool change the selection". The
/// viewport asks it before reporting a pick, the presenter before acting on one,
/// and the two must never disagree.
/// </summary>
/// <remarks>
/// Enumerated over the whole enum for the same reason its neighbour is: a tool
/// added tomorrow must be a no until somebody decides that its click is for
/// finding things rather than for doing something to them.
/// </remarks>
public sealed class SelectingToolsTests
{
    [Fact]
    public void TheCursorAndTheThreeTransformToolsQualify()
    {
        Assert.True(SelectingTools.IsSelectingTool(ToolKind.Select));
        Assert.True(SelectingTools.IsSelectingTool(ToolKind.Move));
        Assert.True(SelectingTools.IsSelectingTool(ToolKind.Rotate));
        Assert.True(SelectingTools.IsSelectingTool(ToolKind.Scale));
    }

    /// <summary>
    /// The tools that aim at cells do not: their click is already spent on the
    /// cells, and having it also change what is being edited is the auto-select
    /// complaint.
    /// </summary>
    [Fact]
    public void EveryToolThatAimsAtCellsDoesNot()
    {
        foreach (ToolKind tool in Enum.GetValues<ToolKind>())
        {
            if (tool is ToolKind.Select or ToolKind.Move or ToolKind.Rotate or ToolKind.Scale)
                continue;

            Assert.False(SelectingTools.IsSelectingTool(tool));
        }
    }

    /// <summary>
    /// Every tool that gets handles selects, and that pairing is load-bearing: it
    /// is what lets one press mean "grab the handle" or "pick what is under it"
    /// depending only on what it hit.
    /// </summary>
    [Fact]
    public void EveryToolWithHandlesSelects()
    {
        foreach (ToolKind tool in Enum.GetValues<ToolKind>())
        {
            if (TransformTools.IsTransformTool(tool))
                Assert.True(SelectingTools.IsSelectingTool(tool));
        }
    }
}
