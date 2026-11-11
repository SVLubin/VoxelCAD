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
/// The compile is the whole of what the tracer knows about a document, so these
/// hold it to the things a picture would otherwise be quietly wrong about: what is
/// left out, where a cell lands in the grid, and where the grid lands in the world.
/// </summary>
public sealed class RenderSceneBuilderTests
{
    [Fact]
    public void AVisibleModelBecomesOneElement()
    {
        (IProject project, IScene scene) = NewProject();
        Guid element = VoxelElement(scene, "Block");

        Fill(scene, element, Default(project), new Vector3I(1, 2, 3));

        RenderScene compiled = RenderFactory.CompileScene(project, scene);

        Assert.Equal(1, compiled.ElementCount);
        Assert.False(compiled.IsEmpty);
    }

    [Fact]
    public void AnEmptyModelIsNotAnElementAtAll()
    {
        (IProject project, IScene scene) = NewProject();

        VoxelElement(scene, "Nothing");

        Assert.True(RenderFactory.CompileScene(project, scene).IsEmpty);
    }

    [Fact]
    public void AHiddenModelIsLeftOut()
    {
        (IProject project, IScene scene) = NewProject();
        Guid element = VoxelElement(scene, "Block");

        Fill(scene, element, Default(project), Vector3I.Zero);
        scene.SetVisible(element, false);

        Assert.True(RenderFactory.CompileScene(project, scene).IsEmpty);
    }

    /// <summary>
    /// Visibility composes down the tree the way a transform does, so a model
    /// under a hidden group is out even though its own flag says otherwise.
    /// </summary>
    [Fact]
    public void AModelUnderAHiddenGroupIsLeftOutToo()
    {
        (IProject project, IScene scene) = NewProject();
        Guid group = scene.CreateElement(scene.Id, ComponentKind.None, "Group");
        Guid element = scene.CreateElement(group, ComponentKind.VoxelModel, "Block");

        Fill(scene, element, Default(project), Vector3I.Zero);
        scene.SetVisible(group, false);

        Assert.True(RenderFactory.CompileScene(project, scene).IsEmpty);
    }

    [Fact]
    public void ACellCarriesItsOwnMaterialsIndexPlusOne()
    {
        (IProject project, IScene scene) = NewProject();
        Guid second = project.CreateMaterial("Steel", new Color(0.6f, 0.6f, 0.7f));
        Guid element = VoxelElement(scene, "Block");

        Fill(scene, element, second, Vector3I.Zero);

        RenderScene compiled = RenderFactory.CompileScene(project, scene);

        // Default is index 0, Steel is index 1, and a cell holds the index plus one.
        Assert.Equal(2u, compiled.Voxels[0]);
    }

    [Fact]
    public void ACellLandsWhereTheGridSaysItDoes()
    {
        (IProject project, IScene scene) = NewProject();
        Guid element = VoxelElement(scene, "Bar");

        // A three-by-one-by-one run, with only its far end filled by the second
        // material, so the index arithmetic has somewhere to go wrong.
        Guid steel = project.CreateMaterial("Steel", new Color(0.6f, 0.6f, 0.7f));

        Fill(scene, element, Default(project), new Vector3I(4, 7, 9));
        Fill(scene, element, steel, new Vector3I(6, 7, 9));

        RenderScene compiled = RenderFactory.CompileScene(project, scene);

        Assert.Equal(3, compiled.CellCount);
        Assert.Equal(1u, compiled.Voxels[0]);
        Assert.Equal(0u, compiled.Voxels[1]);
        Assert.Equal(2u, compiled.Voxels[2]);
    }

    /// <summary>
    /// The grid is dense over an element's own box, so two cells at opposite ends
    /// of a huge one would ask for a grid nobody has the memory for. Refused and
    /// named, never silently allocated.
    /// </summary>
    [Fact]
    public void AModelTooSparseToHoldDenselyIsRefusedAndNamed()
    {
        (IProject project, IScene scene) = NewProject(new Vector3I(512, 512, 512));
        Guid element = VoxelElement(scene, "Scattered");

        Fill(scene, element, Default(project), Vector3I.Zero, new Vector3I(400, 400, 400));

        RenderScene compiled = RenderFactory.CompileScene(project, scene);

        Assert.True(compiled.IsEmpty);
        Assert.Equal(new[] { "Scattered" }, compiled.OversizedElements);
    }

    /// <summary>
    /// Cell (x,y,z) occupies the box from (x,y,z) to (x+1,y+1,z+1) in the
    /// element's own frame, and the world box is that carried across — so a model
    /// standing at the origin ends one cell past its last filled one.
    /// </summary>
    [Fact]
    public void TheWorldBoxCoversTheWholeOfEveryCell()
    {
        (IProject project, IScene scene) = NewProject();
        Guid element = VoxelElement(scene, "Block");

        Fill(scene, element, Default(project), new Vector3I(2, 0, 0), new Vector3I(3, 1, 1));

        RenderScene compiled = RenderFactory.CompileScene(project, scene);

        Assert.True(
            compiled.BoundsMin.IsEqualApprox(new Vector3(2f, 0f, 0f)),
            compiled.BoundsMin.ToString());
        Assert.True(
            compiled.BoundsMax.IsEqualApprox(new Vector3(4f, 2f, 2f)),
            compiled.BoundsMax.ToString());
    }

    /// <summary>
    /// A rotated element keeps its cells in its own frame — that is the whole
    /// reason the tracer marches each element separately — so what moves is the
    /// world box, not the grid.
    /// </summary>
    /// <remarks>
    /// A two-cell bar, because a single cube turned a quarter about its own centre
    /// lands exactly on itself: the pivot defaults to the middle of the first edit,
    /// so the box would not move and the test would prove nothing.
    /// </remarks>
    [Fact]
    public void ARotatedElementKeepsItsGridAndMovesItsBox()
    {
        (IProject project, IScene scene) = NewProject();
        Guid element = VoxelElement(scene, "Bar");

        Fill(scene, element, Default(project), Vector3I.Zero, new Vector3I(1, 0, 0));

        RenderScene upright = RenderFactory.CompileScene(project, scene);

        Assert.True(
            (upright.BoundsMax - upright.BoundsMin).IsEqualApprox(new Vector3(2f, 1f, 1f)),
            (upright.BoundsMax - upright.BoundsMin).ToString());

        scene.SetTransform(element, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));

        RenderScene turned = RenderFactory.CompileScene(project, scene);

        // The grid is untouched: the cells are the element's own and the ray is
        // what gets carried into their frame.
        Assert.Equal(2, turned.CellCount);
        Assert.Equal(new Vector3I(2, 1, 1), turned.Elements[0].GridSize);

        // The box is not: two cells long along X became two cells long along Z.
        Assert.True(
            (turned.BoundsMax - turned.BoundsMin).IsEqualApprox(new Vector3(1f, 1f, 2f)),
            (turned.BoundsMax - turned.BoundsMin).ToString());
    }

    /// <summary>
    /// The transform written for the GPU is the inverse of where the element
    /// stands, since the ray is what gets carried into its frame.
    /// </summary>
    [Fact]
    public void TheElementCarriesTheInverseOfItsWorldTransform()
    {
        (IProject project, IScene scene) = NewProject();
        Guid element = VoxelElement(scene, "Block");

        Fill(scene, element, Default(project), Vector3I.Zero);
        scene.SetTransform(element, TransformChannel.Position, new Vector3(5f, 6f, 7f));

        RenderScene compiled = RenderFactory.CompileScene(project, scene);
        scene.TryGetElement(element, out IHierarchyElement placed);

        Transform3D expected = placed.WorldTransform.AffineInverse();

        Assert.True(
            compiled.Elements[0].WorldToLocal.IsEqualApprox(expected),
            compiled.Elements[0].WorldToLocal.ToString());
    }

    private static (IProject Project, IScene Scene) NewProject(Vector3I? size = null)
    {
        IProject project = ModelFactory.CreateProject(size ?? new Vector3I(32, 32, 32));

        return (project, project.Scenes[0]);
    }

    private static Guid Default(IProject project) => project.Materials[0].Id;

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
