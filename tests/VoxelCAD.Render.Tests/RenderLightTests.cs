using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Render;
using Xunit;

namespace VoxelCAD.Render.Tests;

/// <summary>
/// Which cells the tracer aims at. Aiming at one it should not is light counted
/// twice; missing one it should is a lamp that lights nothing, and both read as
/// the tracer being wrong somewhere far away from here.
/// </summary>
public sealed class RenderLightTests
{
    [Fact]
    public void AnEmissiveCellIsSomethingToAimAt()
    {
        (IProject project, IScene scene) = NewProject();
        Guid lamp = Emissive(project, "Lamp");
        Guid element = VoxelElement(scene, "Bulb");

        Fill(scene, element, lamp, new Vector3I(3, 4, 5));

        RenderScene compiled = RenderFactory.CompileScene(project, scene);

        Assert.Single(compiled.Lights);
        Assert.Equal(new Vector3I(3, 4, 5), compiled.Lights[0].Cell);
        Assert.False(compiled.LightsTruncated);
    }

    [Fact]
    public void AMaterialThatDoesNotGlowIsNotALight()
    {
        (IProject project, IScene scene) = NewProject();
        Guid element = VoxelElement(scene, "Block");

        Fill(scene, element, project.Materials[0].Id, Vector3I.Zero);

        Assert.Empty(RenderFactory.CompileScene(project, scene).Lights);
    }

    /// <summary>
    /// The middle of the cell, carried into the world — so a moved element's lamp
    /// is aimed at where it now stands rather than where it was built.
    /// </summary>
    [Fact]
    public void ALightStandsWhereItsCellDoes()
    {
        (IProject project, IScene scene) = NewProject();
        Guid lamp = Emissive(project, "Lamp");
        Guid element = VoxelElement(scene, "Bulb");

        Fill(scene, element, lamp, new Vector3I(2, 0, 0));
        scene.SetTransform(element, TransformChannel.Position, new Vector3(10f, 0f, 0f));

        RenderScene compiled = RenderFactory.CompileScene(project, scene);
        scene.TryGetElement(element, out IHierarchyElement placed);

        Vector3 expected = placed.WorldTransform * new Vector3(2.5f, 0.5f, 0.5f);

        Assert.True(
            compiled.Lights[0].Centre.IsEqualApprox(expected),
            $"{compiled.Lights[0].Centre} is not {expected}");
    }

    /// <summary>
    /// The sphere has to cover the whole cell, corners included: a cone that
    /// missed part of the cube would lose exactly the light from its corners, and
    /// the estimator would be quietly dark rather than wrong in a way anyone could
    /// point at.
    /// </summary>
    [Fact]
    public void TheSphereCoversTheWholeCell()
    {
        (IProject project, IScene scene) = NewProject();
        Guid lamp = Emissive(project, "Lamp");
        Guid element = VoxelElement(scene, "Bulb");

        Fill(scene, element, lamp, Vector3I.Zero);

        RenderLight light = RenderFactory.CompileScene(project, scene).Lights[0];

        // Half the diagonal of a unit cube.
        Assert.True(light.Radius >= 0.8660f, light.Radius.ToString());
    }

    /// <summary>
    /// A cell walled in by its own neighbours lights nothing, and aiming at it
    /// would spend samples on a direction that is always occluded.
    /// </summary>
    [Fact]
    public void ACellWithNoFaceOnTheOutsideIsNotAimedAt()
    {
        (IProject project, IScene scene) = NewProject();
        Guid lamp = Emissive(project, "Lamp");
        Guid element = VoxelElement(scene, "Block");

        // A three-cubed block: twenty-six cells have a face out, and the one in
        // the middle has none.
        var cells = new List<Vector3I>();

        for (var z = 0; z < 3; z++)
        {
            for (var y = 0; y < 3; y++)
            {
                for (var x = 0; x < 3; x++)
                    cells.Add(new Vector3I(x, y, z));
            }
        }

        Fill(scene, element, lamp, cells.ToArray());

        RenderScene compiled = RenderFactory.CompileScene(project, scene);

        Assert.Equal(26, compiled.Lights.Count);
        Assert.DoesNotContain(compiled.Lights, light => light.Cell == new Vector3I(1, 1, 1));
    }

    [Fact]
    public void PastTheCapTheRestAreLeftToBeFoundRatherThanAimedAt()
    {
        (IProject project, IScene scene) = NewProject(new Vector3I(64, 64, 64));
        Guid lamp = Emissive(project, "Lamp");
        Guid element = VoxelElement(scene, "Wall");

        // A slab with every cell facing out, so every one of them qualifies.
        var cells = new List<Vector3I>();

        for (var z = 0; z < 40; z++)
        {
            for (var x = 0; x < 40; x++)
                cells.Add(new Vector3I(x, 0, z));
        }

        Fill(scene, element, lamp, cells.ToArray());

        RenderScene compiled = RenderFactory.CompileScene(project, scene);

        Assert.Equal(RenderSceneBuilder.MaximumLights, compiled.Lights.Count);
        Assert.True(compiled.LightsTruncated);
    }

    [Fact]
    public void EveryLightNamesTheElementItBelongsTo()
    {
        (IProject project, IScene scene) = NewProject();
        Guid lamp = Emissive(project, "Lamp");
        Guid first = VoxelElement(scene, "One");
        Guid second = VoxelElement(scene, "Two");

        Fill(scene, first, lamp, Vector3I.Zero);
        Fill(scene, second, lamp, new Vector3I(5, 0, 0));

        RenderScene compiled = RenderFactory.CompileScene(project, scene);

        Assert.Equal(new[] { 0, 1 }, compiled.Lights.Select(light => light.Element));
    }

    private static (IProject Project, IScene Scene) NewProject(Vector3I? size = null)
    {
        IProject project = ModelFactory.CreateProject(size ?? new Vector3I(32, 32, 32));

        return (project, project.Scenes[0]);
    }

    private static Guid Emissive(IProject project, string name)
    {
        Guid id = project.CreateMaterial(name, new Color(1f, 0.86f, 0.55f));

        project.SetMaterialAppearance(id, MaterialAppearance.Default with { Emission = 0.75f });

        return id;
    }

    private static Guid VoxelElement(IScene scene, string name) =>
        scene.CreateElement(scene.Id, ComponentKind.VoxelModel, name);

    private static void Fill(IScene scene, Guid element, Guid material, params Vector3I[] cells)
    {
        IReadOnlyList<VoxelEdit> edits = cells
            .Select(cell => new VoxelEdit(cell, material))
            .ToList();

        scene.SetVoxels(element, edits);
    }
}
