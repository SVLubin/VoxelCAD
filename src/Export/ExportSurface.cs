using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// One contiguous run of welded, indexed triangles: the geometry of a single
/// element drawn in a single material. Positions and normals are world-space, in
/// the IR's canonical frame (right-handed, Y-up, CCW front faces); each writer
/// applies its own axis conversion and scale.
/// </summary>
/// <remarks>
/// A surface carries its element's name as well as its material index, so the
/// grouping the mesh formats need (OBJ's <c>o</c>/<c>g</c>, FBX's per-element
/// <c>Model</c> nodes) is read straight off the surface list rather than from a
/// separate range map — one element, one material, one surface. Welding is within
/// the surface: two coplanar faces of the same element and material share a vertex,
/// while an edge between differing materials keeps its seam.
/// </remarks>
public sealed class ExportSurface
{
    public ExportSurface(
        string elementName,
        int materialIndex,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<int> indices)
    {
        ElementName = elementName;
        MaterialIndex = materialIndex;
        Positions = positions;
        Normals = normals;
        Indices = indices;
    }

    public string ElementName { get; }

    /// <summary>Index into <see cref="ExportPalette.Materials"/>.</summary>
    public int MaterialIndex { get; }

    public IReadOnlyList<Vector3> Positions { get; }

    /// <summary>One per position, so a vertex carries the normal of the face it belongs to.</summary>
    public IReadOnlyList<Vector3> Normals { get; }

    /// <summary>Triangle list, three indices per triangle, into <see cref="Positions"/>.</summary>
    public IReadOnlyList<int> Indices { get; }
}
