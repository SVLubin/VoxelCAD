using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.Geometry;
using Xunit;

namespace VoxelCAD.Geometry.Tests;

/// <summary>
/// What "mirror now" writes: the images of the filled cells that land on empty
/// ones, with whatever already stands there left alone.
/// </summary>
public sealed class VoxelMirrorImagesTests
{
    private static readonly Guid Red = Guid.NewGuid();
    private static readonly Guid Blue = Guid.NewGuid();

    /// <summary>Plane at x=3 (doubled 6): cell 1 pairs with cell 4, cell 2 with 3.</summary>
    private const int PlaneDoubled = 6;

    [Fact]
    public void EveryCellGetsAnImageCarryingItsOwnMaterial()
    {
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [new Vector3I(1, 0, 0)] = Red,
            [new Vector3I(2, 5, 7)] = Blue,
        };

        IReadOnlyList<(Vector3I Cell, Guid Material)> images =
            VoxelSymmetry.CollectMirrorImages(voxels, 0, PlaneDoubled);

        Assert.Equal(2, images.Count);
        Assert.Contains((new Vector3I(4, 0, 0), Red), images);
        Assert.Contains((new Vector3I(3, 5, 7), Blue), images);
    }

    /// <summary>The existing cell wins: the model is the original, the mirror is not allowed to repaint it.</summary>
    [Fact]
    public void AnOccupiedImageIsLeftAlone()
    {
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [new Vector3I(1, 0, 0)] = Red,
            [new Vector3I(4, 0, 0)] = Blue,
        };

        Assert.Empty(VoxelSymmetry.CollectMirrorImages(voxels, 0, PlaneDoubled));
    }

    [Fact]
    public void ASymmetricModelGainsNothing()
    {
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [new Vector3I(2, 0, 0)] = Red,
            [new Vector3I(3, 0, 0)] = Red,
        };

        Assert.Empty(VoxelSymmetry.CollectMirrorImages(voxels, 0, PlaneDoubled));
    }

    /// <summary>A cell centred on the plane is its own image, and is not written twice.</summary>
    [Fact]
    public void ACellOnThePlaneIsItsOwnImage()
    {
        // Plane through the centre of cell 3 (doubled 7): cell 3 maps to itself.
        var voxels = new Dictionary<Vector3I, Guid> { [new Vector3I(3, 0, 0)] = Red };

        Assert.Empty(VoxelSymmetry.CollectMirrorImages(voxels, 0, 7));
    }

    [Fact]
    public void TheAxisIsHonoured()
    {
        var voxels = new Dictionary<Vector3I, Guid> { [new Vector3I(0, 1, 0)] = Red };

        IReadOnlyList<(Vector3I Cell, Guid Material)> images =
            VoxelSymmetry.CollectMirrorImages(voxels, 1, PlaneDoubled);

        Assert.Equal(new Vector3I(0, 4, 0), images.Single().Cell);
    }
}
