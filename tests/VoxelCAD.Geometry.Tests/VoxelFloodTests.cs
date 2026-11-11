using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.Geometry;
using Xunit;

namespace VoxelCAD.Geometry.Tests;

/// <summary>
/// The connected volume a fill recolours: six-connected, bounded by material,
/// and never through empty cells.
/// </summary>
public sealed class VoxelFloodTests
{
    private static readonly Guid Red = Guid.NewGuid();
    private static readonly Guid Blue = Guid.NewGuid();

    [Fact]
    public void AnEmptySeedIsNoVolume()
    {
        var voxels = new Dictionary<Vector3I, Guid> { [new Vector3I(1, 0, 0)] = Red };

        Assert.Empty(VoxelFlood.CollectConnected(voxels, new Vector3I(0, 0, 0)));
    }

    [Fact]
    public void TheVolumeStopsAtAnotherMaterial()
    {
        // A bar: two red cells, a blue one, two red ones.
        var voxels = new Dictionary<Vector3I, Guid>();

        for (var x = 0; x < 5; x++)
            voxels[new Vector3I(x, 0, 0)] = x == 2 ? Blue : Red;

        IReadOnlyList<Vector3I> volume = VoxelFlood.CollectConnected(voxels, new Vector3I(0, 0, 0));

        Assert.Equal(2, volume.Count);
        Assert.Equal(new Vector3I(0, 0, 0), volume[0]);
        Assert.Contains(new Vector3I(1, 0, 0), volume);
    }

    /// <summary>
    /// Two regions of one material that only touch at an edge are two regions: a
    /// checkerboard must not flood whole.
    /// </summary>
    [Fact]
    public void AnEdgeContactIsNotAConnection()
    {
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [new Vector3I(0, 0, 0)] = Red,
            [new Vector3I(1, 1, 0)] = Red,
            [new Vector3I(1, 1, 1)] = Red,
        };

        IReadOnlyList<Vector3I> volume = VoxelFlood.CollectConnected(voxels, new Vector3I(0, 0, 0));

        Assert.Single(volume);
    }

    [Fact]
    public void AGapIsNotCrossed()
    {
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [new Vector3I(0, 0, 0)] = Red,
            [new Vector3I(2, 0, 0)] = Red,
        };

        Assert.Single(VoxelFlood.CollectConnected(voxels, new Vector3I(0, 0, 0)));
    }

    /// <summary>With no plane on, the orbit answer is the plain one.</summary>
    [Fact]
    public void WithNoMirrorTheOrbitIsTheOneVolume()
    {
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [new Vector3I(0, 0, 0)] = Red,
            [new Vector3I(1, 0, 0)] = Red,
            [new Vector3I(5, 0, 0)] = Red,
        };

        Assert.Equal(
            2,
            VoxelFlood.CollectOrbitConnected(voxels, new Vector3I(0, 0, 0), null, null, null).Count);
    }

    /// <summary>
    /// The defect the orbit answer exists for: a click under a mirror plane acts
    /// on both sides, so a fill floods a volume per image — and the tool and the
    /// command both ask this one function, so neither can answer differently.
    /// </summary>
    [Fact]
    public void EachImageSeedsAVolumeOfItsOwn()
    {
        // Plane at x=4 (doubled 8): 0..2 pairs with 7..5.
        var voxels = new Dictionary<Vector3I, Guid>();

        for (var x = 0; x < 3; x++)
            voxels[new Vector3I(x, 0, 0)] = Red;

        for (var x = 5; x < 8; x++)
            voxels[new Vector3I(x, 0, 0)] = Red;

        IReadOnlyList<Vector3I> volume =
            VoxelFlood.CollectOrbitConnected(voxels, new Vector3I(1, 0, 0), 8, null, null);

        Assert.Equal(6, volume.Count);
        Assert.Equal(volume.Count, new HashSet<Vector3I>(volume).Count);
    }

    /// <summary>An image landing on empty space seeds nothing.</summary>
    [Fact]
    public void AnImageOnEmptySpaceSeedsNothing()
    {
        var voxels = new Dictionary<Vector3I, Guid> { [new Vector3I(0, 0, 0)] = Red };

        Assert.Single(VoxelFlood.CollectOrbitConnected(voxels, new Vector3I(0, 0, 0), 8, null, null));
    }

    /// <summary>
    /// And neither does an image landing inside a region of <em>another</em>
    /// material: a fill recolours the region the click named, and the mirror
    /// repeats it only where the other side wears the same material.
    /// </summary>
    [Fact]
    public void AnImageInsideAnotherMaterialSeedsNothing()
    {
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [new Vector3I(0, 0, 0)] = Red,
            [new Vector3I(7, 0, 0)] = Blue,
            [new Vector3I(8, 0, 0)] = Blue,
        };

        IReadOnlyList<Vector3I> volume =
            VoxelFlood.CollectOrbitConnected(voxels, new Vector3I(0, 0, 0), 8, null, null);

        Assert.Equal(new[] { new Vector3I(0, 0, 0) }, volume);
    }

    /// <summary>
    /// The seeds are what the marker shows and what the flood starts from: the
    /// cell, and its images of the same material. Two planes, so three images —
    /// one of the clicked material, one of another, one on empty space.
    /// </summary>
    [Fact]
    public void TheSeedsAreTheCellAndItsImagesOfTheSameMaterial()
    {
        // Planes at x=4 and z=4 (doubled 8): 1 pairs with 6 on each axis.
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [new Vector3I(1, 0, 1)] = Red,
            [new Vector3I(6, 0, 1)] = Red,
            [new Vector3I(1, 0, 6)] = Blue,
        };

        IReadOnlyList<Vector3I> seeds =
            VoxelFlood.CollectOrbitSeeds(voxels, new Vector3I(1, 0, 1), 8, null, 8);

        Assert.Equal(new[] { new Vector3I(1, 0, 1), new Vector3I(6, 0, 1) }, seeds);
    }

    [Fact]
    public void AnEmptyCellHasNoSeeds()
    {
        var voxels = new Dictionary<Vector3I, Guid> { [new Vector3I(6, 0, 0)] = Red };

        Assert.Empty(VoxelFlood.CollectOrbitSeeds(voxels, new Vector3I(1, 0, 0), 8, null, null));
    }

    [Fact]
    public void ABlockFloodsWholeAlongAllThreeAxes()
    {
        var voxels = new Dictionary<Vector3I, Guid>();

        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        for (var z = 0; z < 3; z++)
            voxels[new Vector3I(x, y, z)] = Red;

        Assert.Equal(27, VoxelFlood.CollectConnected(voxels, new Vector3I(2, 2, 2)).Count);
    }
}
