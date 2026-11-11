using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Geometry;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Turns a model's filled cells into a mesh, Goxel style: only faces the eye can
/// reach are emitted, one surface per material, each corner carrying the shade the
/// cells crowding it earn (<see cref="VoxelAmbientOcclusion"/>).
/// </summary>
/// <remarks>
/// Which faces survive is <see cref="VoxelFaces.ShouldEmitFace"/> and is shared
/// with the exporter: against a filled neighbour only the denser side shows its
/// face, so an opaque cell still shows its surface through the glass on top of it
/// while no seam ever emits two coincident quads.
/// <para>
/// The occlusion each corner earns is then weakened by the material's own opacity
/// (<see cref="VoxelAmbientOcclusion.FadeShade"/>): darkening the albedo of a pane
/// you look through reads as dirt on the glass rather than as shade in the fold.
/// </para>
/// </remarks>
internal static class VoxelMesher
{
    /// <summary>
    /// Unit-cube corner offsets per face, indexed by <see cref="VoxelFaces"/>'s
    /// face order and pre-wound for Godot's front-face convention. Godot uses
    /// CLOCKWISE winding for front faces, unlike the OpenGL/right-hand-rule CCW
    /// most references assume — which is why this table is this mesher's own
    /// while the offsets and the culling rule are shared. Verified per face:
    /// (corners[1]-corners[0]) x (corners[2]-corners[0]) points opposite to
    /// the face's outward normal, which is the correct Godot front-face condition.
    /// </summary>
    private static readonly Vector3I[][] FaceCorners =
    {
        new[] { new Vector3I(1, 0, 0), new Vector3I(1, 0, 1), new Vector3I(1, 1, 1), new Vector3I(1, 1, 0) },
        new[] { new Vector3I(0, 0, 1), new Vector3I(0, 0, 0), new Vector3I(0, 1, 0), new Vector3I(0, 1, 1) },
        new[] { new Vector3I(0, 1, 0), new Vector3I(1, 1, 0), new Vector3I(1, 1, 1), new Vector3I(0, 1, 1) },
        new[] { new Vector3I(0, 0, 1), new Vector3I(1, 0, 1), new Vector3I(1, 0, 0), new Vector3I(0, 0, 0) },
        new[] { new Vector3I(0, 0, 1), new Vector3I(0, 1, 1), new Vector3I(1, 1, 1), new Vector3I(1, 0, 1) },
        new[] { new Vector3I(0, 0, 0), new Vector3I(1, 0, 0), new Vector3I(1, 1, 0), new Vector3I(0, 1, 0) }
    };

    /// <summary>
    /// The element's geometry, split so the alpha pass has something to sort:
    /// see <see cref="VoxelMeshSet"/> for why the transparent materials come out
    /// one mesh apiece. Empty when nothing is visible, which is a valid answer.
    /// </summary>
    public static VoxelMeshSet Build(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        IReadOnlyDictionary<Guid, Material> materialsById,
        IReadOnlyDictionary<Guid, float> opacityByMaterial,
        CellRegion? only = null,
        IReadOnlyDictionary<Guid, AtlasPlacement>? atlasByMaterial = null,
        Material? atlasMaterial = null)
    {
        Dictionary<Guid, List<VoxelFace>> facesByMaterialDictionary =
            CollectVisibleFaces(voxels, opacityByMaterial, only);

        if (facesByMaterialDictionary.Count == 0)
            return default;

        ArrayMesh? opaque = null;
        List<VoxelMeshPart>? transparentList = null;

        // Every shipped material into one surface, because they are one material:
        // the same shader, and two numbers on the vertex saying which layer of the
        // arrays to read and how many cells one tile of it spans. Three hundred
        // surfaces become one. Both numbers are per vertex rather than uniforms
        // precisely because this surface is shared: a uniform on it could only
        // ever say one thing for every shipped material in the chunk, which is
        // what tiled two of them alike until AtlasPlacement.
        Surface? atlas = null;

        foreach (KeyValuePair<Guid, List<VoxelFace>> entry in facesByMaterialDictionary)
        {
            // Every face of this surface wears one material, so how far its
            // occlusion carries is settled once here rather than per corner.
            float opacity = VoxelFaces.GetOpacity(entry.Key, opacityByMaterial);

            AtlasPlacement placement = Placement(atlasByMaterial, entry.Key);

            if (placement.HasLayer
                && atlasMaterial is not null
                && !VoxelMeshSet.NeedsOwnInstance(opacity))
            {
                atlas ??= new Surface();
                BuildInto(atlas, voxels, entry.Value, opacity, placement);

                continue;
            }

            Surface surface = BuildSurface(voxels, entry.Value, opacity);
            materialsById.TryGetValue(entry.Key, out Material? material);

            if (!VoxelMeshSet.NeedsOwnInstance(opacity))
            {
                opaque ??= new ArrayMesh();
                AppendSurface(opaque, surface, material);

                continue;
            }

            var mesh = new ArrayMesh();
            AppendSurface(mesh, surface, material);
            (transparentList ??= new List<VoxelMeshPart>()).Add(new VoxelMeshPart(entry.Key, mesh));
        }

        if (atlas is not null)
        {
            opaque ??= new ArrayMesh();
            AppendSurface(opaque, atlas, atlasMaterial);
        }

        return new VoxelMeshSet(
            opaque, (IReadOnlyList<VoxelMeshPart>?)transparentList ?? Array.Empty<VoxelMeshPart>());
    }

    private static AtlasPlacement Placement(
        IReadOnlyDictionary<Guid, AtlasPlacement>? atlasByMaterial,
        Guid material) =>
        atlasByMaterial is not null && atlasByMaterial.TryGetValue(material, out AtlasPlacement found)
            ? found
            : AtlasPlacement.None;

    private static Surface BuildSurface(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        List<VoxelFace> facesList,
        float opacity)
    {
        var surface = new Surface();

        BuildInto(surface, voxels, facesList, opacity, AtlasPlacement.None);

        return surface;
    }

    /// <summary>
    /// The same faces appended to a surface that may already hold others — which
    /// is what lets every shipped material share one, each face carrying the layer
    /// it reads.
    /// </summary>
    private static void BuildInto(
        Surface surface,
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        List<VoxelFace> facesList,
        float opacity,
        AtlasPlacement placement)
    {
        // The faces of one cell were collected together, so remembering the last
        // neighbourhood is enough to read each cell's surroundings once.
        Vector3I neighborhoodCell = default;
        uint neighborhood = 0;
        var hasNeighborhood = false;

        foreach (VoxelFace visibleFace in facesList)
        {
            if (!hasNeighborhood || neighborhoodCell != visibleFace.Cell)
            {
                neighborhoodCell = visibleFace.Cell;
                neighborhood = VoxelAmbientOcclusion.GetNeighborhood(voxels, neighborhoodCell);
                hasNeighborhood = true;
            }

            int before = surface.VerticesList.Count;

            AppendFace(surface, visibleFace.Cell, visibleFace.Face, neighborhood, opacity);

            if (!placement.HasLayer)
                continue;

            // Two floats a vertex, which is the narrowest custom channel Godot
            // offers: which layer of the shipped arrays to read, and how many
            // cells one tile of it spans. Both per material, because the atlas
            // surface is shared by every shipped material in the chunk and a
            // uniform could only say one thing for all of them.
            for (int i = before; i < surface.VerticesList.Count; i++)
            {
                surface.LayersList.Add(placement.Layer);
                surface.LayersList.Add(placement.SafePeriod);
            }
        }
    }

    private static void AppendSurface(ArrayMesh mesh, Surface surface, Material? material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = surface.VerticesList.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = surface.NormalsList.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = surface.ColorsList.ToArray();

        if (surface.LayersList.Count == 0)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
        else
        {
            // The layer each vertex reads, in the custom channel the shader
            // declares. The format rides in the flags rather than beside the
            // array, which is Godot's own arrangement.
            arrays[(int)Mesh.ArrayType.Custom0] = surface.LayersList.ToArray();

            var flags = (Mesh.ArrayFormat)((ulong)Mesh.ArrayCustomFormat.RgFloat
                << (int)Mesh.ArrayFormat.FormatCustom0Shift);

            mesh.AddSurfaceFromArrays(
                Mesh.PrimitiveType.Triangles,
                arrays,
                blendShapes: null,
                lods: null,
                flags: flags);
        }

        if (material is not null)
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }

    /// <summary>
    /// The visibility decision on its own, free of any engine type: which faces
    /// of which cells survive culling, grouped by the material that draws them.
    /// A face is hidden only when its neighbour is filled with an equally opaque
    /// material; an absent neighbour defaults to fully opaque so nothing outside
    /// the model is ever occluded by it.
    /// </summary>
    internal static Dictionary<Guid, List<VoxelFace>> CollectVisibleFaces(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        IReadOnlyDictionary<Guid, float> opacityById) =>
        CollectVisibleFaces(voxels, opacityById, null);

    /// <summary>
    /// The same answer for one box of cells rather than for the whole model. The
    /// neighbours outside the box are still read — a face is culled by what is
    /// beside it, and a cell at the edge of a chunk has neighbours in the next one
    /// — so what the box decides is which faces are <em>emitted</em>, never what
    /// is looked at.
    /// </summary>
    internal static Dictionary<Guid, List<VoxelFace>> CollectVisibleFaces(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        IReadOnlyDictionary<Guid, float> opacityById,
        CellRegion? only)
    {
        var facesByMaterialDictionary = new Dictionary<Guid, List<VoxelFace>>();

        foreach (KeyValuePair<Vector3I, Guid> voxel in Cells(voxels, only))
        {
            float opacity = VoxelFaces.GetOpacity(voxel.Value, opacityById);

            for (var face = 0; face < VoxelFaces.FaceCount; face++)
            {
                bool neighborPresent = voxels.TryGetValue(
                    voxel.Key + VoxelFaces.GetNeighborOffset(face), out Guid neighbor);

                if (!VoxelFaces.ShouldEmitFace(
                    opacity, neighborPresent, VoxelFaces.GetOpacity(neighbor, opacityById)))
                    continue;

                if (!facesByMaterialDictionary.TryGetValue(voxel.Value, out List<VoxelFace>? faces))
                {
                    faces = new List<VoxelFace>();
                    facesByMaterialDictionary[voxel.Value] = faces;
                }

                faces.Add(new VoxelFace(voxel.Key, face));
            }
        }

        return facesByMaterialDictionary;
    }


    private static float CornerShade(
        uint neighborhood, Vector3I cellNormal, Vector3I corner, float opacity) =>
        VoxelAmbientOcclusion.FadeShade(
            VoxelAmbientOcclusion.GetCornerShade(neighborhood, cellNormal, corner), opacity);

    /// <summary>
    /// Every cell of the model, or the cells of one box. Walking the box costs a
    /// probe per coordinate in it rather than a pass over the whole dictionary,
    /// which is what makes a chunk cheap to rebuild — and it is why a chunk is
    /// small: the cost is the box's volume whether or not anything is in it.
    /// </summary>
    private static IEnumerable<KeyValuePair<Vector3I, Guid>> Cells(
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        CellRegion? only)
    {
        if (only is not CellRegion box)
        {
            foreach (KeyValuePair<Vector3I, Guid> voxel in voxels)
                yield return voxel;

            yield break;
        }

        for (int z = box.Min.Z; z <= box.Max.Z; z++)
        {
            for (int y = box.Min.Y; y <= box.Max.Y; y++)
            {
                for (int x = box.Min.X; x <= box.Max.X; x++)
                {
                    var cell = new Vector3I(x, y, z);

                    if (voxels.TryGetValue(cell, out Guid material))
                        yield return new KeyValuePair<Vector3I, Guid>(cell, material);
                }
            }
        }
    }

    private static void AppendFace(
        Surface surface,
        Vector3I cell,
        int face,
        uint neighborhood,
        float opacity)
    {
        Vector3I[] corners = FaceCorners[face];
        Vector3I cellNormal = VoxelFaces.GetNeighborOffset(face);

        float shadeA = CornerShade(neighborhood, cellNormal, corners[0], opacity);
        float shadeB = CornerShade(neighborhood, cellNormal, corners[1], opacity);
        float shadeC = CornerShade(neighborhood, cellNormal, corners[2], opacity);
        float shadeD = CornerShade(neighborhood, cellNormal, corners[3], opacity);

        Vector3 cornerA = cell + corners[0];
        Vector3 cornerB = cell + corners[1];
        Vector3 cornerC = cell + corners[2];
        Vector3 cornerD = cell + corners[3];

        // The neighbour offset is the face's outward normal.
        var normal = (Vector3)cellNormal;

        // The quad as two triangles, fanned from whichever corner leaves the seam
        // joining the lighter pair. Both fans keep the corners in their cyclic
        // order, so the winding — and with it which side is the front — is the same
        // either way.
        if (VoxelAmbientOcclusion.ShouldFlipDiagonal(shadeA, shadeB, shadeC, shadeD))
        {
            AppendTriangle(surface, normal, cornerB, cornerC, cornerD, shadeB, shadeC, shadeD);
            AppendTriangle(surface, normal, cornerB, cornerD, cornerA, shadeB, shadeD, shadeA);
        }
        else
        {
            AppendTriangle(surface, normal, cornerA, cornerB, cornerC, shadeA, shadeB, shadeC);
            AppendTriangle(surface, normal, cornerA, cornerC, cornerD, shadeA, shadeC, shadeD);
        }
    }

    private static void AppendTriangle(
        Surface surface,
        Vector3 normal,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        float firstShade,
        float secondShade,
        float thirdShade)
    {
        surface.VerticesList.Add(first);
        surface.VerticesList.Add(second);
        surface.VerticesList.Add(third);

        for (var i = 0; i < 3; i++)
            surface.NormalsList.Add(normal);

        // Grey against the albedo, and alpha untouched: the shade must darken a
        // material without making it any more see-through than it asked to be.
        surface.ColorsList.Add(new Color(firstShade, firstShade, firstShade));
        surface.ColorsList.Add(new Color(secondShade, secondShade, secondShade));
        surface.ColorsList.Add(new Color(thirdShade, thirdShade, thirdShade));
    }

    /// <summary>One exposed face of a cell: the cell, and which of the six sides.</summary>
    internal readonly record struct VoxelFace(Vector3I Cell, int Face);

    private sealed class Surface
    {
        public List<Vector3> VerticesList { get; } = new();

        public List<Vector3> NormalsList { get; } = new();

        public List<Color> ColorsList { get; } = new();

        /// <summary>
        /// Two floats a vertex — the layer of the shipped arrays it reads and the
        /// cells one tile of it spans — or empty for a surface drawn with a
        /// material of its own.
        /// </summary>
        public List<float> LayersList { get; } = new();
    }
}
