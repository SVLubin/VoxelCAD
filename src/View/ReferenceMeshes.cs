using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Somebody else's 3D model, turned into nodes this scene can hold.
/// </summary>
/// <remarks>
/// <para>
/// Three formats, and the split between them is who reads the file. glTF is the
/// engine's own, so it is handed straight to <see cref="GltfDocument"/> — materials,
/// textures and node structure included. OBJ and STL are read by parsers of ours
/// (<see cref="ObjGeometry"/>, <see cref="StlGeometry"/>), which is why those two
/// are engine-free and testable while this is not.
/// </para>
/// <para>
/// Nothing here is cached. A model is re-read only when the reference is pointed
/// at another file or switched between surfaces and lines; turning its opacity or
/// its lighting touches the nodes that are already there.
/// </para>
/// </remarks>
internal static class ReferenceMeshes
{
    /// <summary>
    /// The model as a node, or null where the file is missing, unreadable or not
    /// one this editor knows. A reference may name any of those, and none of them
    /// is a reason to take the editor down.
    /// </summary>
    public static Node3D? Load(string path, bool wireframe)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();

            Node3D? loaded = extension switch
            {
                ".gltf" or ".glb" => LoadGltf(path),
                ".obj" => LoadObj(path),
                ".stl" => LoadStl(path),
                _ => null
            };

            if (loaded != null && wireframe)
                ToWireframe(loaded);

            return loaded;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static Node3D? LoadGltf(string path)
    {
        var document = new GltfDocument();
        var state = new GltfState();

        if (document.AppendFromFile(path, state) != Error.Ok)
            return null;

        return document.GenerateScene(state) as Node3D;
    }

    private static Node3D? LoadObj(string path)
    {
        if (!ObjGeometry.TryRead(
            File.ReadAllText(path),
            out IReadOnlyList<ObjSurface> surfacesList,
            out IReadOnlyList<string> librariesList))
        {
            return null;
        }

        string? directory = Path.GetDirectoryName(path);
        Dictionary<string, ObjMaterial> materialsByNameDictionary =
            ReadLibraries(directory, librariesList);

        var mesh = new ArrayMesh();

        foreach (ObjSurface surface in surfacesList)
        {
            var arrays = new Godot.Collections.Array();

            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = surface.Positions;
            arrays[(int)Mesh.ArrayType.Normal] = surface.Normals;

            if (surface.Uvs.Length == surface.Positions.Length)
                arrays[(int)Mesh.ArrayType.TexUV] = surface.Uvs;

            arrays[(int)Mesh.ArrayType.Index] = surface.Indices;

            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            if (!materialsByNameDictionary.TryGetValue(
                surface.MaterialName,
                out ObjMaterial material))
            {
                material = ObjMaterial.Default;
            }

            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, Build(material, directory));
        }

        return Instance(mesh);
    }

    private static Node3D? LoadStl(string path)
    {
        if (!StlGeometry.TryRead(File.ReadAllBytes(path), out Vector3[] positions))
            return null;

        // No sharing and no texture coordinates: an STL triangle is three points
        // and nothing else, so the mesh is unindexed and the normals are the
        // winding's own.
        var normals = new Vector3[positions.Length];

        for (var triangle = 0; triangle + 2 < positions.Length; triangle += 3)
        {
            Vector3 face = (positions[triangle + 1] - positions[triangle])
                .Cross(positions[triangle + 2] - positions[triangle]);

            Vector3 normal = face.LengthSquared() > 0f ? face.Normalized() : Vector3.Up;

            normals[triangle] = normal;
            normals[triangle + 1] = normal;
            normals[triangle + 2] = normal;
        }

        var arrays = new Godot.Collections.Array();

        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;

        var mesh = new ArrayMesh();

        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, Build(ObjMaterial.Default, directory: null));

        return Instance(mesh);
    }

    private static Dictionary<string, ObjMaterial> ReadLibraries(
        string? directory,
        IReadOnlyList<string> librariesList)
    {
        var materialsByNameDictionary = new Dictionary<string, ObjMaterial>();

        if (directory == null)
            return materialsByNameDictionary;

        foreach (string library in librariesList)
        {
            string path = Path.Combine(directory, library);

            if (!File.Exists(path))
                continue;

            foreach (KeyValuePair<string, ObjMaterial> entry
                in ObjMaterialLibrary.Read(File.ReadAllText(path)))
            {
                materialsByNameDictionary[entry.Key] = entry.Value;
            }
        }

        return materialsByNameDictionary;
    }

    /// <summary>
    /// A material's own colour and picture, drawn per pixel. Whether the lights
    /// reach it is not decided here — the reference's own switch says that, and it
    /// is applied to whatever the loader produced, glTF's materials included.
    /// </summary>
    private static StandardMaterial3D Build(ObjMaterial material, string? directory)
    {
        var built = new StandardMaterial3D
        {
            AlbedoColor = new Color(
                material.Diffuse.R,
                material.Diffuse.G,
                material.Diffuse.B,
                material.Opacity),

            // A reference is looked at from wherever the work is, and a surface
            // wound the other way is not a defect worth a hole in the drawing.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };

        if (material.Opacity < 1f)
            built.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;

        if (!string.IsNullOrEmpty(material.DiffuseMap) && directory != null)
        {
            Texture2D? picture = ReferenceAssets.Picture(
                Path.GetFullPath(Path.Combine(directory, material.DiffuseMap)));

            if (picture != null)
                built.AlbedoTexture = picture;
        }

        return built;
    }

    private static Node3D Instance(Mesh mesh) => new MeshInstance3D
    {
        Name = "Model",
        Mesh = mesh
    };

    /// <summary>
    /// Every mesh under the node re-made as its own edges. In place, because the
    /// node was built for this reference alone and nothing else is looking at it.
    /// </summary>
    private static void ToWireframe(Node node)
    {
        if (node is MeshInstance3D instance && instance.Mesh is { } mesh)
            instance.Mesh = ToWireframe(mesh);

        foreach (Node child in node.GetChildren())
            ToWireframe(child);
    }

    private static Mesh ToWireframe(Mesh mesh)
    {
        var lines = new ArrayMesh();

        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            // Only an ArrayMesh publishes what each surface is made of; anything
            // else here came from a loader that builds triangles and nothing else.
            Mesh.PrimitiveType primitive = mesh is ArrayMesh described
                ? described.SurfaceGetPrimitiveType(surface)
                : Mesh.PrimitiveType.Triangles;

            if (primitive != Mesh.PrimitiveType.Triangles)
                continue;

            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);

            if (arrays[(int)Mesh.ArrayType.Vertex].Obj is not Vector3[] positions)
                continue;

            // An unindexed surface — an STL, or anything else that shares nothing —
            // is its own index list.
            int[] indices = arrays[(int)Mesh.ArrayType.Index].Obj is int[] held
                ? held
                : Sequence(positions.Length);

            var edges = new Godot.Collections.Array();

            edges.Resize((int)Mesh.ArrayType.Max);
            edges[(int)Mesh.ArrayType.Vertex] = positions;
            edges[(int)Mesh.ArrayType.Index] = WireframeEdges.From(indices);

            lines.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, edges);
            lines.SurfaceSetMaterial(
                lines.GetSurfaceCount() - 1,
                mesh.SurfaceGetMaterial(surface));
        }

        return lines.GetSurfaceCount() > 0 ? lines : mesh;
    }

    private static int[] Sequence(int count)
    {
        var indices = new int[count];

        for (var index = 0; index < count; index++)
            indices[index] = index;

        return indices;
    }
}
