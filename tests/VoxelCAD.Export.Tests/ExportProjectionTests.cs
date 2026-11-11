using System;
using System.Linq;
using Godot;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// The projection is the one place that knows the model, so these tests hold it to
/// the guarantees the writers lean on: a stable palette, world-space geometry, and
/// two culling policies that differ where they must.
/// </summary>
public sealed class ExportProjectionTests
{
    private readonly IExporter _exporter = ExportFactory.CreateExporter();

    [Fact]
    public void ThePaletteFollowsProjectMaterialOrder()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid stone = project.CreateMaterial("Stone", new Color(0.5f, 0.5f, 0.5f));
        Guid steel = project.CreateMaterial("Steel", new Color(0.6f, 0.6f, 0.7f));

        ExportModel model = _exporter.BuildModel(project, scene, ExportFacet.Mesh, CullingPolicy.Visual);

        Assert.Equal(
            new[] { "Default", "Stone", "Steel" },
            model.Palette.Materials.Select(material => material.Name));
        Assert.True(model.Palette.TryGetIndex(stone, out int stoneIndex));
        Assert.Equal(1, stoneIndex);
        Assert.True(model.Palette.TryGetIndex(steel, out int steelIndex));
        Assert.Equal(2, steelIndex);
    }

    [Fact]
    public void GeometryIsBakedIntoWorldSpace()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        // Park the cell five along X: position names the pivot, which the
        // first paint centred on the cell, so the request is the cell's middle.
        scene.SetTransform(
            element, CommonTypes.TransformChannel.Position, new Vector3(5.5f, 0.5f, 0.5f));

        ExportSurface surface = Assert.Single(Mesh(project, scene, CullingPolicy.Visual).Surfaces);

        Assert.Equal(5f, surface.Positions.Min(position => position.X));
        Assert.Equal(6f, surface.Positions.Max(position => position.X));
        Assert.Equal(0f, surface.Positions.Min(position => position.Y));
        Assert.Equal(1f, surface.Positions.Max(position => position.Y));
    }

    [Fact]
    public void ASingleCubeHasSixQuads()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        ExportSurface surface = Assert.Single(Mesh(project, scene, CullingPolicy.Visual).Surfaces);

        // Six faces, two triangles each, four corners each welded per face.
        Assert.Equal(36, surface.Indices.Count);
        Assert.Equal(24, surface.Positions.Count);
        Assert.Equal(surface.Positions.Count, surface.Normals.Count);
    }

    [Fact]
    public void AnInteriorSeamBetweenEqualMaterialsIsCulled()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(
            scene, element, ModelBuilder.DefaultMaterial(project),
            Vector3I.Zero, new Vector3I(1, 0, 0));

        ExportSurface surface = Assert.Single(Mesh(project, scene, CullingPolicy.Visual).Surfaces);

        // Two cubes sharing a face: twelve faces less the two that meet = ten quads.
        Assert.Equal(10 * 6, surface.Indices.Count);
    }

    [Fact]
    public void VisualKeepsOneSideOfASeamBetweenDifferentlyTransparentMaterials()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid glass = ModelBuilder.Glass(project);
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        ModelBuilder.Fill(scene, element, glass, new Vector3I(1, 0, 0));

        ExportMesh mesh = Mesh(project, scene, CullingPolicy.Visual);

        // Opaque beside glass: the solid keeps all six faces, because its surface
        // reads through the glass — and the glass gives that one up, because two
        // quads in one plane are a double blend rather than a seam. Eleven quads.
        Assert.Equal(2, mesh.Surfaces.Count);
        Assert.Equal(11 * 6, TotalIndices(mesh));
    }

    [Fact]
    public void SolidDropsThatSameSeam()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid glass = ModelBuilder.Glass(project);
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        ModelBuilder.Fill(scene, element, glass, new Vector3I(1, 0, 0));

        ExportMesh mesh = Mesh(project, scene, CullingPolicy.Solid);

        // Solid ignores opacity: the interior seam goes, whatever the two are made
        // of, leaving five faces a side — ten quads across the two surfaces.
        Assert.Equal(10 * 6, TotalIndices(mesh));
    }

    [Fact]
    public void EachElementBecomesItsOwnSurface()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid material = ModelBuilder.DefaultMaterial(project);
        Guid a = ModelBuilder.VoxelElement(scene, "Alpha");
        Guid b = ModelBuilder.VoxelElement(scene, "Beta");
        ModelBuilder.Fill(scene, a, material, Vector3I.Zero);
        ModelBuilder.Fill(scene, b, material, new Vector3I(4, 0, 0));

        ExportMesh mesh = Mesh(project, scene, CullingPolicy.Visual);

        Assert.Equal(
            new[] { "Alpha", "Beta" },
            mesh.Surfaces.Select(surface => surface.ElementName));
        Assert.All(mesh.Surfaces, surface => Assert.Equal(0, surface.MaterialIndex));
    }

    [Fact]
    public void EmptyAndGroupElementsProduceNoGeometry()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        scene.CreateElement(scene.Id, CommonTypes.ComponentKind.None, "Group");
        ModelBuilder.VoxelElement(scene, "EmptyModel");

        Assert.Empty(Mesh(project, scene, CullingPolicy.Visual).Surfaces);
    }

    private ExportMesh Mesh(IProject project, IScene scene, CullingPolicy culling) =>
        _exporter.BuildModel(project, scene, ExportFacet.Mesh, culling).Mesh!;

    private static int TotalIndices(ExportMesh mesh) =>
        mesh.Surfaces.Sum(surface => surface.Indices.Count);

    /// <summary>
    /// The palette carries the material's <em>declared</em> opacity. It used to
    /// carry the raw colour, so an opaque material with an alpha nobody chose
    /// wrote a dissolve into the file for a surface the same export had culled
    /// faces as if it were solid — the file disagreeing with itself.
    /// </summary>
    [Fact]
    public void AnOpaqueMaterialsStrayAlphaDoesNotReachTheFile()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid solid = project.CreateMaterial("Solid", new Color(1f, 0f, 0f, 0.25f));

        ExportModel model = _exporter.BuildModel(
            project, scene, ExportFacet.Mesh, CullingPolicy.Visual);

        Assert.Equal(
            1f,
            model.Palette.Materials.Single(entry => entry.Id == solid).Rgba.A);
    }

    /// <summary>
    /// And a material that <em>is</em> declared see-through keeps its alpha, or
    /// the flag would be a way of losing transparency rather than of declaring it.
    /// </summary>
    [Fact]
    public void ADeclaredTransparentMaterialKeepsItsAlpha()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid glass = ModelBuilder.Glass(project, alpha: 0.25f);

        ExportModel model = _exporter.BuildModel(
            project, scene, ExportFacet.Mesh, CullingPolicy.Visual);

        Assert.Equal(
            0.25f,
            model.Palette.Materials.Single(entry => entry.Id == glass).Rgba.A,
            4);
    }
}
