using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// The samples the editor ships with: they open as new, clean projects, and the one
/// a first launch opens shows the editor off rather than merely containing cells.
/// </summary>
public sealed class SampleTests
{
    /// <summary>A sample wears the shipped materials, so it needs them in place.</summary>
    public SampleTests() => StandardMaterials.Install();

    [Fact]
    public void TheCastleShips()
    {
        Assert.Contains("castle", ModelFactory.SampleNames);
    }

    [Fact]
    public void ASampleOpensWithNothingToUndo()
    {
        IProject project = ModelFactory.OpenSample("castle");

        Assert.Empty(project.History.Records);
        Assert.Equal("Castle", Assert.Single(project.Scenes).Name);
    }

    [Fact]
    public void ANameIsMatchedWhateverItsCase()
    {
        Assert.Equal("Castle", ModelFactory.OpenSample("Castle").Scenes[0].Name);
    }

    [Fact]
    public void AnUnknownSampleIsRefusedNamingTheOnesThereAre()
    {
        var exception = Assert.Throws<KeyNotFoundException>(() => ModelFactory.OpenSample("fortress"));

        Assert.Contains("castle", exception.Message);
    }

    /// <summary>
    /// What the castle is for: four towers that are instances of one prefab, so
    /// editing one rebuilds all four, and a camera, so a render works at once.
    /// </summary>
    [Fact]
    public void TheCastleShowsPrefabsAndACamera()
    {
        IProject project = ModelFactory.OpenSample("castle");
        IScene scene = project.Scenes[0];
        IPrefab tower = Assert.Single(project.Prefabs);
        List<IHierarchyElement> elements = Walk(scene.Children).ToList();

        int instances = elements.Count(element =>
            element.Component is IPrefabInstance instance && instance.SourceId == tower.Id);

        Assert.Equal(4, instances);
        Assert.Contains(elements, element => element.Id == scene.ActiveCameraId
            && element.ComponentKind == CommonTypes.ComponentKind.Camera);
    }

    /// <summary>
    /// Every cell, in every scene and every prefab of every sample, wears a material
    /// the project can resolve — so a shipped name that went away, or a project
    /// material the file forgot, fails here rather than as a blank cell on screen.
    /// </summary>
    [Fact]
    public void EveryCellOfEverySampleWearsAMaterialThatResolves()
    {
        foreach (string name in ModelFactory.SampleNames)
        {
            IProject project = ModelFactory.OpenSample(name);
            var known = project.AllMaterials.Select(material => material.Id).ToHashSet();
            IEnumerable<IScene> scenes = project.Scenes.Concat(project.Prefabs.Select(p => p.Content));
            var cells = 0;

            foreach (IScene scene in scenes)
            {
                foreach (IHierarchyElement element in Walk(scene.Children))
                {
                    if (element.Component is not IVoxelModel model)
                        continue;

                    foreach (Guid material in model.Voxels.Values)
                    {
                        Assert.Contains(material, known);
                        cells++;
                    }
                }
            }

            Assert.True(cells > 0, $"The sample '{name}' holds no cells.");
        }
    }

    private static IEnumerable<IHierarchyElement> Walk(IReadOnlyList<IHierarchyElement> elements)
    {
        foreach (IHierarchyElement element in elements)
        {
            yield return element;

            foreach (IHierarchyElement child in Walk(element.Children))
                yield return child;
        }
    }
}
