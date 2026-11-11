using System.Collections.Generic;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The wall half of the laser: the mirror plane carried into world space
/// through the element's transform, cut against the region's wall rectangles,
/// and the CPU restatement of which walls the box actually shows.
/// </summary>
public sealed class SymmetryWallTraceTests
{
    private static readonly Vector3 Box = new(32, 32, 32);

    private static HashSet<(Vector3, Vector3)> Segments(IReadOnlyList<Vector3> points)
    {
        Assert.Equal(0, points.Count % 2);

        var segments = new HashSet<(Vector3, Vector3)>();

        for (var i = 0; i < points.Count; i += 2)
        {
            Vector3 a = points[i];
            Vector3 b = points[i + 1];

            segments.Add(a < b ? (a, b) : (b, a));
        }

        return segments;
    }

    [Fact]
    public void AnIdentityElementKeepsThePlaneWhereItSays()
    {
        Assert.True(SymmetryWallTrace.TryGetWorldPlane(Transform3D.Identity, 0, 32, out Plane plane));

        Assert.Equal(0f, Mathf.Abs(plane.DistanceTo(new Vector3(16, 5, 9))), 4);
        Assert.Equal(1f, Mathf.Abs(plane.Normal.X), 4);
    }

    /// <summary>A quarter turn about Y carries a local X plane onto a world Z plane.</summary>
    [Fact]
    public void ARotatedElementTurnsThePlaneWithIt()
    {
        var world = new Transform3D(
            Basis.FromEuler(new Vector3(0, Mathf.DegToRad(90), 0)), new Vector3(16, 0, 16));

        Assert.True(SymmetryWallTrace.TryGetWorldPlane(world, 0, 0, out Plane plane));

        Assert.Equal(1f, Mathf.Abs(plane.Normal.Z), 4);
        Assert.Equal(0f, Mathf.Abs(plane.DistanceTo(new Vector3(3, 7, 16))), 4);
    }

    /// <summary>A scaled element moves the plane by its scale, and the normal survives.</summary>
    [Fact]
    public void AScaledElementStretchesThePlanePosition()
    {
        var world = new Transform3D(Basis.FromScale(new Vector3(2, 1, 1)), Vector3.Zero);

        Assert.True(SymmetryWallTrace.TryGetWorldPlane(world, 0, 6, out Plane plane));

        Assert.Equal(0f, Mathf.Abs(plane.DistanceTo(new Vector3(6, 1, 2))), 4);
        Assert.Equal(1f, Mathf.Abs(plane.Normal.X), 4);
    }

    [Fact]
    public void AZeroScaleHasNoPlaneToDraw()
    {
        var world = new Transform3D(Basis.FromScale(new Vector3(0, 1, 1)), Vector3.Zero);

        Assert.False(SymmetryWallTrace.TryGetWorldPlane(world, 0, 6, out _));
    }

    [Fact]
    public void APlaneCrossingAWallLeavesOneSegmentAcrossIt()
    {
        var plane = new Plane(new Vector3(1, 0, 0), 16f);

        HashSet<(Vector3, Vector3)> segments =
            Segments(SymmetryWallTrace.TraceWall(Box, 2, 0, plane));

        Assert.Equal(
            new HashSet<(Vector3, Vector3)> { (new Vector3(16, 0, 0), new Vector3(16, 32, 0)) },
            segments);
    }

    [Fact]
    public void AParallelPlaneMissesTheWall()
    {
        var plane = new Plane(new Vector3(1, 0, 0), 16f);

        Assert.Empty(SymmetryWallTrace.TraceWall(Box, 0, 0, plane));
        Assert.Empty(SymmetryWallTrace.TraceWall(Box, 0, 1, plane));
    }

    /// <summary>
    /// The plane lying in the wall itself — symmetry at zero on an untouched
    /// element — outlines the wall's own rectangle.
    /// </summary>
    [Fact]
    public void ACoplanarPlaneOutlinesTheWall()
    {
        var plane = new Plane(new Vector3(1, 0, 0), 0f);

        HashSet<(Vector3, Vector3)> segments =
            Segments(SymmetryWallTrace.TraceWall(Box, 0, 0, plane));

        Assert.Equal(4, segments.Count);
        Assert.Contains((new Vector3(0, 0, 0), new Vector3(0, 32, 0)), segments);
        Assert.Contains((new Vector3(0, 0, 32), new Vector3(0, 32, 32)), segments);
    }

    /// <summary>A plane running along a wall's edge yields exactly that edge.</summary>
    [Fact]
    public void APlaneAlongTheWallsEdgeYieldsTheEdge()
    {
        var plane = new Plane(new Vector3(0, 0, 1), 0f);

        HashSet<(Vector3, Vector3)> segments =
            Segments(SymmetryWallTrace.TraceWall(Box, 0, 0, plane));

        Assert.Equal(
            new HashSet<(Vector3, Vector3)> { (new Vector3(0, 0, 0), new Vector3(0, 32, 0)) },
            segments);
    }

    /// <summary>A tilted plane slants its line across the floor.</summary>
    [Fact]
    public void ATiltedPlaneSlantsAcrossTheFloor()
    {
        Vector3 normal = new Vector3(1, 0, -1).Normalized();
        var plane = new Plane(normal, new Vector3(16, 0, 16));

        HashSet<(Vector3, Vector3)> segments =
            Segments(SymmetryWallTrace.TraceWall(Box, 1, 0, plane));

        Assert.Equal(
            new HashSet<(Vector3, Vector3)> { (new Vector3(0, 0, 0), new Vector3(32, 0, 32)) },
            segments);
    }

    [Fact]
    public void InsideTheRegionEveryWallShows()
    {
        var camera = new Vector3(16, 16, 16);

        for (var axis = 0; axis < 3; axis++)
        {
            Assert.True(SymmetryWallTrace.IsWallVisible(axis, 0, Box, camera));
            Assert.True(SymmetryWallTrace.IsWallVisible(axis, 1, Box, camera));
        }
    }

    /// <summary>From outside, the walls between the camera and the room drop out.</summary>
    [Fact]
    public void OutsideTheRegionTheNearWallDrops()
    {
        var camera = new Vector3(50, 16, 16);

        Assert.True(SymmetryWallTrace.IsWallVisible(0, 0, Box, camera));
        Assert.False(SymmetryWallTrace.IsWallVisible(0, 1, Box, camera));
        Assert.True(SymmetryWallTrace.IsWallVisible(1, 0, Box, camera));
        Assert.True(SymmetryWallTrace.IsWallVisible(2, 1, Box, camera));
    }
}
