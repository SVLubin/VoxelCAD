using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The selection border is twelve edges derived from two corners. Deriving them
/// rather than tabulating them is what makes this worth a test: the walk has to
/// produce every edge of the box, each exactly once.
/// </summary>
public sealed class SelectionBoxTests
{
    [Fact]
    public void ABoxHasTwelveEdges()
    {
        IReadOnlyList<Vector3> points = SelectionBox.CreateEdgePoints(Vector3.Zero, Vector3.One);

        Assert.Equal(24, points.Count);
    }

    /// <summary>
    /// Every edge joins two corners differing in exactly one coordinate — which
    /// is the definition of an edge, and rules out diagonals across a face.
    /// </summary>
    [Fact]
    public void EveryEdgeRunsAlongOneAxis()
    {
        IReadOnlyList<Vector3> points = SelectionBox.CreateEdgePoints(
            new Vector3(-2f, 1f, 4f),
            new Vector3(3f, 5f, 9f));

        for (var index = 0; index < points.Count; index += 2)
        {
            Vector3 edge = points[index + 1] - points[index];
            var moving = 0;

            for (var axis = 0; axis < 3; axis++)
            {
                if (edge[axis] != 0f)
                    moving++;
            }

            Assert.Equal(1, moving);
        }
    }

    [Fact]
    public void EveryEdgeIsDistinct()
    {
        IReadOnlyList<Vector3> points = SelectionBox.CreateEdgePoints(Vector3.Zero, Vector3.One);

        var edges = new HashSet<(Vector3, Vector3)>();

        for (var index = 0; index < points.Count; index += 2)
            Assert.True(edges.Add((points[index], points[index + 1])));
    }

    /// <summary>
    /// The eight corners each appear on three edges, which is what says the walk
    /// covered the whole box rather than, say, two opposite faces twice.
    /// </summary>
    [Fact]
    public void EveryCornerIsUsedThreeTimes()
    {
        var min = new Vector3(-1f, -2f, -3f);
        var max = new Vector3(4f, 5f, 6f);

        IReadOnlyList<Vector3> points = SelectionBox.CreateEdgePoints(min, max);

        Dictionary<Vector3, int> uses = points
            .GroupBy(point => point)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(8, uses.Count);
        Assert.All(uses.Values, count => Assert.Equal(3, count));
    }

    /// <summary>
    /// The coordinate the dash-dot pattern runs on. Per edge, not cumulative: every
    /// edge starts its pattern at a corner, so the box reads as twelve marked-out
    /// lengths instead of one pattern the corners land in the middle of.
    /// </summary>
    [Fact]
    public void EachEdgeCarriesItsOwnLengthFromZero()
    {
        var min = new Vector3(0f, 0f, 0f);
        var max = new Vector3(3f, 5f, 7f);

        IReadOnlyList<Vector3> points = SelectionBox.CreateEdgePoints(min, max);
        IReadOnlyList<float> distances = SelectionBox.CreateEdgeDistances(points);

        Assert.Equal(points.Count, distances.Count);

        for (var index = 0; index < points.Count; index += 2)
        {
            Assert.Equal(0f, distances[index]);
            Assert.Equal(
                points[index].DistanceTo(points[index + 1]), distances[index + 1], precision: 5);
        }
    }

    /// <summary>
    /// Four edges along each axis, each as long as the box is on that axis — the
    /// pattern therefore counts cells, which is the ruler the walls' grid uses.
    /// </summary>
    [Fact]
    public void TheLengthsAreTheBoxsOwnSides()
    {
        IReadOnlyList<Vector3> points = SelectionBox.CreateEdgePoints(
            Vector3.Zero, new Vector3(3f, 5f, 7f));

        List<float> lengths = SelectionBox.CreateEdgeDistances(points)
            .Where(distance => distance > 0f)
            .ToList();

        Assert.Equal(12, lengths.Count);
        Assert.Equal(4, lengths.Count(length => length == 3f));
        Assert.Equal(4, lengths.Count(length => length == 5f));
        Assert.Equal(4, lengths.Count(length => length == 7f));
    }
}
