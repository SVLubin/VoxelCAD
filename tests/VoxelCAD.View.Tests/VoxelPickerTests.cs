using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The geometry behind a click: which cell was hit and on which face, so a
/// presenter can fill the cell in front or clear the one behind.
/// </summary>
public sealed class VoxelPickerTests
{
    private static readonly Guid Material = Guid.NewGuid();
    private static readonly Vector3I Size = new(32, 32, 32);

    [Fact]
    public void ARayStraightDownMeetsTheFloor()
    {
        bool hit = VoxelPicker.TryPickRegion(
            new Vector3(4.5f, 10f, 6.5f),
            Vector3.Down,
            Size,
            out Vector3 point,
            out Vector3 inward,
            out _);

        Assert.True(hit);
        Assert.Equal(Vector3.Up, inward);
        Assert.True(Mathf.IsZeroApprox(point.Y));
    }

    [Fact]
    public void AFloorHitFillsTheCellStandingOnTheFloor()
    {
        // The contract both kinds of hit share: Cell + Face is the empty cell a
        // plain click fills. Getting the sign wrong here aims the brush through
        // the floor, where edits are silently dropped as out of region.
        VoxelPicker.TryPickRegion(
            new Vector3(4.5f, 10f, 6.5f),
            Vector3.Down,
            Size,
            out Vector3 point,
            out Vector3 inward,
            out _);

        (Vector3I cell, Vector3I face) = VoxelPicker.ToRegionPick(point, VoxelPicker.ToAxis(inward));

        Assert.Equal(new Vector3I(4, 0, 6), cell + face);
        Assert.Equal(new Vector3I(0, 1, 0), face);
    }

    [Fact]
    public void AWallHitFillsTheCellAgainstTheWall()
    {
        VoxelPicker.TryPickRegion(
            new Vector3(-20f, 5.5f, 16.5f),
            Vector3.Right,
            Size,
            out Vector3 point,
            out Vector3 inward,
            out _);

        (Vector3I cell, Vector3I face) = VoxelPicker.ToRegionPick(point, VoxelPicker.ToAxis(inward));

        Assert.Equal(new Vector3I(31, 5, 16), cell + face);
    }

    [Fact]
    public void TheNearWallIsNotAHitWhenLookingInFromOutside()
    {
        // A camera sits outside the region; the wall it looks through must not
        // swallow the click, or nothing inside would ever be reachable.
        bool hit = VoxelPicker.TryPickRegion(
            new Vector3(-20f, 5f, 16f),
            Vector3.Right,
            Size,
            out Vector3 point,
            out Vector3 inward,
            out _);

        Assert.True(hit);
        Assert.Equal(32f, point.X, 3);
        Assert.Equal(Vector3.Left, inward);
    }

    [Fact]
    public void ARayMissingTheRegionEntirelyIsNotAHit()
    {
        // Parallel to the floor and far above the box: crosses the wall planes
        // only outside the walls themselves.
        Assert.False(VoxelPicker.TryPickRegion(
            new Vector3(-20f, 500f, 16f),
            Vector3.Right,
            Size,
            out _,
            out _,
            out _));
    }

    [Fact]
    public void ARayStraightDownHitsTheVoxelUnderIt()
    {
        var voxels = new Dictionary<Vector3I, Guid> { [new Vector3I(4, 0, 6)] = Material };

        bool hit = VoxelPicker.TryPickVoxels(
            new Vector3(4.5f, 10f, 6.5f),
            Vector3.Down,
            voxels,
            out Vector3I cell,
            out Vector3I face,
            out _);

        Assert.True(hit);
        Assert.Equal(new Vector3I(4, 0, 6), cell);
        Assert.Equal(new Vector3I(0, 1, 0), face);

        // What a plain click fills: one step along the face normal.
        Assert.Equal(new Vector3I(4, 1, 6), cell + face);
    }

    [Fact]
    public void ARayFromTheSideHitsTheFacingSideOfAVoxel()
    {
        var voxels = new Dictionary<Vector3I, Guid> { [new Vector3I(4, 0, 6)] = Material };

        VoxelPicker.TryPickVoxels(
            new Vector3(-5f, 0.5f, 6.5f),
            Vector3.Right,
            voxels,
            out Vector3I cell,
            out Vector3I face,
            out _);

        Assert.Equal(new Vector3I(4, 0, 6), cell);
        Assert.Equal(new Vector3I(-1, 0, 0), face);
        Assert.Equal(new Vector3I(3, 0, 6), cell + face);
    }

    [Fact]
    public void TheNearestVoxelWinsWhenSeveralAreInLine()
    {
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [new Vector3I(4, 0, 6)] = Material,
            [new Vector3I(4, 3, 6)] = Material,
            [new Vector3I(4, 6, 6)] = Material
        };

        VoxelPicker.TryPickVoxels(
            new Vector3(4.5f, 20f, 6.5f),
            Vector3.Down,
            voxels,
            out Vector3I cell,
            out _,
            out _);

        Assert.Equal(new Vector3I(4, 6, 6), cell);
    }

    [Fact]
    public void AVoxelBehindTheRayIsIgnored()
    {
        var voxels = new Dictionary<Vector3I, Guid> { [new Vector3I(4, 10, 6)] = Material };

        Assert.False(VoxelPicker.TryPickVoxels(
            new Vector3(4.5f, 5f, 6.5f),
            Vector3.Down,
            voxels,
            out _,
            out _,
            out _));
    }

    [Fact]
    public void AVoxelIsNearerThanTheFloorBehindIt()
    {
        var voxels = new Dictionary<Vector3I, Guid> { [new Vector3I(4, 0, 6)] = Material };
        var origin = new Vector3(4.5f, 10f, 6.5f);

        VoxelPicker.TryPickRegion(origin, Vector3.Down, Size, out _, out _, out float floorDistance);
        VoxelPicker.TryPickVoxels(origin, Vector3.Down, voxels, out _, out _, out float voxelDistance);

        Assert.True(voxelDistance < floorDistance);
    }

    [Theory]
    [InlineData(0.9f, 0.1f, 0f, 1, 0, 0)]
    [InlineData(-0.2f, -0.95f, 0.1f, 0, -1, 0)]
    [InlineData(0f, 0.1f, -0.99f, 0, 0, -1)]
    public void ADirectionRoundsToTheAxisItMostlyPointsAlong(
        float x,
        float y,
        float z,
        int expectedX,
        int expectedY,
        int expectedZ)
    {
        Assert.Equal(
            new Vector3I(expectedX, expectedY, expectedZ),
            VoxelPicker.ToAxis(new Vector3(x, y, z)));
    }
}
