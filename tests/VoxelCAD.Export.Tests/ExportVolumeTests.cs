using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// The volume facet only promises exactness for axis-aligned placement. These
/// tests hold it to that: an aligned element bakes to an integer brick, and a
/// rotated one is reported rather than resampled into the wrong cells.
/// </summary>
public sealed class ExportVolumeTests
{
    private readonly IExporter _exporter = ExportFactory.CreateExporter();

    [Fact]
    public void ACellBakesToOneByteAtItsWorldOrigin()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), new Vector3I(2, 3, 4));

        ExportVolume volume = Volume(project, scene);
        ExportBrick brick = Assert.Single(volume.Bricks);

        Assert.Equal(new Vector3I(2, 3, 4), brick.Origin);
        Assert.Equal(Vector3I.One, brick.Size);

        // Default is palette index 0, so its byte is 1: MagicaVoxel reserves 0 for
        // empty and addresses colours from 1.
        Assert.Equal(new byte[] { 1 }, brick.PaletteIndices);
    }

    [Fact]
    public void ElementPositionMovesTheBrick()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        // Position names the pivot, and a single cell's first-paint default puts
        // it at the cell's middle — so the on-lattice way to park the cell at
        // (10, 0, 5) is to put its middle at (10.5, 0.5, 5.5).
        scene.SetTransform(element, TransformChannel.Position, new Vector3(10.5f, 0.5f, 5.5f));

        ExportBrick brick = Assert.Single(Volume(project, scene).Bricks);

        Assert.Equal(new Vector3I(10, 0, 5), brick.Origin);
    }

    [Fact]
    public void ASnap90RotationStaysAxisAligned()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        scene.SetFieldMode(element, TransformChannel.Rotation, FieldMode.Snap90);
        scene.SetTransform(element, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));

        ExportModel model = _exporter.BuildModel(project, scene, ExportFacet.Volume, CullingPolicy.Solid);

        Assert.Empty(model.UnbakeableElements);
        Assert.Single(model.Volume!.Bricks);
    }

    [Fact]
    public void ARotatedElementIsReportedNotBaked()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Tilted");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        // A free 45-degree turn: no integer grid can hold it exactly.
        scene.SetFieldMode(element, TransformChannel.Rotation, FieldMode.Float);
        scene.SetTransform(element, TransformChannel.Rotation, new Vector3(0f, 45f, 0f));

        ExportModel model = _exporter.BuildModel(project, scene, ExportFacet.Volume, CullingPolicy.Solid);

        Assert.Equal(new[] { "Tilted" }, model.UnbakeableElements);
        Assert.Empty(model.Volume!.Bricks);
    }

    [Fact]
    public void ANonUniformScaleIsAlsoRefused()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Stretched");
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        scene.SetTransform(element, TransformChannel.Scale, new Vector3(2f, 1f, 1f));

        ExportModel model = _exporter.BuildModel(project, scene, ExportFacet.Volume, CullingPolicy.Solid);

        Assert.Equal(new[] { "Stretched" }, model.UnbakeableElements);
    }

    private ExportVolume Volume(IProject project, IScene scene) =>
        _exporter.BuildModel(project, scene, ExportFacet.Volume, CullingPolicy.Solid).Volume!;
}
