using System;
using System.Linq;
using Godot;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// An instance's contents leave the editor as ordinary geometry: derived elements
/// are elements, and the projection's walk neither knows nor cares that a subtree
/// was expanded from a library entry. Held by a test rather than by luck.
/// </summary>
public sealed class PrefabExportTests
{
    private readonly IExporter _exporter = ExportFactory.CreateExporter();

    [Fact]
    public void AnInstancesContentsExportAsGeometry()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid model = ModelBuilder.VoxelElement(scene, "Wheel");

        ModelBuilder.Fill(scene, model, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        Guid prefabId = scene.CreatePrefab(model);
        scene.CreateInstance(scene.Id, prefabId, "Second");

        ExportModel exported =
            _exporter.BuildModel(project, scene, ExportFacet.Mesh, CullingPolicy.Visual);

        // Two instances, one cube each: both export, as the derived elements they
        // hold rather than as anything prefab-shaped. A lone cube is six quads —
        // twelve triangles, thirty-six indices.
        Assert.Equal(2, exported.Mesh!.Surfaces.Count);
        Assert.All(exported.Mesh!.Surfaces, surface => Assert.Equal(36, surface.Indices.Count));
    }

    [Fact]
    public void AHiddenInstanceTakesItsContentsOutOfTheFile()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid model = ModelBuilder.VoxelElement(scene, "Wheel");

        ModelBuilder.Fill(scene, model, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        Guid prefabId = scene.CreatePrefab(model);
        Guid second = scene.CreateInstance(scene.Id, prefabId, "Second");

        scene.SetVisible(second, false);

        ExportModel exported =
            _exporter.BuildModel(project, scene, ExportFacet.Mesh, CullingPolicy.Visual);

        Assert.Single(exported.Mesh!.Surfaces);
    }

    /// <summary>A library entry nothing instances is not in the scene, so it is not in the file.</summary>
    [Fact]
    public void AnUninstancedPrefabExportsNothing()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid model = ModelBuilder.VoxelElement(scene, "Wheel");

        ModelBuilder.Fill(scene, model, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        scene.CreatePrefab(model);
        scene.DeleteElement(scene.Children[0].Id);

        ExportModel exported =
            _exporter.BuildModel(project, scene, ExportFacet.Mesh, CullingPolicy.Visual);

        Assert.Empty(exported.Mesh!.Surfaces);
    }
}
