using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.Geometry;
using Xunit;

namespace VoxelCAD.Geometry.Tests;

/// <summary>
/// The one answer both meshers cull by. The viewport and the exporter used to
/// carry private copies of these; a disagreement between them would have been a
/// model showing different faces on screen and in a file.
/// </summary>
public sealed class VoxelFacesTests
{
    [Fact]
    public void OffsetsAreTheSixUnitAxes()
    {
        var seen = new HashSet<Vector3I>();

        for (var face = 0; face < VoxelFaces.FaceCount; face++)
        {
            Vector3I offset = VoxelFaces.GetNeighborOffset(face);

            Assert.Equal(1, Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z));
            Assert.True(seen.Add(offset), $"Face {face} repeats offset {offset}.");
        }

        Assert.Equal(VoxelFaces.FaceCount, seen.Count);
    }

    [Fact]
    public void OppositeFacesAreAdjacentPairs()
    {
        // The +/− pairing per axis is part of the index contract the corner
        // tables are built against, not an accident of listing order.
        for (var axis = 0; axis < 3; axis++)
        {
            Assert.Equal(
                VoxelFaces.GetNeighborOffset(axis * 2),
                -VoxelFaces.GetNeighborOffset(axis * 2 + 1));
        }
    }

    [Fact]
    public void FaceIndexOutOfRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VoxelFaces.GetNeighborOffset(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => VoxelFaces.GetNeighborOffset(-1));
    }

    [Fact]
    public void AbsentNeighborAlwaysShowsTheFace()
    {
        Assert.True(VoxelFaces.ShouldEmitFace(1f, neighborPresent: false, neighborOpacity: 1f));
        Assert.True(VoxelFaces.ShouldEmitFace(0.5f, neighborPresent: false, neighborOpacity: 0.5f));
    }

    [Fact]
    public void EquallyOpaqueNeighborsHideTheirSeam()
    {
        Assert.False(VoxelFaces.ShouldEmitFace(1f, neighborPresent: true, neighborOpacity: 1f));
        Assert.False(VoxelFaces.ShouldEmitFace(0.5f, neighborPresent: true, neighborOpacity: 0.5f));
    }

    /// <summary>
    /// The glass-over-bread case: the opaque cell must show its surface through
    /// the transparent one sitting on it, or the solid reads as a hollow shell.
    /// </summary>
    [Fact]
    public void TheDenserSideOfAnUnequalSeamShowsItsFace()
    {
        Assert.True(VoxelFaces.ShouldEmitFace(1f, neighborPresent: true, neighborOpacity: 0.5f));
    }

    /// <summary>
    /// And the thinner side does not, which is the half the first version of this
    /// rule got wrong: two coincident quads at one plane are not a seam but a coin
    /// toss, and the loser blends over the winner. Seen on a block where a pane at
    /// 0.25 met one at 0.75.
    /// </summary>
    [Fact]
    public void TheThinnerSideOfAnUnequalSeamDoesNot()
    {
        Assert.False(VoxelFaces.ShouldEmitFace(0.5f, neighborPresent: true, neighborOpacity: 1f));
        Assert.False(VoxelFaces.ShouldEmitFace(0.25f, neighborPresent: true, neighborOpacity: 0.75f));
    }

    /// <summary>
    /// Whatever two opacities meet, exactly one face is emitted between them — the
    /// property the whole rule exists for, stated as itself rather than left to be
    /// inferred from the two cases above.
    /// </summary>
    [Fact]
    public void ASeamNeverEmitsTwoFaces()
    {
        float[] opacities = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        foreach (float mine in opacities)
        {
            foreach (float theirs in opacities)
            {
                bool here = VoxelFaces.ShouldEmitFace(mine, neighborPresent: true, theirs);
                bool there = VoxelFaces.ShouldEmitFace(theirs, neighborPresent: true, mine);

                Assert.False(here && there, $"{mine} against {theirs} emitted both faces.");
            }
        }
    }

    [Fact]
    public void UnknownMaterialReadsAsFullyOpaque()
    {
        var opacityById = new Dictionary<Guid, float> { [Guid.NewGuid()] = 0.25f };

        Assert.Equal(1f, VoxelFaces.GetOpacity(Guid.NewGuid(), opacityById));
    }

    [Fact]
    public void KnownMaterialReadsItsOwnOpacity()
    {
        var id = Guid.NewGuid();
        var opacityById = new Dictionary<Guid, float> { [id] = 0.25f };

        Assert.Equal(0.25f, VoxelFaces.GetOpacity(id, opacityById));
    }
}
