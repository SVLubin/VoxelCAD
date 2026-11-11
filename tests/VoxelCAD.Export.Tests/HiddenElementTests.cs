using System;
using System.Linq;
using Godot;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// What is hidden does not leave the editor. The projection is the one walk over
/// the tree both facets share, so the rule is stated once and holds for a mesh and
/// for a volume alike.
/// </summary>
public sealed class HiddenElementTests
{
    private readonly IExporter _exporter = ExportFactory.CreateExporter();

    [Fact]
    public void AHiddenElementContributesNoSurfaces()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid shown = ModelBuilder.VoxelElement(scene, "Shown");
        Guid hidden = ModelBuilder.VoxelElement(scene, "Hidden");

        ModelBuilder.Fill(scene, shown, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        ModelBuilder.Fill(
            scene, hidden, ModelBuilder.DefaultMaterial(project), new Vector3I(4, 0, 0));

        scene.SetVisible(hidden, false);

        ExportMesh mesh = Mesh(project, scene);

        Assert.Equal(
            new[] { "Shown" }, mesh.Surfaces.Select(surface => surface.ElementName).Distinct());
    }

    [Fact]
    public void AHiddenElementBakesNoBrick()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid hidden = ModelBuilder.VoxelElement(scene, "Hidden");

        ModelBuilder.Fill(scene, hidden, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        scene.SetVisible(hidden, false);

        Assert.Empty(Volume(project, scene).Bricks);
    }

    /// <summary>
    /// Hiding a group takes its contents with it, without the model writing
    /// anything to them — visibility composes down the tree the way a transform
    /// does.
    /// </summary>
    [Fact]
    public void AHiddenGroupTakesItsSubtreeWithIt()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid group = scene.CreateElement(scene.Id, CommonTypes.ComponentKind.None, "Group");
        Guid child = scene.CreateElement(group, CommonTypes.ComponentKind.VoxelModel, "Child");

        ModelBuilder.Fill(scene, child, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        scene.SetVisible(group, false);

        Assert.Empty(Mesh(project, scene).Surfaces);
        Assert.True(scene.TryGetElement(child, out IHierarchyElement element) && element.Visible);
    }

    /// <summary>
    /// Showing it again is the whole of what undoing the hide has to restore: the
    /// cells were never touched.
    /// </summary>
    [Fact]
    public void ShowingItAgainBringsTheGeometryBack()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Model");

        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        scene.SetVisible(element, false);
        scene.SetVisible(element, true);

        Assert.Single(Mesh(project, scene).Surfaces);
    }

    private ExportMesh Mesh(IProject project, IScene scene) =>
        _exporter.BuildModel(project, scene, ExportFacet.Mesh, CullingPolicy.Visual).Mesh!;

    private ExportVolume Volume(IProject project, IScene scene) =>
        _exporter.BuildModel(project, scene, ExportFacet.Volume, CullingPolicy.Solid).Volume!;
}
