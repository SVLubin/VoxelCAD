using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// The corner shade on its own: which cells crowd a corner, and which way a quad
/// should be split so the shading spreads instead of creasing. No mesh here — the
/// arithmetic that feeds one.
/// </summary>
public sealed class VoxelAmbientOcclusionTests
{
    private static readonly Guid Stone = Guid.NewGuid();

    // The +Y face, and its four corners as VoxelMesher winds them.
    private static readonly Vector3I Up = new(0, 1, 0);
    private static readonly Vector3I CornerNearLeft = new(0, 1, 0);
    private static readonly Vector3I CornerNearRight = new(1, 1, 0);
    private static readonly Vector3I CornerFarRight = new(1, 1, 1);
    private static readonly Vector3I CornerFarLeft = new(0, 1, 1);

    private const float Occluded = 1f - VoxelAmbientOcclusion.OcclusionStrength;

    [Fact]
    public void ALoneVoxelIsShadedNowhere()
    {
        uint neighborhood = NeighborhoodOf(Vector3I.Zero);

        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerNearLeft));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerNearRight));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerFarRight));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerFarLeft));
    }

    [Fact]
    public void ANeighborAcrossAnEdgeShadesThatEdgesTwoCorners()
    {
        // A block resting on the slab, one cell over in +X: the top face of the cell
        // below it keeps its two far corners and loses the two against the block.
        uint neighborhood = NeighborhoodOf(Vector3I.Zero, new Vector3I(1, 1, 0));

        Assert.Equal(Occluded, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerNearRight));
        Assert.Equal(Occluded, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerFarRight));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerNearLeft));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerFarLeft));
    }

    [Fact]
    public void ADiagonalNeighborShadesOneCornerAlone()
    {
        uint neighborhood = NeighborhoodOf(Vector3I.Zero, new Vector3I(1, 1, 1));

        Assert.Equal(Occluded, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerFarRight));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerNearLeft));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerNearRight));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerFarLeft));
    }

    [Fact]
    public void TheCellDirectlyOnTopShadesNothing()
    {
        // It is the neighbour that hides the face outright rather than shading it,
        // so no corner may read it as an occluder.
        uint neighborhood = NeighborhoodOf(Vector3I.Zero, new Vector3I(0, 1, 0));

        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerNearLeft));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerNearRight));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerFarRight));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerFarLeft));
    }

    [Fact]
    public void ANeighborBesideTheCellShadesNothingAbove()
    {
        // Level with the cell, not above it: it crowds the side face, never the top.
        uint neighborhood = NeighborhoodOf(Vector3I.Zero, new Vector3I(1, 0, 0));

        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerNearRight));
        Assert.Equal(1f, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerFarRight));
    }

    [Fact]
    public void EveryFaceOfAnEnclosedCellIsShadedAtEveryCorner()
    {
        var cellsList = new List<Vector3I>();

        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                    cellsList.Add(new Vector3I(dx, dy, dz));
            }
        }

        uint neighborhood = NeighborhoodOf(cellsList.ToArray());

        Assert.Equal(Occluded, VoxelAmbientOcclusion.GetCornerShade(neighborhood, Up, CornerNearLeft));
        Assert.Equal(
            Occluded,
            VoxelAmbientOcclusion.GetCornerShade(neighborhood, new Vector3I(-1, 0, 0), new Vector3I(0, 1, 1)));
        Assert.Equal(
            Occluded,
            VoxelAmbientOcclusion.GetCornerShade(neighborhood, new Vector3I(0, 0, -1), new Vector3I(1, 1, 0)));
    }

    [Fact]
    public void AQuadKeepsItsDiagonalWhenTheDarkCornerIsOnIt()
    {
        Assert.False(VoxelAmbientOcclusion.ShouldFlipDiagonal(Occluded, 1f, 1f, 1f));
        Assert.False(VoxelAmbientOcclusion.ShouldFlipDiagonal(1f, 1f, Occluded, 1f));
    }

    [Fact]
    public void AQuadFlipsItsDiagonalWhenTheDarkCornerIsOffIt()
    {
        Assert.True(VoxelAmbientOcclusion.ShouldFlipDiagonal(1f, Occluded, 1f, 1f));
        Assert.True(VoxelAmbientOcclusion.ShouldFlipDiagonal(1f, 1f, 1f, Occluded));
    }

    [Fact]
    public void AnEvenlyShadedQuadIsNotFlipped()
    {
        Assert.False(VoxelAmbientOcclusion.ShouldFlipDiagonal(1f, 1f, 1f, 1f));
        Assert.False(VoxelAmbientOcclusion.ShouldFlipDiagonal(Occluded, 1f, Occluded, 1f));
    }

    private static uint NeighborhoodOf(params Vector3I[] cells)
    {
        var voxels = new Dictionary<Vector3I, Guid>();

        foreach (Vector3I cell in cells)
            voxels[cell] = Stone;

        return VoxelAmbientOcclusion.GetNeighborhood(voxels, Vector3I.Zero);
    }

    /// <summary>
    /// An opaque surface keeps exactly the occlusion it always had — the guard that
    /// says fading it for glass moved nothing anybody has ever looked at.
    /// </summary>
    [Fact]
    public void AnOpaqueSurfaceKeepsItsOcclusion()
    {
        Assert.Equal(0.6f, VoxelAmbientOcclusion.FadeShade(0.6f, 1f), 5);
        Assert.Equal(1f, VoxelAmbientOcclusion.FadeShade(1f, 1f), 5);
    }

    /// <summary>
    /// A fully clear one keeps none of it, and everything between is proportional,
    /// so there is no threshold at which the folds suddenly clean themselves.
    /// </summary>
    [Fact]
    public void OcclusionFadesWithTheSurfaceCarryingIt()
    {
        Assert.Equal(1f, VoxelAmbientOcclusion.FadeShade(0.6f, 0f), 5);
        Assert.Equal(0.8f, VoxelAmbientOcclusion.FadeShade(0.6f, 0.5f), 5);
        Assert.Equal(0.9f, VoxelAmbientOcclusion.FadeShade(0.6f, 0.25f), 5);
    }

    /// <summary>
    /// And it only ever lightens: a fade that could darken would be a second
    /// occlusion rather than a weakening of the one there is.
    /// </summary>
    [Fact]
    public void FadingNeverDarkens()
    {
        for (var shade = 0; shade <= 10; shade++)
        {
            for (var alpha = 0; alpha <= 10; alpha++)
                Assert.True(
                    VoxelAmbientOcclusion.FadeShade(shade / 10f, alpha / 10f) >= shade / 10f - 1e-5f);
        }
    }
}
