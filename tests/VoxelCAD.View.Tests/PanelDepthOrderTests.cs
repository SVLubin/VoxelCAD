using System.Linq;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Which panel is drawn in front of which. Godot reads that off the child list,
/// and the child list was the one output of the column's solve nobody ever
/// wrote down — a panel is appended when it is reparented and never moved
/// again, so depth was a residue of arrival history.
/// </summary>
public sealed class PanelDepthOrderTests
{
    private static readonly PanelKind[] Three =
    {
        PanelKind.Project, PanelKind.History, PanelKind.Inspector
    };

    [Fact]
    public void WithNothingCarriedDepthIsLayoutOrder() =>
        Assert.Equal(new[] { 0, 1, 2 }, PanelDepthOrder.Solve(Three, carried: null));

    /// <summary>
    /// The thing under the hand sweeps over its neighbours, so it goes last —
    /// which is the whole of the defect: a panel dragged past a
    /// sibling passed behind it.
    /// </summary>
    [Theory]
    [InlineData(PanelKind.Project, new[] { 2, 0, 1 })]
    [InlineData(PanelKind.History, new[] { 0, 2, 1 })]
    [InlineData(PanelKind.Inspector, new[] { 0, 1, 2 })]
    public void TheCarriedPanelIsDrawnLast(PanelKind carried, int[] expected) =>
        Assert.Equal(expected, PanelDepthOrder.Solve(Three, carried));

    /// <summary>
    /// The assertion that names the class of the defect: the answer is a
    /// function of the layout alone. Fed the same panels in a different layout
    /// order it agrees with that order, never with the order they arrived in.
    /// </summary>
    [Fact]
    public void DepthIsAFunctionOfLayoutOrderAlone()
    {
        PanelKind[] reversed = Three.Reverse().ToArray();

        Assert.Equal(new[] { 0, 1, 2 }, PanelDepthOrder.Solve(reversed, carried: null));
        Assert.Equal(new[] { 0, 2, 1 }, PanelDepthOrder.Solve(reversed, PanelKind.History));
    }

    /// <summary>
    /// A panel carried out of this column is nobody's — the run closes over it
    /// and the answer is the remaining order, with no hole left behind.
    /// </summary>
    [Fact]
    public void APanelNotInTheColumnChangesNothing() =>
        Assert.Equal(
            new[] { 0, 1, 2 }, PanelDepthOrder.Solve(Three, PanelKind.Toolbar));

    [Fact]
    public void AnEmptyColumnHasNoDepth() =>
        Assert.Empty(PanelDepthOrder.Solve(new PanelKind[0], PanelKind.Project));
}
