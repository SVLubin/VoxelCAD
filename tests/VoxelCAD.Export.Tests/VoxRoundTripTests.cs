using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// A model written to <c>.vox</c> and read back. This is what holds the axis
/// convention: the writer turns Y-up cells into MagicaVoxel's Z-up ones and the
/// reader has to be the exact inverse, which no amount of reading either half can
/// establish on its own.
/// </summary>
public sealed class VoxRoundTripTests
{
    [Fact]
    public void ARoundTripKeepsEveryCellWhereItWas()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid red = project.CreateMaterial("Red", new Color(1f, 0f, 0f));
        Guid blue = project.CreateMaterial("Blue", new Color(0f, 0f, 1f));
        Guid element = ModelBuilder.VoxelElement(scene, "Model");

        // Deliberately lopsided on every axis, so a mirrored or transposed reading
        // could not come back looking the same.
        ModelBuilder.Fill(scene, element, red, Vector3I.Zero, new Vector3I(3, 0, 0), new Vector3I(0, 5, 0));
        ModelBuilder.Fill(scene, element, blue, new Vector3I(0, 0, 7), new Vector3I(3, 5, 7));

        Dictionary<Vector3I, Color> before = SceneCells.Collect(project, scene);
        Dictionary<Vector3I, Color> after = RoundTrip(project, scene);

        Assert.Equal(before.Count, after.Count);

        foreach ((Vector3I cell, Color color) in before)
        {
            Assert.True(after.ContainsKey(cell), $"cell {cell} did not come back");
            AssertSameColor(color, after[cell], $"cell {cell}");
        }
    }

    [Fact]
    public void ARoundTripKeepsAMaterialSeeThrough()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid glass = ModelBuilder.Glass(project, alpha: 0.4f);
        Guid element = ModelBuilder.VoxelElement(scene, "Pane");
        ModelBuilder.Fill(scene, element, glass, Vector3I.Zero);

        (IProject imported, IScene importedScene) = Import(Export(project, scene));

        IMaterial material = Assert.Single(
            imported.Materials, candidate => candidate.Appearance.Transparent);

        Assert.Equal(0.4f, material.Color.A, precision: 2);
        Assert.NotEmpty(SceneCells.Collect(imported, importedScene));
    }

    /// <summary>
    /// The palette is a list and the colour index is one-based over it, so a model
    /// wearing the very first material has to come back wearing that colour and
    /// not the one beside it. This is the off-by-one, stated as a round trip.
    /// </summary>
    [Fact]
    public void TheFirstMaterialComesBackAsItself()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid element = ModelBuilder.VoxelElement(scene, "Model");

        // The project's first material, which the writer puts at colour index 1.
        ModelBuilder.Fill(scene, element, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);

        Color before = project.Materials[0].Color;
        (IProject imported, IScene importedScene) = Import(Export(project, scene));
        Color after = Assert.Single(SceneCells.Collect(imported, importedScene)).Value;

        AssertSameColor(before, after, "the first material");
    }

    [Fact]
    public void AnImportCreatesOnlyTheMaterialsTheCellsWear()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        project.CreateMaterial("Unworn", new Color(0f, 1f, 0f));
        Guid worn = project.CreateMaterial("Worn", new Color(1f, 0f, 1f));
        Guid element = ModelBuilder.VoxelElement(scene, "Model");
        ModelBuilder.Fill(scene, element, worn, Vector3I.Zero);

        (IProject imported, IScene _) = Import(Export(project, scene));

        // The fresh project's own default material, plus the one the cell wears.
        // The green one nothing wore did not travel: a palette is not a scene.
        Assert.Equal(2, imported.Materials.Count);
        Assert.Contains(imported.Materials, material => Same(material.Color, new Color(1f, 0f, 1f)));
        Assert.DoesNotContain(imported.Materials, material => Same(material.Color, new Color(0f, 1f, 0f)));
    }

    /// <summary>
    /// Colours are compared to the byte, which is the whole of what a
    /// <c>.vox</c> palette can hold — four bytes an entry. A material at 0.7 comes
    /// back at 179/255, and asserting anything tighter would be asserting against
    /// the format rather than against the reader.
    /// </summary>
    private static void AssertSameColor(Color expected, Color actual, string what) =>
        Assert.True(Same(expected, actual), $"{what}: expected {expected} but got {actual}");

    private static bool Same(Color expected, Color actual) =>
        Byte(expected.R) == Byte(actual.R)
        && Byte(expected.G) == Byte(actual.G)
        && Byte(expected.B) == Byte(actual.B)
        && Byte(expected.A) == Byte(actual.A);

    /// <summary>
    /// Rounded away from zero, which is what the writer does. Godot's own
    /// <c>RoundToInt</c> is banker's — it sends 178.5 to 178 — so a channel of 0.7
    /// would come out one short of the byte the file actually carries, and the
    /// test would be measuring its own arithmetic rather than the round trip.
    /// </summary>
    private static int Byte(float channel) =>
        Math.Clamp((int)MathF.Round(channel * 255f, MidpointRounding.AwayFromZero), 0, 255);

    private static Dictionary<Vector3I, Color> RoundTrip(IProject project, IScene scene)
    {
        (IProject imported, IScene importedScene) = Import(Export(project, scene));

        return SceneCells.Collect(imported, importedScene);
    }

    private static byte[] Export(IProject project, IScene scene)
    {
        ExportModel model = ExportFactory.CreateExporter()
            .BuildModel(project, scene, ExportFacet.Volume, CullingPolicy.Solid);

        var sink = new BufferingExportSink("part");
        new VoxFormat().Write(model, sink, new ExportOptions());

        return sink.Collect().Single().Bytes;
    }

    private static (IProject Project, IScene Scene) Import(byte[] bytes)
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        var plan = new ImportPlan(new VoxImportFormat().Read(bytes, "part"), "part", ".vox");

        ImportProjection.Apply(project, scene, plan, scene.Id);

        return (project, scene);
    }
}
