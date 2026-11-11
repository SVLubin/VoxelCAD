using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.Geometry;
using Xunit;

namespace VoxelCAD.Geometry.Tests;

/// <summary>
/// The face an extrude click takes hold of, and the cells pushing it writes. Both
/// the edit and the marker drawn before it come from here, so what these hold is
/// that the two cannot disagree.
/// </summary>
public sealed class VoxelFacePatchTests
{
    private static readonly Guid Material = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    /// <summary>The +Y face index, per the shared face order.</summary>
    private const int Up = 2;

    private const int Right = 0;

    [Fact]
    public void ThePatchIsTheWholeConnectedTopOfASlab()
    {
        // A 3x1x3 slab: every cell shows its top to the air.
        Dictionary<Vector3I, Guid> voxels = Slab(3, 3);

        IReadOnlyList<Vector3I> patch =
            VoxelFacePatch.CollectPatch(voxels, new Vector3I(1, 0, 1), Up);

        Assert.Equal(9, patch.Count);
        Assert.Equal(new Vector3I(1, 0, 1), patch[0]);
        Assert.Equal(9, patch.Distinct().Count());
    }

    /// <summary>
    /// The patch is the run of surface, not the run of cells: a cell with something
    /// standing on it shows no top, and the walk stops there.
    /// </summary>
    [Fact]
    public void ACoveredCellIsNotPartOfTheFace()
    {
        Dictionary<Vector3I, Guid> voxels = Slab(3, 1);
        voxels[new Vector3I(1, 1, 0)] = Material;

        IReadOnlyList<Vector3I> patch =
            VoxelFacePatch.CollectPatch(voxels, new Vector3I(0, 0, 0), Up);

        Assert.Equal(new[] { new Vector3I(0, 0, 0) }, patch);
    }

    /// <summary>What the eye reads as one face is one face, however it is painted.</summary>
    [Fact]
    public void AStripeOfAnotherMaterialDoesNotSplitTheFace()
    {
        Dictionary<Vector3I, Guid> voxels = Slab(3, 1);
        voxels[new Vector3I(1, 0, 0)] = Other;

        Assert.Equal(3, VoxelFacePatch.CollectPatch(voxels, new Vector3I(0, 0, 0), Up).Count);
    }

    [Fact]
    public void AnEmptySeedHasNoFace()
    {
        Assert.Empty(VoxelFacePatch.CollectPatch(Slab(2, 2), new Vector3I(9, 9, 9), Up));
    }

    [Fact]
    public void ABuriedFaceIsNoFace()
    {
        Dictionary<Vector3I, Guid> voxels = Slab(2, 1);
        voxels[new Vector3I(0, 1, 0)] = Material;

        Assert.Empty(VoxelFacePatch.CollectPatch(voxels, new Vector3I(0, 0, 0), Up));
    }

    [Fact]
    public void PushingOutWritesTheLayerInFrontOfThePatch()
    {
        IReadOnlyList<Vector3I> cells = VoxelFacePatch.CollectExtrusion(
            Slab(2, 1), new Vector3I(0, 0, 0), Up, layers: 1, null, null, null);

        Assert.Equal(2, cells.Count);
        Assert.All(cells, cell => Assert.Equal(1, cell.Y));
    }

    [Fact]
    public void MoreLayersStackNearestFirst()
    {
        IReadOnlyList<Vector3I> cells = VoxelFacePatch.CollectExtrusion(
            Slab(1, 1), new Vector3I(0, 0, 0), Up, layers: 3, null, null, null);

        Assert.Equal(
            new[] { new Vector3I(0, 1, 0), new Vector3I(0, 2, 0), new Vector3I(0, 3, 0) },
            cells);
    }

    /// <summary>Carving starts on the surface itself — the layer being removed.</summary>
    [Fact]
    public void CarvingInStartsWithThePatchsOwnCells()
    {
        Dictionary<Vector3I, Guid> voxels = new()
        {
            [new Vector3I(0, 0, 0)] = Material,
            [new Vector3I(0, 1, 0)] = Material,
            [new Vector3I(0, 2, 0)] = Material
        };

        IReadOnlyList<Vector3I> cells = VoxelFacePatch.CollectExtrusion(
            voxels, new Vector3I(0, 2, 0), Up, layers: -2, null, null, null);

        Assert.Equal(new[] { new Vector3I(0, 2, 0), new Vector3I(0, 1, 0) }, cells);
    }

    [Fact]
    public void ZeroLayersWriteNothing()
    {
        Assert.Empty(VoxelFacePatch.CollectExtrusion(
            Slab(2, 2), new Vector3I(0, 0, 0), Up, layers: 0, null, null, null));
    }

    /// <summary>
    /// The whole reason mirroring is applied to the written cells rather than to
    /// the patch: reflecting a cell in front of a face lands in front of the
    /// mirrored face, so the other side grows outward too rather than inward.
    /// </summary>
    [Fact]
    public void AMirroredExtrusionRunsTheMirroredWay()
    {
        // Two cells either side of the plane at x = 2, each showing its +X face.
        Dictionary<Vector3I, Guid> voxels = new()
        {
            [new Vector3I(2, 0, 0)] = Material,
            [new Vector3I(1, 0, 0)] = Material
        };

        // Doubled plane 4 => x = 2: cell 1 mirrors to 2, cell 2 to 1.
        IReadOnlyList<Vector3I> cells = VoxelFacePatch.CollectExtrusion(
            voxels, new Vector3I(2, 0, 0), Right, layers: 1, planeDoubledX: 4, null, null);

        Assert.Contains(new Vector3I(3, 0, 0), cells);
        Assert.Contains(new Vector3I(0, 0, 0), cells);
        Assert.Equal(2, cells.Count);
    }

    /// <summary>A cell that is its own image is written once, not twice.</summary>
    [Fact]
    public void MirroredCellsAreNotDuplicated()
    {
        var voxels = new Dictionary<Vector3I, Guid> { [new Vector3I(0, 0, 0)] = Material };

        IReadOnlyList<Vector3I> cells = VoxelFacePatch.CollectExtrusion(
            voxels, new Vector3I(0, 0, 0), Up, layers: 1, planeDoubledX: 1, null, null);

        Assert.Equal(new[] { new Vector3I(0, 1, 0) }, cells);
    }

    /// <summary>One click is one layer, and both sides of the seam measure in it.</summary>
    [Fact]
    public void AClickIsOneLayer() => Assert.Equal(1, VoxelFacePatch.ClickLayers);

    private static Dictionary<Vector3I, Guid> Slab(int width, int depth)
    {
        var voxels = new Dictionary<Vector3I, Guid>();

        for (var x = 0; x < width; x++)
        for (var z = 0; z < depth; z++)
            voxels[new Vector3I(x, 0, z)] = Material;

        return voxels;
    }
}
