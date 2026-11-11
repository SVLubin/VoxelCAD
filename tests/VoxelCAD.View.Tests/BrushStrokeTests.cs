using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What a brush stroke decides for itself: the mode latched at the press, a cell
/// written once however often the hover proposes it, and the frozen surface —
/// the picker reading the stroke's own track as it stood before the stroke.
/// </summary>
public sealed class BrushStrokeTests
{
    private static readonly Guid Material = Guid.NewGuid();

    [Fact]
    public void TheModeIsLatchedAtThePress()
    {
        Assert.True(new BrushStroke(alternate: true).Alternate);
        Assert.False(new BrushStroke(alternate: false).Alternate);
    }

    [Fact]
    public void ACellIsNewUntilItIsWritten()
    {
        var stroke = new BrushStroke(alternate: false);
        var cell = new Vector3I(3, 0, 3);

        Assert.True(stroke.IsNew(cell));

        stroke.Record(new[] { cell });

        Assert.False(stroke.IsNew(cell));
        Assert.True(stroke.IsNew(new Vector3I(4, 0, 3)));
    }

    /// <summary>
    /// The mirror images a step wrote are the stroke's too: the pointer drifting
    /// across the plane onto its own image must not write it again.
    /// </summary>
    [Fact]
    public void AStepRecordsEveryCellItWroteImagesIncluded()
    {
        var stroke = new BrushStroke(alternate: false);

        stroke.Record(new[] { new Vector3I(1, 0, 0), new Vector3I(4, 0, 0) });

        Assert.False(stroke.IsNew(new Vector3I(4, 0, 0)));
        Assert.Equal(2, stroke.Written.Count);
    }

    /// <summary>
    /// The defect the frozen surface exists for. A ray from above lands on the
    /// floor at (3,0,3) and the stroke places (3,0,3); on the next frame the same
    /// ray meets the cell it just placed and would propose the one above it — a
    /// tower where the hand rested. Read through the stroke's own cells, the ray
    /// still lands where it did.
    /// </summary>
    [Fact]
    public void APlacingStrokeReadsThroughWhatItPlaced()
    {
        var stroke = new BrushStroke(alternate: false);
        var placed = new Vector3I(3, 0, 3);
        var voxels = new Dictionary<Vector3I, Guid> { [placed] = Material };

        stroke.Record(new[] { placed });

        // Straight down through the middle of the cell, from well above it.
        bool hit = VoxelPicker.TryPickVoxels(
            new Vector3(3.5f, 10f, 3.5f),
            Vector3.Down,
            voxels,
            stroke.Written,
            restored: null,
            out _,
            out _,
            out _);

        Assert.False(hit);

        // And with no stroke reading through it, the very cell is met on its top face.
        Assert.True(VoxelPicker.TryPickVoxels(
            new Vector3(3.5f, 10f, 3.5f),
            Vector3.Down,
            voxels,
            out Vector3I cell,
            out Vector3I face,
            out _));
        Assert.Equal(placed, cell);
        Assert.Equal(Vector3I.Up, face);
    }

    /// <summary>
    /// The mirror image: an erased cell is still met, so the ray does not reach
    /// the cell behind it and the stroke does not dig where the hand pauses.
    /// </summary>
    [Fact]
    public void AnErasingStrokeStillMeetsWhatItErased()
    {
        var stroke = new BrushStroke(alternate: true);
        var erased = new Vector3I(3, 1, 3);
        var behind = new Vector3I(3, 0, 3);
        var voxels = new Dictionary<Vector3I, Guid> { [behind] = Material };

        stroke.Record(new[] { erased });

        Assert.True(VoxelPicker.TryPickVoxels(
            new Vector3(3.5f, 10f, 3.5f),
            Vector3.Down,
            voxels,
            hidden: null,
            restored: stroke.Written,
            out Vector3I cell,
            out Vector3I face,
            out float distance));

        Assert.Equal(erased, cell);
        Assert.Equal(Vector3I.Up, face);
        Assert.True(Mathf.IsEqualApprox(distance, 8f));
    }

    /// <summary>
    /// Hidden cells only hide themselves: a cell the stroke did not place is met
    /// as before, and the nearest surviving cell wins.
    /// </summary>
    [Fact]
    public void HidingOneCellRevealsTheOneBehindIt()
    {
        var stroke = new BrushStroke(alternate: false);
        var top = new Vector3I(3, 1, 3);
        var bottom = new Vector3I(3, 0, 3);
        var voxels = new Dictionary<Vector3I, Guid> { [top] = Material, [bottom] = Material };

        stroke.Record(new[] { top });

        Assert.True(VoxelPicker.TryPickVoxels(
            new Vector3(3.5f, 10f, 3.5f),
            Vector3.Down,
            voxels,
            stroke.Written,
            restored: null,
            out Vector3I cell,
            out _,
            out _));

        Assert.Equal(bottom, cell);
    }

    /// <summary>The two-set overload with nothing in either set is the plain walk.</summary>
    [Fact]
    public void EmptySetsAreTheOrdinaryPick()
    {
        var voxels = new Dictionary<Vector3I, Guid> { [new Vector3I(3, 0, 3)] = Material };

        Assert.True(VoxelPicker.TryPickVoxels(
            new Vector3(3.5f, 10f, 3.5f),
            Vector3.Down,
            voxels,
            new HashSet<Vector3I>(),
            Array.Empty<Vector3I>(),
            out Vector3I cell,
            out _,
            out _));

        Assert.Equal(new Vector3I(3, 0, 3), cell);
    }
}
