using System;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The history panel's wording. One test, and it is about the class rather than
/// about any one label: a switch with a fallback rots as the enum grows, and the
/// rot is silent — the panel prints "CreatePrefab" and nothing fails.
/// </summary>
/// <remarks>
/// Found the way these things are found: by looking at the panel after adding an
/// action kind. This is the guard that means the next one is caught by a test.
/// </remarks>
public sealed class ActionLabelTests
{
    [Fact]
    public void EveryActionKindHasWordingOfItsOwn()
    {
        foreach (ActionKind kind in Enum.GetValues<ActionKind>())
        {
            var record = new HistoryRecord(Guid.NewGuid(), kind, Guid.NewGuid(), "Bracket", default);
            var label = ActionLabelHelper.Describe(record);

            Assert.False(
                label == kind.ToString(),
                $"{kind} falls through to the raw enum name; give it a sentence.");
            Assert.Contains("Bracket", label);
        }
    }

    [Fact]
    public void AGroupGestureNamesOneAndCountsTheRest()
    {
        Assert.Equal("Bracket", ActionLabelHelper.DescribeTargets("Bracket", 1));
        Assert.Equal("Bracket and 2 more", ActionLabelHelper.DescribeTargets("Bracket", 3));
    }
}
