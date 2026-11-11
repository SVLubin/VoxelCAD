using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Render;

/// <summary>
/// A document compiled into what a path tracer can march: voxel grids in their
/// elements' own frames, the materials they wear, and the world box they occupy.
/// </summary>
/// <remarks>
/// Opaque outside this layer, the way a clip is opaque outside the model: a
/// caller carries one from the compile to the tracer and never looks inside. What
/// it does say out loud is what it had to leave out, because a render that
/// quietly disagreed with the viewport would be worse than one that admits it.
/// <para>
/// It refers to nothing in the document and outlives no edit — a scene is
/// compiled for a render and thrown away, which is what keeps the tracer from
/// having to know that documents change.
/// </para>
/// </remarks>
public sealed class RenderScene
{
    internal RenderScene(
        IReadOnlyList<RenderElement> elements,
        IReadOnlyList<RenderMaterial> materials,
        uint[] voxels,
        Vector3 boundsMin,
        Vector3 boundsMax,
        IReadOnlyList<string> oversizedElements,
        IReadOnlyList<RenderFacture> factures,
        IReadOnlyList<RenderLight> lights,
        bool lightsTruncated)
    {
        Elements = elements;
        Materials = materials;
        Voxels = voxels;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        OversizedElements = oversizedElements;
        Factures = factures;
        Lights = lights;
        LightsTruncated = lightsTruncated;
    }

    /// <summary>Elements whose grid was refused as too large to hold.</summary>
    public IReadOnlyList<string> OversizedElements { get; }

    /// <summary>
    /// The factures the materials here wear, each once. What a caller has to
    /// supply a height field for before a tracer can be started — the fields come
    /// from the view, which draws the same patterns for the viewport, and this is
    /// the list of the ones actually needed rather than all of them.
    /// </summary>
    public IReadOnlyList<RenderFacture> Factures { get; }

    /// <summary>
    /// Whether there were more emissive cells than the tracer will aim at, so the
    /// ones past the cap light the scene only by being stumbled onto.
    /// </summary>
    public bool LightsTruncated { get; }

    /// <summary>
    /// The emissive cells the tracer aims at — only those with a face on the
    /// outside, because a cell walled in by its own neighbours lights nothing.
    /// </summary>
    internal IReadOnlyList<RenderLight> Lights { get; }

    public bool IsEmpty => Elements.Count == 0;

    public int ElementCount => Elements.Count;

    /// <summary>How many cells the compiled grids hold, empty ones included.</summary>
    public int CellCount => Voxels.Length;

    internal IReadOnlyList<RenderElement> Elements { get; }

    internal IReadOnlyList<RenderMaterial> Materials { get; }

    /// <summary>
    /// Every element's dense grid, one after another. Zero is an empty cell, and
    /// any other value is the index into <see cref="Materials"/> plus one — the
    /// same rule the exported volume uses, for the same reason.
    /// </summary>
    internal uint[] Voxels { get; }

    /// <summary>The middle of the world box, which is what a render is framed on.</summary>
    internal Vector3 Centre => (BoundsMin + BoundsMax) * 0.5f;

    internal Vector3 BoundsMin { get; }

    internal Vector3 BoundsMax { get; }
}
