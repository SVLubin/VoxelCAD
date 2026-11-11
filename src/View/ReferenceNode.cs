using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What a reference is on screen: a plane wearing a picture, or somebody else's
/// model, standing under the element's own node so it moves with the transform
/// like anything else in the tree.
/// </summary>
/// <remarks>
/// It holds no cells and never will, which is what keeps it out of the mesher, the
/// exporter, the render's own compile and every ray a cell tool casts — a brush
/// paints straight through a reference, which is what a reference is for.
/// </remarks>
internal sealed partial class ReferenceNode : Node3D
{
    private Node3D? _content;

    /// <summary>
    /// What is loaded, so that turning a dial does not re-read a file. Only the
    /// path and the wireframe switch change what the geometry <em>is</em>;
    /// everything else is written onto the nodes already standing here.
    /// </summary>
    private (string Path, bool Wireframe)? _loaded;

    public ReferenceNode() => Name = "Reference";

    /// <summary>
    /// Rebuilds the plane. Everything is re-derived rather than patched: four
    /// vertices and one material cost nothing, and half-updated geometry is how a
    /// size and a picture come to disagree.
    /// </summary>
    public void Apply(ReferenceImageSettings settings)
    {
        ReferenceLook look = ReferenceLook.From(settings);

        Detach();

        var surface = new MeshInstance3D
        {
            Name = "Surface",
            Mesh = BuildPlane(settings.SizeInCells),
            MaterialOverride = ReferenceMaterialFactory.CreatePicture(
                look,
                ReferenceAssets.Picture(settings.Path))
        };

        _content = surface;
        _loaded = (settings.Path, false);

        AddChild(surface);
        ApplyLook(surface, look);
    }

    /// <summary>
    /// Points at a model, and re-reads the file only where it has to. A fifty-
    /// megabyte glTF re-imported because somebody dragged an opacity slider would
    /// be a dial nobody could use.
    /// </summary>
    public void Apply(ReferenceModelSettings settings)
    {
        ReferenceLook look = ReferenceLook.From(settings);

        if (_loaded != (settings.Path, settings.Wireframe))
        {
            Detach();

            _content = ReferenceMeshes.Load(settings.Path, settings.Wireframe);
            _loaded = (settings.Path, settings.Wireframe);

            if (_content != null)
                AddChild(_content);
        }

        if (_content != null)
            ApplyLook(_content, look);
    }

    /// <summary>
    /// Walks whatever the loader produced and writes the look onto it. A glTF
    /// arrives as a tree of instances, an OBJ as one — and the shading is set on
    /// each material rather than replaced by an override, which is what lets a
    /// model be unlit and keep its own textures.
    /// </summary>
    private static void ApplyLook(Node node, ReferenceLook look)
    {
        if (node is GeometryInstance3D instance)
        {
            // Per instance, so a model's own materials are never touched to fade
            // it — and so a fade costs no rebuild at all.
            instance.Transparency = look.Transparency;
            instance.Visible = look.Drawn;
            instance.CastShadow = look.CastsShadow
                ? GeometryInstance3D.ShadowCastingSetting.On
                : GeometryInstance3D.ShadowCastingSetting.Off;
            instance.Layers = RenderLayers.Voxels;
        }

        if (node is MeshInstance3D drawn)
            ApplyShading(drawn, look);

        foreach (Node child in node.GetChildren())
            ApplyLook(child, look);
    }

    private static void ApplyShading(MeshInstance3D instance, ReferenceLook look)
    {
        BaseMaterial3D.ShadingModeEnum shading = look.Unshaded
            ? BaseMaterial3D.ShadingModeEnum.Unshaded
            : BaseMaterial3D.ShadingModeEnum.PerPixel;

        if (instance.MaterialOverride is BaseMaterial3D overridden)
            overridden.ShadingMode = shading;

        if (instance.Mesh is not { } mesh)
            return;

        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            // A shader material has no shading mode to set, and a reference wearing
            // one is not something this editor produced — leave it as its author
            // wrote it rather than replacing what it draws.
            if (mesh.SurfaceGetMaterial(surface) is BaseMaterial3D material)
                material.ShadingMode = shading;
        }
    }

    private static ArrayMesh BuildPlane(Vector2 sizeInCells)
    {
        Vector3[] corners = ReferencePlaneGeometry.Corners(sizeInCells);
        var normals = new Vector3[corners.Length];

        for (var index = 0; index < normals.Length; index++)
            normals[index] = ReferencePlaneGeometry.Normal;

        var arrays = new Godot.Collections.Array();

        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = corners;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = ReferencePlaneGeometry.Uvs();
        arrays[(int)Mesh.ArrayType.Index] = ReferencePlaneGeometry.Indices();

        var mesh = new ArrayMesh();

        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return mesh;
    }

    private void Detach()
    {
        if (_content == null)
            return;

        RemoveChild(_content);
        _content.QueueFree();
        _content = null;
        _loaded = null;
    }
}
