using System.Collections.Generic;
using Godot;
using VoxelCAD.Geometry;
using Xunit;

namespace VoxelCAD.Geometry.Tests;

/// <summary>
/// The mirror formula both sides of the view seam depend on: the brush writes
/// the orbit of a click, the preview offers the same orbit, and a one-cell
/// disagreement between them would make the preview lie.
/// </summary>
public sealed class VoxelSymmetryTests
{
    /// <summary>
    /// An even doubled plane sits on a cell boundary: the plane at 0 pairs cell 0
    /// with cell -1, and the plane at 3 (doubled 6) pairs 2 with 3.
    /// </summary>
    [Theory]
    [InlineData(0, 0, -1)]
    [InlineData(0, -1, 0)]
    [InlineData(6, 2, 3)]
    [InlineData(6, 0, 5)]
    public void ABoundaryPlanePairsTheCellsBesideIt(int planeDoubled, int coordinate, int expected) =>
        Assert.Equal(expected, VoxelSymmetry.MirrorCoordinate(coordinate, planeDoubled));

    /// <summary>
    /// An odd doubled plane runs through cell centres: the cell it runs through
    /// is its own image, and its neighbours pair across it.
    /// </summary>
    [Theory]
    [InlineData(7, 3, 3)]
    [InlineData(7, 2, 4)]
    [InlineData(7, 0, 6)]
    [InlineData(1, 0, 0)]
    public void ACentrePlaneMapsItsOwnCellToItself(int planeDoubled, int coordinate, int expected) =>
        Assert.Equal(expected, VoxelSymmetry.MirrorCoordinate(coordinate, planeDoubled));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-4)]
    public void MirroringTwiceIsTheIdentity(int planeDoubled)
    {
        for (var coordinate = -3; coordinate <= 3; coordinate++)
        {
            int once = VoxelSymmetry.MirrorCoordinate(coordinate, planeDoubled);

            Assert.Equal(coordinate, VoxelSymmetry.MirrorCoordinate(once, planeDoubled));
        }
    }

    [Fact]
    public void MirrorCellLeavesTheOtherAxesAlone()
    {
        var cell = new Vector3I(1, 2, 3);

        Assert.Equal(new Vector3I(4, 2, 3), VoxelSymmetry.MirrorCell(cell, 0, 6));
        Assert.Equal(new Vector3I(1, 2, 3), VoxelSymmetry.MirrorCell(cell, 1, 5));
        Assert.Equal(new Vector3I(1, 2, -4), VoxelSymmetry.MirrorCell(cell, 2, 0));
    }

    [Fact]
    public void NoPlanesGiveTheCellAlone()
    {
        IReadOnlyList<Vector3I> orbit = VoxelSymmetry.ExpandOrbit(new Vector3I(1, 2, 3), null, null, null);

        Assert.Equal(new[] { new Vector3I(1, 2, 3) }, orbit);
    }

    /// <summary>The cell itself always comes first, so a caller can rely on it.</summary>
    [Fact]
    public void OnePlaneGivesTheCellAndItsImage()
    {
        IReadOnlyList<Vector3I> orbit = VoxelSymmetry.ExpandOrbit(new Vector3I(1, 2, 3), 6, null, null);

        Assert.Equal(new[] { new Vector3I(1, 2, 3), new Vector3I(4, 2, 3) }, orbit);
    }

    [Fact]
    public void ThreePlanesGiveEightDistinctImages()
    {
        IReadOnlyList<Vector3I> orbit = VoxelSymmetry.ExpandOrbit(new Vector3I(0, 0, 0), 6, 6, 6);

        Assert.Equal(8, orbit.Count);
        Assert.Equal(orbit.Count, new HashSet<Vector3I>(orbit).Count);
        Assert.Contains(new Vector3I(5, 5, 5), orbit);
    }

    /// <summary>
    /// A cell lying on one of the planes is its own image there, so that axis
    /// contributes nothing and the orbit halves rather than repeating cells.
    /// </summary>
    [Fact]
    public void ACellOnOnePlaneHalvesTheOrbit()
    {
        IReadOnlyList<Vector3I> orbit = VoxelSymmetry.ExpandOrbit(new Vector3I(3, 0, 0), 7, 6, 6);

        Assert.Equal(4, orbit.Count);
        Assert.Equal(orbit.Count, new HashSet<Vector3I>(orbit).Count);
    }

    [Fact]
    public void ACellOnEveryPlaneIsItsWholeOrbit()
    {
        IReadOnlyList<Vector3I> orbit = VoxelSymmetry.ExpandOrbit(new Vector3I(3, 3, 3), 7, 7, 7);

        Assert.Equal(new[] { new Vector3I(3, 3, 3) }, orbit);
    }
}
