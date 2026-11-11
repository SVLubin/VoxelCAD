using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Geometry;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// Bakes one voxel element into world-space welded triangles. Unlike the
/// viewport mesher, which meshes each element locally and lets the node tree
/// compose transforms for display, this bakes the world transform into the
/// geometry so a single file holds the whole scene.
/// </summary>
/// <remarks>
/// The IR's canonical frame is right-handed, Y-up, with CCW front faces (the
/// neutral/OpenGL convention) — the opposite winding to Godot's own front faces,
/// which is why the corner table here is not the viewport mesher's. A writer that
/// needs another convention converts on its way out.
/// </remarks>
internal static class ExportMesher
{
    /// <summary>
    /// Unit-cube corner offsets per face, indexed by <see cref="VoxelFaces"/>'s
    /// face order and wound CCW as seen from outside, so the right-hand rule
    /// gives the outward normal. Verified per face:
    /// (corners[1]-corners[0]) x (corners[2]-corners[0]) equals the face's
    /// outward normal.
    /// </summary>
    private static readonly Vector3[][] FaceCorners =
    {
        new[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) },
        new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) },
        new[] { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) },
        new[] { new Vector3(0, 0, 1), new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1) },
        new[] { new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1) },
        new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }
    };

    /// <summary>
    /// One surface per material the element uses, in ascending palette order so the
    /// output is deterministic. Cells are visited in a fixed order for the same
    /// reason — a welded vertex list must not depend on dictionary iteration.
    /// </summary>
    public static IReadOnlyList<ExportSurface> BuildElementSurfaces(
        string elementName,
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        Transform3D world,
        CullingPolicy policy,
        IReadOnlyDictionary<Guid, float> opacityById,
        ExportPalette palette)
    {
        // A mirrored transform (negative-determinant basis) reverses face winding;
        // flipping the triangles back keeps front faces outward in world space.
        bool flipWinding = world.Basis.Determinant() < 0f;
        Basis normalBasis = world.Basis.Inverse().Transposed();

        var buildersByMaterialDictionary = new Dictionary<Guid, SurfaceBuilder>();

        foreach (Vector3I cell in OrderedCells(voxels.Keys))
        {
            Guid material = voxels[cell];
            float opacity = VoxelFaces.GetOpacity(material, opacityById);

            for (var face = 0; face < VoxelFaces.FaceCount; face++)
            {
                bool neighborPresent = voxels.TryGetValue(
                    cell + VoxelFaces.GetNeighborOffset(face), out Guid neighbor);

                if (!ShouldEmitFace(
                    policy, opacity, neighborPresent, VoxelFaces.GetOpacity(neighbor, opacityById)))
                    continue;

                if (!buildersByMaterialDictionary.TryGetValue(material, out SurfaceBuilder? builder))
                {
                    builder = new SurfaceBuilder();
                    buildersByMaterialDictionary[material] = builder;
                }

                AppendFace(builder, cell, face, world, normalBasis, flipWinding);
            }
        }

        var surfacesList = new List<(int MaterialIndex, ExportSurface Surface)>(buildersByMaterialDictionary.Count);

        foreach (KeyValuePair<Guid, SurfaceBuilder> entry in buildersByMaterialDictionary)
        {
            // A cell can only carry a project material, so this always resolves;
            // guarding keeps a stray id from crashing an export.
            if (!palette.TryGetIndex(entry.Key, out int materialIndex))
                continue;

            surfacesList.Add((materialIndex, entry.Value.ToSurface(elementName, materialIndex)));
        }

        return surfacesList
            .OrderBy(entry => entry.MaterialIndex)
            .Select(entry => entry.Surface)
            .ToList();
    }

    private static bool ShouldEmitFace(
        CullingPolicy policy,
        float opacity,
        bool neighborPresent,
        float neighborOpacity)
    {
        if (!neighborPresent)
            return true;

        // Solid ignores opacity entirely: an interior seam between two filled cells
        // never shows, whatever they are made of. Visual is the viewport's own rule,
        // shared so a file and the screen cannot disagree about which faces exist.
        return policy != CullingPolicy.Solid
            && VoxelFaces.ShouldEmitFace(opacity, neighborPresent, neighborOpacity);
    }

    private static void AppendFace(
        SurfaceBuilder builder,
        Vector3I cell,
        int face,
        Transform3D world,
        Basis normalBasis,
        bool flipWinding)
    {
        Vector3 origin = new(cell.X, cell.Y, cell.Z);
        Vector3[] corners = FaceCorners[face];

        // The neighbour offset is the face's outward normal, carried through the
        // inverse-transpose so a scaled or mirrored world keeps it perpendicular.
        Vector3 normal = (normalBasis * (Vector3)VoxelFaces.GetNeighborOffset(face)).Normalized();

        int a = builder.Weld(world * (origin + corners[0]), normal);
        int b = builder.Weld(world * (origin + corners[1]), normal);
        int c = builder.Weld(world * (origin + corners[2]), normal);
        int d = builder.Weld(world * (origin + corners[3]), normal);

        // The quad as two CCW triangles: ABC and ACD, reversed under a mirror.
        if (flipWinding)
        {
            builder.AddTriangle(a, c, b);
            builder.AddTriangle(a, d, c);
        }
        else
        {
            builder.AddTriangle(a, b, c);
            builder.AddTriangle(a, c, d);
        }
    }

    private static IEnumerable<Vector3I> OrderedCells(IEnumerable<Vector3I> cells) =>
        cells
            .OrderBy(cell => cell.X)
            .ThenBy(cell => cell.Y)
            .ThenBy(cell => cell.Z);

    private sealed class SurfaceBuilder
    {
        private readonly List<Vector3> _positionsList = new();
        private readonly List<Vector3> _normalsList = new();
        private readonly List<int> _indicesList = new();
        private readonly Dictionary<(Vector3 Position, Vector3 Normal), int> _indexByVertexDictionary = new();

        public int Weld(Vector3 position, Vector3 normal)
        {
            var key = (position, normal);

            if (_indexByVertexDictionary.TryGetValue(key, out int index))
                return index;

            index = _positionsList.Count;
            _positionsList.Add(position);
            _normalsList.Add(normal);
            _indexByVertexDictionary[key] = index;

            return index;
        }

        public void AddTriangle(int a, int b, int c)
        {
            _indicesList.Add(a);
            _indicesList.Add(b);
            _indicesList.Add(c);
        }

        public ExportSurface ToSurface(string elementName, int materialIndex) =>
            new(elementName, materialIndex, _positionsList, _normalsList, _indicesList);
    }
}
