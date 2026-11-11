using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// One element's geometry, split into what the depth buffer can order and what
/// the engine has to sort: the opaque materials as one mesh, and each transparent
/// material as a mesh of its own.
/// </summary>
/// <remarks>
/// The split exists because Godot's alpha pass sorts <b>per instance</b>. Surfaces
/// inside one mesh are drawn in the order they were added, which is a dictionary's
/// order over material ids — a hash, fixed for the session and unrelated to where
/// the camera is. So two panes of different transparent materials in one element
/// blended the same way from either side: measured, a red pane and a blue pane at
/// alpha 0.5 read R/B 1.18 from the front and 1.20 from the back, where the
/// nearer one should dominate each time. The identical pair as two elements —
/// two instances — read 0.68 and 1.20, which is the swap the eye expects.
/// <para>
/// Giving each transparent material its own instance hands the engine the same
/// thing it already gets right between elements. What it does not fix, and cannot:
/// two panes of the <b>same</b> material (one instance again), and objects that
/// interpenetrate, since an instance is sorted by one distance and has only one
/// place in the order.
/// </para>
/// <para>
/// Opaque materials deliberately stay together. They are ordered by the depth
/// buffer whatever sequence they arrive in, so splitting them would buy nothing
/// and cost a draw call per material.
/// </para>
/// </remarks>
internal readonly struct VoxelMeshSet
{
    public VoxelMeshSet(ArrayMesh? opaque, IReadOnlyList<VoxelMeshPart> transparent)
    {
        Opaque = opaque;
        Transparent = transparent;
    }

    /// <summary>Every opaque material of the element, or null when it has none.</summary>
    public ArrayMesh? Opaque { get; }

    /// <summary>One entry per transparent material, each wanting an instance of its own.</summary>
    public IReadOnlyList<VoxelMeshPart> Transparent { get; }

    /// <summary>Nothing visible at all, which is a valid thing for an element to be.</summary>
    public bool IsEmpty => Opaque is null && (Transparent is null || Transparent.Count == 0);

    /// <summary>
    /// Whether a material has to be drawn as an instance of its own, which is the
    /// whole of the split: anything the eye can see through, and nothing else.
    /// </summary>
    /// <remarks>
    /// The boundary is load-bearing in both directions. Below one a material is in
    /// the alpha pass, where the engine's only ordering is between instances. At
    /// exactly one it is depth-buffered and needs no ordering at all, so giving it
    /// an instance would buy nothing — and an ordinary model is a handful of opaque
    /// materials, so it would be a node apiece for every one of them.
    /// <para>
    /// Note that this costs no draw calls either way: a surface is a draw call
    /// wherever it lives, and this only decides which mesh holds it.
    /// </para>
    /// </remarks>
    public static bool NeedsOwnInstance(float opacity) => opacity < 1f;
}

/// <summary>One transparent material's faces, and which material they wear.</summary>
internal readonly record struct VoxelMeshPart(Guid MaterialId, ArrayMesh Mesh);
