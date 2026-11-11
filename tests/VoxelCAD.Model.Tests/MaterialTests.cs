using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// The project-level material registry, and the single history that now covers
/// both materials and scene edits.
/// </summary>
public sealed class MaterialTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;

    public MaterialTests()
    {
        _scene = _project.Scenes[0];
    }

    private IHistory History => _project.History;

    [Fact]
    public void ANewProjectCanAlreadyPlaceVoxels()
    {
        Assert.Single(_project.Materials);
        Assert.Empty(History.Records);
    }

    [Fact]
    public void CreatedMaterialsAreFindableById()
    {
        Guid id = _project.CreateMaterial("Brass", Colors.Goldenrod);

        Assert.True(_project.TryGetMaterial(id, out IMaterial material));
        Assert.Equal("Brass", material.Name);
        Assert.Equal(Colors.Goldenrod, material.Color);
    }

    [Fact]
    public void RenamingAndRecolouringAreUndoable()
    {
        Guid id = _project.CreateMaterial("Brass", Colors.Goldenrod);

        _project.RenameMaterial(id, "Polished brass");
        _project.SetMaterialColor(id, Colors.Red);

        History.Undo();
        Assert.Equal(Colors.Goldenrod, Material(id).Color);
        Assert.Equal("Polished brass", Material(id).Name);

        History.Undo();
        Assert.Equal("Brass", Material(id).Name);
    }

    [Fact]
    public void EditsThatChangeNothingAreNotRecorded()
    {
        Guid id = _project.CreateMaterial("Brass", Colors.Goldenrod);
        var before = History.Records.Count;

        _project.RenameMaterial(id, "Brass");
        _project.SetMaterialColor(id, Colors.Goldenrod);

        Assert.Equal(before, History.Records.Count);
    }

    [Fact]
    public void UndoOfACreationRemovesTheMaterialAndRedoBringsBackTheSameId()
    {
        Guid id = _project.CreateMaterial("Brass", Colors.Goldenrod);

        History.Undo();
        Assert.False(_project.TryGetMaterial(id, out _));

        History.Redo();
        Assert.True(_project.TryGetMaterial(id, out _));
    }

    [Fact]
    public void DeletionRestoresItsPlaceInTheList()
    {
        Guid first = _project.CreateMaterial("First", Colors.Red);
        Guid second = _project.CreateMaterial("Second", Colors.Green);

        _project.DeleteMaterial(first);
        History.Undo();

        Assert.Equal(
            new[] { _project.Materials[0].Id, first, second },
            _project.Materials.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void AMaterialStillOnAVoxelCannotBeDeleted()
    {
        Guid id = _project.CreateMaterial("Brass", Colors.Goldenrod);
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(element, new[] { new VoxelEdit(Vector3I.Zero, id) });

        Assert.Throws<InvalidOperationException>(() => _project.DeleteMaterial(id));
    }

    [Fact]
    public void AMaterialFreedByUndoCanBeDeleted()
    {
        Guid id = _project.CreateMaterial("Brass", Colors.Goldenrod);
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(element, new[] { new VoxelEdit(Vector3I.Zero, id) });

        History.Undo();
        _project.DeleteMaterial(id);

        Assert.False(_project.TryGetMaterial(id, out _));
    }

    [Fact]
    public void TheLastMaterialCannotBeDeleted()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _project.DeleteMaterial(_project.Materials[0].Id));
    }

    [Fact]
    public void PlacingAVoxelWithAnUnknownMaterialIsRefused()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        Assert.Throws<ArgumentException>(() =>
            _scene.SetVoxels(element, new[] { new VoxelEdit(Vector3I.Zero, Guid.NewGuid()) }));
    }

    [Fact]
    public void MaterialChangesAreAnnounced()
    {
        var changesList = new List<ProjectChange>();
        _project.Changed += changesList.Add;

        Guid id = _project.CreateMaterial("Brass", Colors.Goldenrod);
        _project.SetMaterialColor(id, Colors.Red);
        _project.DeleteMaterial(id);

        Assert.Equal(
            new[]
            {
                ProjectChangeKind.MaterialAdded,
                ProjectChangeKind.MaterialChanged,
                ProjectChangeKind.MaterialRemoved
            },
            changesList.Select(c => c.Kind).ToArray());
    }

    [Fact]
    public void OneHistoryCoversMaterialsAndScenesAlike()
    {
        // Undo walks back through whatever the user last did, in order, without
        // caring which panel it belonged to.
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.None, "Element");
        Guid material = _project.CreateMaterial("Brass", Colors.Goldenrod);
        _scene.RenameElement(element, "Renamed");

        Assert.Equal(
            new[] { ActionKind.CreateElement, ActionKind.CreateMaterial, ActionKind.RenameElement },
            History.Records.Select(r => r.Kind).ToArray());

        History.Undo();
        Assert.Equal("Element", _scene.Element(element).Name);

        History.Undo();
        Assert.False(_project.TryGetMaterial(material, out _));

        History.Undo();
        Assert.Empty(_scene.Children);
    }

    private IMaterial Material(Guid id)
    {
        Assert.True(_project.TryGetMaterial(id, out IMaterial material));

        return material;
    }
}
