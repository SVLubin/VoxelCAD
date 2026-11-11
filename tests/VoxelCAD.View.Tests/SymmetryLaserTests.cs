using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The contour a mirror plane draws on the model: the outline of the columns of
/// cells the plane touches, at the plane's own coordinate, in the element's own
/// space. What the node does with it is a mesh; what is decided is here.
/// </summary>
public sealed class SymmetryLaserTests
{
    private static readonly Guid Stone = Guid.NewGuid();

    private static Dictionary<Vector3I, Guid> Cells(params Vector3I[] cells)
    {
        var voxels = new Dictionary<Vector3I, Guid>();

        foreach (Vector3I cell in cells)
            voxels[cell] = Stone;

        return voxels;
    }

    private static HashSet<(Vector3, Vector3)> Segments(IReadOnlyList<Vector3> points)
    {
        Assert.Equal(0, points.Count % 2);

        var segments = new HashSet<(Vector3, Vector3)>();

        for (var i = 0; i < points.Count; i += 2)
        {
            // Normalised so a segment matches whichever way round it was emitted.
            Vector3 a = points[i];
            Vector3 b = points[i + 1];

            segments.Add(a < b ? (a, b) : (b, a));
        }

        return segments;
    }

    [Fact]
    public void AnEmptyModelDrawsNothing()
    {
        Assert.Empty(SymmetryLaser.CreateContourPoints(Cells(), 0, 7));
    }

    /// <summary>
    /// A centre plane through one cell outlines that cell's cross-section: four
    /// edges of the unit square, at the plane's own coordinate.
    /// </summary>
    [Fact]
    public void ACentrePlaneOutlinesTheLayerItRunsThrough()
    {
        HashSet<(Vector3, Vector3)> segments = Segments(
            SymmetryLaser.CreateContourPoints(Cells(new Vector3I(3, 5, 9)), 0, 7));

        Assert.Equal(4, segments.Count);
        Assert.Contains((new Vector3(3.5f, 5, 9), new Vector3(3.5f, 5, 10)), segments);
        Assert.Contains((new Vector3(3.5f, 6, 9), new Vector3(3.5f, 6, 10)), segments);
        Assert.Contains((new Vector3(3.5f, 5, 9), new Vector3(3.5f, 6, 9)), segments);
        Assert.Contains((new Vector3(3.5f, 5, 10), new Vector3(3.5f, 6, 10)), segments);
    }

    /// <summary>A cell in another layer contributes nothing to this plane.</summary>
    [Fact]
    public void OtherLayersStayDark()
    {
        Assert.Empty(SymmetryLaser.CreateContourPoints(Cells(new Vector3I(1, 0, 0)), 0, 7));
    }

    /// <summary>
    /// A boundary plane collects both layers beside it: one cell on either side,
    /// same column, is still one square — the contour outlines the union.
    /// </summary>
    [Fact]
    public void ABoundaryPlaneUnitesTheLayersBesideIt()
    {
        HashSet<(Vector3, Vector3)> segments = Segments(SymmetryLaser.CreateContourPoints(
            Cells(new Vector3I(2, 0, 0), new Vector3I(3, 0, 0)), 0, 6));

        Assert.Equal(4, segments.Count);
        Assert.Contains((new Vector3(3, 0, 0), new Vector3(3, 1, 0)), segments);
        Assert.Contains((new Vector3(3, 0, 1), new Vector3(3, 1, 1)), segments);
    }

    /// <summary>
    /// Two footprint columns side by side merge into one outline: the edge they
    /// share is interior and must not be drawn.
    /// </summary>
    [Fact]
    public void AdjacentColumnsMergeTheirOutline()
    {
        HashSet<(Vector3, Vector3)> segments = Segments(SymmetryLaser.CreateContourPoints(
            Cells(new Vector3I(3, 0, 0), new Vector3I(3, 1, 0)), 0, 7));

        Assert.Equal(6, segments.Count);
        Assert.DoesNotContain((new Vector3(3.5f, 1, 0), new Vector3(3.5f, 1, 1)), segments);
    }

    /// <summary>The floor division behind the layer pick holds below zero too.</summary>
    [Fact]
    public void APlaneAtZeroSeesTheCellJustBelowIt()
    {
        HashSet<(Vector3, Vector3)> segments = Segments(
            SymmetryLaser.CreateContourPoints(Cells(new Vector3I(-1, 2, 3)), 0, 0));

        Assert.Equal(4, segments.Count);
        Assert.Contains((new Vector3(0, 2, 3), new Vector3(0, 2, 4)), segments);
    }

    /// <summary>The other two axes take their meaning from the plane's own.</summary>
    [Fact]
    public void TheAxisDecidesWhichCoordinateIsThePlanes()
    {
        HashSet<(Vector3, Vector3)> segments = Segments(
            SymmetryLaser.CreateContourPoints(Cells(new Vector3I(1, 2, 4)), 2, 9));

        Assert.Equal(4, segments.Count);
        Assert.Contains((new Vector3(1, 2, 4.5f), new Vector3(2, 2, 4.5f)), segments);
    }
}
