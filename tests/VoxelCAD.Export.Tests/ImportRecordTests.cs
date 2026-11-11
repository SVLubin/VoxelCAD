using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Export.Tests;

/// <summary>
/// An import is one thing the user did, so it is one record — and a refused one
/// is no record at all.
/// </summary>
/// <remarks>
/// The second half is the one worth a test of its own. The batch is opened only
/// once the file has been read and found sound, on the reasoning that nothing
/// left can fail; if anything inside it can, the batch closes over what it
/// managed to do and files a partial import as one undoable record.
/// </remarks>
public sealed class ImportRecordTests
{
    [Fact]
    public void AWholeImportIsOneRecord()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();

        Apply(project, scene, scene.Id);

        Assert.Single(project.History.Records);
    }

    [Fact]
    public void UndoTakesTheCellsAndTheMaterialsTogether()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        int before = project.Materials.Count;

        Apply(project, scene, scene.Id);

        Assert.Equal(before + 2, project.Materials.Count);

        project.History.Undo();

        Assert.Equal(before, project.Materials.Count);
        Assert.Empty(scene.Children);

        project.History.Redo();

        Assert.Equal(before + 2, project.Materials.Count);
        Assert.Single(scene.Children);
    }

    /// <summary>
    /// A prefab's contents refuse every edit, so creating an element under one
    /// throws — and the materials are attached before the elements are. Refused
    /// half way, the import would leave the registry holding materials nothing
    /// wears and the history holding a record for an import that did not happen.
    /// </summary>
    [Fact]
    public void AnImportUnderSomethingThatRefusesItLeavesNothingBehind()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        Guid source = ModelBuilder.VoxelElement(scene, "Source");
        ModelBuilder.Fill(scene, source, ModelBuilder.DefaultMaterial(project), Vector3I.Zero);
        scene.CreatePrefab(source);

        // Making a prefab leaves an instance standing where the element was, and
        // what is under that instance is derived and refuses every edit.
        IHierarchyElement instance = Assert.Single(scene.Children);
        Guid content = Assert.Single(instance.Children).Id;
        int materials = project.Materials.Count;
        int records = project.History.Records.Count;

        Assert.ThrowsAny<Exception>(() => Apply(project, scene, content));

        Assert.Equal(materials, project.Materials.Count);
        Assert.Equal(records, project.History.Records.Count);
    }

    /// <summary>
    /// Several top-level parts go under one grouping element named after the file,
    /// so an import is one thing in the tree; a single part gets no such wrapper.
    /// Both halves, because a wrapper that always appeared and one that never did
    /// would each pass a test of only the other.
    /// </summary>
    [Fact]
    public void SeveralPartsGoUnderOneGroupAndASinglePartDoesNot()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();

        ApplyGraph(project, scene, parts: 3);

        IHierarchyElement wrapper = Assert.Single(scene.Children);

        Assert.Equal("part", wrapper.Name);
        Assert.Equal(ComponentKind.None, wrapper.ComponentKind);
        Assert.Equal(3, wrapper.Children.Count);

        // The parts are the file's own models, each with its cells.
        Assert.All(wrapper.Children, child =>
            Assert.Equal(ComponentKind.VoxelModel, child.ComponentKind));

        (IProject single, IScene singleScene) = ModelBuilder.NewProject();

        ApplyGraph(single, singleScene, parts: 1);

        IHierarchyElement alone = Assert.Single(singleScene.Children);

        Assert.Equal(ComponentKind.VoxelModel, alone.ComponentKind);
        Assert.Empty(alone.Children);
    }

    /// <summary>
    /// What the import answers with is what a caller reports back, so it has to
    /// count what actually arrived rather than what was asked for.
    /// </summary>
    [Fact]
    public void TheCountsAnswerForWhatArrived()
    {
        (IProject project, IScene scene) = ModelBuilder.NewProject();
        ImportResult result = ApplyGraph(project, scene, parts: 3);

        // Three parts and the group over them.
        Assert.Equal(4, result.Elements);

        // One cell each, and the two colours they wear between them.
        Assert.Equal(3, result.Cells);
        Assert.Equal(2, result.Materials);
        Assert.Equal("part", result.RootName);
        Assert.Equal(".vox", result.Format);
        Assert.Empty(result.Notes);

        // And the element the id names is the one the tree shows.
        Assert.True(scene.TryGetElement(result.RootId, out IHierarchyElement root));
        Assert.Same(root, Assert.Single(scene.Children));
    }

    /// <summary>A file whose scene graph places <paramref name="parts"/> models side by side.</summary>
    private static ImportResult ApplyGraph(IProject project, IScene scene, int parts)
    {
        VoxBuilder builder = VoxBuilder.File();

        for (var i = 0; i < parts; i++)
        {
            builder.Size(1, 1, 1);
            builder.Voxels((0, 0, 0, (byte)(i % 2 == 0 ? 1 : 2)));
        }

        builder.Palette((1, 255, 0, 0, 255), (2, 0, 0, 255, 255));
        builder.Transform(0, 1, Vector3I.Zero);

        var children = new int[parts];

        for (var i = 0; i < parts; i++)
            children[i] = 2 + i * 2;

        builder.Group(1, children);

        for (var i = 0; i < parts; i++)
        {
            builder.Transform(2 + i * 2, 3 + i * 2, new Vector3I(i * 4, 0, 0));
            builder.Shape(3 + i * 2, i);
        }

        var plan = new ImportPlan(
            new VoxImportFormat().Read(builder.Build(), "part"), "part", ".vox");

        return ImportProjection.Apply(project, scene, plan, scene.Id);
    }

    /// <summary>Two colours, one cell each, so a refusal has materials to leave behind.</summary>
    private static void Apply(IProject project, IScene scene, Guid parentId)
    {
        byte[] bytes = VoxBuilder.File()
            .Size(2, 1, 1)
            .Voxels((0, 0, 0, 1), (1, 0, 0, 2))
            .Palette((1, 255, 0, 0, 255), (2, 0, 0, 255, 255))
            .Build();

        var plan = new ImportPlan(new VoxImportFormat().Read(bytes, "part"), "part", ".vox");

        ImportProjection.Apply(project, scene, plan, parentId);
    }
}
