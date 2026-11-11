using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// Face culling on its own: a shared face is hidden only between neighbours of
/// the same opacity, so an opaque slab under glass still shows its lid. These
/// exercise the engine-free decision, not the ArrayMesh it feeds.
/// </summary>
public sealed class VoxelMesherTests
{
    private const int PlusY = 2;
    private const int MinusY = 3;

    private static readonly Guid Bread = Guid.NewGuid();
    private static readonly Guid Glass = Guid.NewGuid();
    private static readonly Guid Stone = Guid.NewGuid();

    [Fact]
    public void ALoneVoxelShowsAllSixFaces()
    {
        var voxels = new Dictionary<Vector3I, Guid> { [Vector3I.Zero] = Bread };
        var opacity = new Dictionary<Guid, float> { [Bread] = 1f };

        Dictionary<Guid, List<VoxelMesher.VoxelFace>> faces =
            VoxelMesher.CollectVisibleFaces(voxels, opacity);

        Assert.Equal(6, faces[Bread].Count);
    }

    [Fact]
    public void TwoEquallyOpaqueVoxelsHideTheirSharedFace()
    {
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [Vector3I.Zero] = Stone,
            [Vector3I.Up] = Stone
        };
        var opacity = new Dictionary<Guid, float> { [Stone] = 1f };

        Dictionary<Guid, List<VoxelMesher.VoxelFace>> faces =
            VoxelMesher.CollectVisibleFaces(voxels, opacity);

        // Ten, not twelve: each cube drops the one face it shares with the other.
        Assert.Equal(10, faces[Stone].Count);
        Assert.DoesNotContain(new VoxelMesher.VoxelFace(Vector3I.Zero, PlusY), faces[Stone]);
        Assert.DoesNotContain(new VoxelMesher.VoxelFace(Vector3I.Up, MinusY), faces[Stone]);
    }

    [Fact]
    public void TwoEquallyTransparentVoxelsAlsoHideTheirSharedFace()
    {
        // "Same transparency" is about the opacity value, not about being opaque:
        // glass on identical glass occludes at the seam just as stone on stone.
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [Vector3I.Zero] = Glass,
            [Vector3I.Up] = Glass
        };
        var opacity = new Dictionary<Guid, float> { [Glass] = 0.4f };

        Dictionary<Guid, List<VoxelMesher.VoxelFace>> faces =
            VoxelMesher.CollectVisibleFaces(voxels, opacity);

        Assert.Equal(10, faces[Glass].Count);
    }

    [Fact]
    public void AnOpaqueVoxelUnderGlassKeepsItsTopFace()
    {
        // The glass-sandwich bug: culling the seam left the opaque slab without a
        // lid, so the eye passed through it. The bread keeps its whole box — and
        // the glass gives its floor up, or the two quads would sit in one plane
        // and blend twice.
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [Vector3I.Zero] = Bread,
            [Vector3I.Up] = Glass
        };
        var opacity = new Dictionary<Guid, float> { [Bread] = 1f, [Glass] = 0.4f };

        Dictionary<Guid, List<VoxelMesher.VoxelFace>> faces =
            VoxelMesher.CollectVisibleFaces(voxels, opacity);

        Assert.Equal(6, faces[Bread].Count);
        Assert.Equal(5, faces[Glass].Count);
        Assert.Contains(new VoxelMesher.VoxelFace(Vector3I.Zero, PlusY), faces[Bread]);
        Assert.DoesNotContain(new VoxelMesher.VoxelFace(Vector3I.Up, MinusY), faces[Glass]);
    }

    [Fact]
    public void AMaterialMissingFromTheMapCountsAsOpaque()
    {
        // An unmapped material defaults to fully opaque, so an unmapped solid
        // against glass still differs in opacity and keeps its seam.
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [Vector3I.Zero] = Stone,
            [Vector3I.Up] = Glass
        };
        var opacity = new Dictionary<Guid, float> { [Glass] = 0.4f };

        Dictionary<Guid, List<VoxelMesher.VoxelFace>> faces =
            VoxelMesher.CollectVisibleFaces(voxels, opacity);

        Assert.Equal(6, faces[Stone].Count);
        Assert.Contains(new VoxelMesher.VoxelFace(Vector3I.Zero, PlusY), faces[Stone]);
    }

    [Fact]
    public void TwoUnmappedVoxelsHideTheirSharedFaceAsBothOpaque()
    {
        // Both default to opaque, so they read as equal and the seam is culled.
        var voxels = new Dictionary<Vector3I, Guid>
        {
            [Vector3I.Zero] = Stone,
            [Vector3I.Up] = Stone
        };
        var opacity = new Dictionary<Guid, float>();

        Dictionary<Guid, List<VoxelMesher.VoxelFace>> faces =
            VoxelMesher.CollectVisibleFaces(voxels, opacity);

        Assert.Equal(10, faces[Stone].Count);
    }
}
