using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// Voxel cells live in the model, are edited only through the scene, and undo
/// restores them exactly — including erasures.
/// </summary>
public sealed class VoxelModelTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _stone;
    private readonly Guid _glass;
    private readonly Guid _element;

    public VoxelModelTests()
    {
        _scene = _project.Scenes[0];
        _stone = _project.Materials[0].Id;
        _glass = _project.CreateMaterial("Glass", new Color(0.6f, 0.7f, 0.9f, 0.4f));
        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
    }

    private IHistory History => _project.History;

    [Fact]
    public void AVoxelElementCarriesAVoxelModelPointingBackAtIt()
    {
        IHierarchyElement element = _scene.Element(_element);

        Assert.Equal(ComponentKind.VoxelModel, element.ComponentKind);
        IVoxelModel model = Assert.IsAssignableFrom<IVoxelModel>(element.Component);
        Assert.Same(element, model.Owner);
    }

    [Fact]
    public void AGroupElementHasNoComponent()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        Assert.Null(_scene.Element(group).Component);
        Assert.Equal(ComponentKind.None, _scene.Element(group).ComponentKind);
    }

    [Fact]
    public void ANewModelIsEmpty()
    {
        Assert.Empty(Model().Voxels);
    }

    [Fact]
    public void SettingACellFillsIt()
    {
        Set(new VoxelEdit(new Vector3I(1, 2, 3), _stone));

        Assert.True(Model().TryGetVoxel(new Vector3I(1, 2, 3), out Guid material));
        Assert.Equal(_stone, material);
    }

    [Fact]
    public void SettingACellToNullErasesIt()
    {
        Set(new VoxelEdit(Vector3I.Zero, _stone));
        Set(new VoxelEdit(Vector3I.Zero, null));

        Assert.Empty(Model().Voxels);
    }

    [Fact]
    public void PaintingReplacesTheMaterialWithoutAddingACell()
    {
        Set(new VoxelEdit(Vector3I.Zero, _stone));
        Set(new VoxelEdit(Vector3I.Zero, _glass));

        Assert.Equal(_glass, Model().Voxels[Vector3I.Zero]);
        Assert.Single(Model().Voxels);
    }

    [Fact]
    public void ABulkEditIsOneUndoStep()
    {
        var before = History.Records.Count;

        // An 8-cube, so every cell lands inside the buildable region.
        Set(Enumerable.Range(0, 512).Select(i =>
            new VoxelEdit(new Vector3I(i % 8, i / 8 % 8, i / 64), _stone)));

        Assert.Equal(512, Model().Voxels.Count);
        Assert.Equal(before + 1, History.Records.Count);
        Assert.Equal(ActionKind.EditVoxels, History.Records[^1].Kind);
    }

    [Fact]
    public void UndoRestoresBothFilledAndClearedCells()
    {
        Set(new VoxelEdit(new Vector3I(0, 0, 0), _stone));
        Set(new VoxelEdit(new Vector3I(1, 0, 0), _glass));

        Set(new[]
        {
            new VoxelEdit(new Vector3I(0, 0, 0), null),      // erase
            new VoxelEdit(new Vector3I(1, 0, 0), _stone),     // repaint
            new VoxelEdit(new Vector3I(2, 0, 0), _stone)      // add
        });

        History.Undo();

        Assert.Equal(2, Model().Voxels.Count);
        Assert.Equal(_stone, Model().Voxels[new Vector3I(0, 0, 0)]);
        Assert.Equal(_glass, Model().Voxels[new Vector3I(1, 0, 0)]);
        Assert.False(Model().TryGetVoxel(new Vector3I(2, 0, 0), out _));
    }

    [Fact]
    public void RedoReappliesTheWholeDelta()
    {
        Set(new VoxelEdit(Vector3I.Zero, _stone));
        Set(new VoxelEdit(Vector3I.Zero, null));

        History.Undo();
        History.Redo();

        Assert.Empty(Model().Voxels);
    }

    [Fact]
    public void ACellTouchedTwiceInOneEditKeepsItsOriginalUndoState()
    {
        // A brush stroke that crosses itself must still undo to where it started,
        // not to an intermediate value.
        Set(new VoxelEdit(Vector3I.Zero, _stone));

        Set(new[]
        {
            new VoxelEdit(Vector3I.Zero, _glass),
            new VoxelEdit(Vector3I.Zero, null)
        });

        Assert.Empty(Model().Voxels);

        History.Undo();

        Assert.Equal(_stone, Model().Voxels[Vector3I.Zero]);
    }

    [Fact]
    public void EditsThatChangeNothingAreNotRecorded()
    {
        Set(new VoxelEdit(Vector3I.Zero, _stone));
        var before = History.Records.Count;

        Set(new VoxelEdit(Vector3I.Zero, _stone));
        Set(new VoxelEdit(new Vector3I(9, 9, 9), null));

        Assert.Equal(before, History.Records.Count);
    }

    /// <summary>
    /// The first edit of an empty model announces two things, because it did two:
    /// the cells changed, and the pivot got its default from them. Later edits are
    /// back to one.
    /// </summary>
    [Fact]
    public void VoxelChangesAreAnnounced()
    {
        var changesList = new List<SceneChange>();
        _scene.Changed += changesList.Add;

        Set(new VoxelEdit(Vector3I.Zero, _stone));

        Assert.Equal(
            new[] { SceneChangeKind.VoxelsChanged, SceneChangeKind.PivotChanged },
            changesList.ConvertAll(change => change.Kind));
        Assert.All(changesList, change => Assert.Equal(_element, change.ElementId));

        changesList.Clear();
        Set(new VoxelEdit(new Vector3I(1, 0, 0), _stone));

        Assert.Equal(SceneChangeKind.VoxelsChanged, Assert.Single(changesList).Kind);
    }

    [Fact]
    public void UndoAnnouncesVoxelChangesToo()
    {
        Set(new VoxelEdit(Vector3I.Zero, _stone));

        var changesList = new List<SceneChange>();
        _scene.Changed += changesList.Add;
        History.Undo();

        // The default pivot recedes with the edit that gifted it.
        Assert.Equal(
            new[] { SceneChangeKind.VoxelsChanged, SceneChangeKind.PivotChanged },
            changesList.ConvertAll(change => change.Kind));
    }

    [Fact]
    public void EditingAnElementWithoutAVoxelModelIsRefused()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        Assert.Throws<InvalidOperationException>(() =>
            _scene.SetVoxels(group, new[] { new VoxelEdit(Vector3I.Zero, _stone) }));
    }

    [Fact]
    public void VoxelDataSurvivesDeleteAndUndo()
    {
        Set(new VoxelEdit(new Vector3I(4, 5, 6), _stone));

        _scene.DeleteElement(_element);
        History.Undo();

        Assert.Equal(_stone, Model().Voxels[new Vector3I(4, 5, 6)]);
    }

    [Fact]
    public void PrefabInstancesCannotBeCreatedFromABareKind()
    {
        Assert.Throws<NotSupportedException>(() =>
            _scene.CreateElement(_scene.Id, ComponentKind.PrefabInstance, "Instance"));
    }

    private IVoxelModel Model() => (IVoxelModel)_scene.Element(_element).Component!;

    private void Set(VoxelEdit edit) => _scene.SetVoxels(_element, new[] { edit });

    private void Set(IEnumerable<VoxelEdit> edits) => _scene.SetVoxels(_element, edits.ToArray());
}
