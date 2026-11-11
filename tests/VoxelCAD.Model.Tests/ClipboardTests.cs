using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// Copying and pasting hierarchy items: a deep copy that refers to nothing, one
/// undoable record per paste, and a name composed against what is already there.
/// </summary>
public sealed class ClipboardTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _material;

    public ClipboardTests()
    {
        _scene = _project.Scenes[0];
        _material = _project.Materials[0].Id;
    }

    [Fact]
    public void APastedElementIsANewElement()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        Guid pasted = _scene.PasteElement(_scene.Id, _scene.CaptureElement(source));

        Assert.NotEqual(source, pasted);
        Assert.Equal(2, _scene.Children.Count);
    }

    [Fact]
    public void ThePastedCopyCarriesTheCells()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(source, new[]
        {
            new VoxelEdit(new Vector3I(1, 2, 3), _material),
            new VoxelEdit(new Vector3I(4, 5, 6), _material)
        });

        Guid pasted = _scene.PasteElement(_scene.Id, _scene.CaptureElement(source));

        IReadOnlyDictionary<Vector3I, Guid> voxels = Voxels(pasted);

        Assert.Equal(2, voxels.Count);
        Assert.Equal(_material, voxels[new Vector3I(1, 2, 3)]);
        Assert.Equal(_material, voxels[new Vector3I(4, 5, 6)]);
    }

    /// <summary>
    /// The whole promise: the copy is not a view onto the original. Editing either
    /// one afterwards leaves the other exactly as it was.
    /// </summary>
    [Fact]
    public void EditingTheOriginalLeavesTheCopyAlone()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(source, new[] { new VoxelEdit(Vector3I.Zero, _material) });

        Guid pasted = _scene.PasteElement(_scene.Id, _scene.CaptureElement(source));
        Vector3 position = _scene.Element(pasted).Transform.Position;

        _scene.SetVoxels(source, new[] { new VoxelEdit(new Vector3I(9, 9, 9), _material) });
        _scene.SetTransform(source, TransformChannel.Position, new Vector3(5f, 0f, 0f));
        _scene.RenameElement(source, "Renamed");

        Assert.Single(Voxels(pasted));
        Assert.Equal(position, _scene.Element(pasted).Transform.Position);
        Assert.Equal("Model 2", _scene.Element(pasted).Name);
    }

    /// <summary>
    /// A clip outlives what it was taken from — which is exactly what a cut is, one
    /// step at a time.
    /// </summary>
    [Fact]
    public void AClipSurvivesTheOriginalBeingDeleted()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(source, new[] { new VoxelEdit(new Vector3I(2, 2, 2), _material) });

        ElementClip clip = _scene.CaptureElement(source);
        _scene.DeleteElement(source);

        Guid pasted = _scene.PasteElement(_scene.Id, clip);

        Assert.Single(_scene.Children);
        Assert.Single(Voxels(pasted));

        // The name is free again, so a cut and a paste put it back unchanged.
        Assert.Equal("Model", _scene.Element(pasted).Name);
    }

    [Fact]
    public void TheCopyCarriesTheWholeSubtree()
    {
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid child = _scene.CreateElement(parent, ComponentKind.VoxelModel, "Child");
        _scene.CreateElement(child, ComponentKind.None, "Grandchild");

        Guid pasted = _scene.PasteElement(_scene.Id, _scene.CaptureElement(parent));

        IHierarchyElement copy = _scene.Element(pasted);

        Assert.Single(copy.Children);
        Assert.Equal("Child", copy.Children[0].Name);
        Assert.Equal(ComponentKind.VoxelModel, copy.Children[0].ComponentKind);
        Assert.Single(copy.Children[0].Children);
        Assert.Equal("Grandchild", copy.Children[0].Children[0].Name);
    }

    /// <summary>
    /// Only the root is renamed. What is inside a copied assembly is expected to
    /// read like the inside of the original — two wheels called "Wheel" is what
    /// duplicating a car means.
    /// </summary>
    [Fact]
    public void OnlyTheRootOfACopyIsRenamed()
    {
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        _scene.CreateElement(parent, ComponentKind.None, "Wheel");

        Guid pasted = _scene.PasteElement(_scene.Id, _scene.CaptureElement(parent));

        Assert.Equal("Group 2", _scene.Element(pasted).Name);
        Assert.Equal("Wheel", _scene.Element(pasted).Children[0].Name);
    }

    [Fact]
    public void TheCopyCarriesTransformModesPivotAndVisibility()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        _scene.SetFieldMode(source, TransformChannel.Position, FieldMode.Float);
        _scene.SetTransform(source, TransformChannel.Position, new Vector3(1.5f, 0f, 0f));
        _scene.SetTransform(source, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));
        _scene.SetPivot(source, new Vector3(0.5f, 1f, 0f));
        _scene.SetVisible(source, false);

        Guid pasted = _scene.PasteElement(_scene.Id, _scene.CaptureElement(source));

        IHierarchyElement copy = _scene.Element(pasted);

        Assert.Equal(FieldMode.Float, copy.TransformModes.Position);
        Assert.Equal(new Vector3(0.5f, 1f, 0f), copy.PivotPoint);
        Assert.False(copy.Visible);
        Assert.True(
            _scene.Element(source).Transform.Position.IsEqualApprox(copy.Transform.Position),
            $"{_scene.Element(source).Transform.Position} != {copy.Transform.Position}");
    }

    [Fact]
    public void TheCopyCarriesSymmetry()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(source, new[] { new VoxelEdit(Vector3I.Zero, _material) });
        _scene.SetSymmetry(source, new SymmetryValues(
            new SymmetryPlane(Enabled: true, PlaneDoubled: 4),
            default,
            default));

        Guid pasted = _scene.PasteElement(_scene.Id, _scene.CaptureElement(source));

        var model = (IVoxelModel)_scene.Element(pasted).Component!;

        Assert.True(model.Symmetry.X.Enabled);
        Assert.Equal(4, model.Symmetry.X.PlaneDoubled);
    }

    [Fact]
    public void PastingIsOneRecordThatUndoesWhole()
    {
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid child = _scene.CreateElement(parent, ComponentKind.VoxelModel, "Child");
        _scene.SetVoxels(child, new[] { new VoxelEdit(Vector3I.Zero, _material) });

        int before = _project.History.Records.Count;
        _scene.PasteElement(_scene.Id, _scene.CaptureElement(parent));

        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.CreateElement, _project.History.Records[^1].Kind);

        _project.History.Undo();

        Assert.Single(_scene.Children);

        _project.History.Redo();

        Assert.Equal(2, _scene.Children.Count);
    }

    /// <summary>Copying reads the document; it must leave no trace in the history.</summary>
    [Fact]
    public void CopyingRecordsNothing()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        int before = _project.History.Records.Count;

        _scene.CaptureElement(source);
        _project.CaptureScene(_scene.Id);
        _project.CaptureMaterial(_material);

        Assert.Equal(before, _project.History.Records.Count);
    }

    [Fact]
    public void APastedElementLandsUnderTheGivenParent()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.None, "Model");
        Guid parent = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        Guid pasted = _scene.PasteElement(parent, _scene.CaptureElement(source));

        Assert.Equal(parent, _scene.Element(pasted).Parent.Id);
    }

    /// <summary>
    /// A clip refers to nothing, so where it came from is not where it has to go —
    /// which is what makes copying between scenes work at all.
    /// </summary>
    [Fact]
    public void AClipCanBePastedIntoAnotherScene()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(source, new[] { new VoxelEdit(new Vector3I(1, 1, 1), _material) });

        Guid otherId = _project.CreateScene("Other", new Vector3I(16, 16, 16));
        Assert.True(_project.TryGetScene(otherId, out IScene other));

        Guid pasted = other.PasteElement(other.Id, _scene.CaptureElement(source));

        // The name was free in the scene it landed in, so it kept it.
        Assert.Equal("Model", other.Element(pasted).Name);
        Assert.Single(((IVoxelModel)other.Element(pasted).Component!).Voxels);
    }

    /// <summary>
    /// The narrow window a clip has to survive: cutting a model can leave the
    /// material it was the last user of deletable, and deleted before the paste.
    /// The cells come back wearing a material rather than a dangling id.
    /// </summary>
    [Fact]
    public void PastingRebuildsAMaterialTheProjectHasLost()
    {
        Guid paint = _project.CreateMaterial("Paint", new Color(0.2f, 0.4f, 0.6f));
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(source, new[] { new VoxelEdit(Vector3I.Zero, paint) });

        ElementClip clip = _scene.CaptureElement(source);
        _scene.DeleteElement(source);
        _project.DeleteMaterial(paint);

        Guid pasted = _scene.PasteElement(_scene.Id, clip);
        Guid worn = Voxels(pasted)[Vector3I.Zero];

        Assert.NotEqual(paint, worn);
        Assert.True(_project.TryGetMaterial(worn, out IMaterial rebuilt));
        Assert.Equal("Paint", rebuilt.Name);
        Assert.Equal(new Color(0.2f, 0.4f, 0.6f), rebuilt.Color);
    }

    /// <summary>
    /// One action, so undo takes the material back with the cells that needed it —
    /// otherwise a paste undone would leave a material behind that nobody asked for.
    /// </summary>
    [Fact]
    public void UndoingSuchAPasteTakesTheRebuiltMaterialWithIt()
    {
        Guid paint = _project.CreateMaterial("Paint", Colors.Red);
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(source, new[] { new VoxelEdit(Vector3I.Zero, paint) });

        ElementClip clip = _scene.CaptureElement(source);
        _scene.DeleteElement(source);
        _project.DeleteMaterial(paint);

        int materials = _project.Materials.Count;
        int records = _project.History.Records.Count;

        _scene.PasteElement(_scene.Id, clip);

        Assert.Equal(materials + 1, _project.Materials.Count);
        Assert.Equal(records + 1, _project.History.Records.Count);

        _project.History.Undo();

        Assert.Equal(materials, _project.Materials.Count);
        Assert.Empty(_scene.Children);
    }

    /// <summary>
    /// A material the project still has is shared rather than duplicated: that is
    /// what a registry is for, and recolouring it should repaint both copies.
    /// </summary>
    [Fact]
    public void PastingSharesAMaterialTheProjectStillHas()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(source, new[] { new VoxelEdit(Vector3I.Zero, _material) });

        int materials = _project.Materials.Count;
        Guid pasted = _scene.PasteElement(_scene.Id, _scene.CaptureElement(source));

        Assert.Equal(materials, _project.Materials.Count);
        Assert.Equal(_material, Voxels(pasted)[Vector3I.Zero]);
    }

    [Fact]
    public void APastedSceneCarriesItsRegionAndItsElements()
    {
        Guid sourceId = _project.CreateScene("Room", new Vector3I(8, 9, 10));
        Assert.True(_project.TryGetScene(sourceId, out IScene source));

        Guid model = source.CreateElement(source.Id, ComponentKind.VoxelModel, "Model");
        source.SetVoxels(model, new[] { new VoxelEdit(new Vector3I(3, 3, 3), _material) });

        Guid pastedId = _project.PasteScene(_project.CaptureScene(sourceId));
        Assert.True(_project.TryGetScene(pastedId, out IScene pasted));

        Assert.NotEqual(sourceId, pastedId);
        Assert.Equal("Room 2", pasted.Name);
        Assert.Equal(new Vector3I(8, 9, 10), pasted.Size);
        Assert.Single(pasted.Children);
        Assert.NotEqual(model, pasted.Children[0].Id);
        Assert.Single(((IVoxelModel)pasted.Children[0].Component!).Voxels);
    }

    [Fact]
    public void PastingASceneIsOneRecord()
    {
        int records = _project.History.Records.Count;

        _project.PasteScene(_project.CaptureScene(_scene.Id));

        Assert.Equal(records + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.CreateScene, _project.History.Records[^1].Kind);

        _project.History.Undo();

        Assert.Single(_project.Scenes);
    }

    [Fact]
    public void APastedMaterialIsAMaterialOfItsOwn()
    {
        Guid source = _project.CreateMaterial("Paint", new Color(0.1f, 0.2f, 0.3f, 0.5f));
        _project.SetMaterialAppearance(
            source, MaterialAppearance.Default with { Roughness = 0.25f });

        Guid pasted = _project.PasteMaterial(_project.CaptureMaterial(source));

        Assert.NotEqual(source, pasted);
        Assert.True(_project.TryGetMaterial(pasted, out IMaterial copy));
        Assert.Equal("Paint 2", copy.Name);
        Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.5f), copy.Color);
        Assert.Equal(0.25f, copy.Appearance.Roughness);
    }

    /// <summary>
    /// Voxels name a material by id, and the copy's id is not the one they name —
    /// so recolouring the copy leaves the original's voxels alone.
    /// </summary>
    [Fact]
    public void APastedMaterialRepaintsNothing()
    {
        Guid source = _project.CreateMaterial("Paint", Colors.Red);
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _scene.SetVoxels(model, new[] { new VoxelEdit(Vector3I.Zero, source) });

        Guid pasted = _project.PasteMaterial(_project.CaptureMaterial(source));
        _project.SetMaterialColor(pasted, Colors.Blue);

        Assert.Equal(source, Voxels(model)[Vector3I.Zero]);
        Assert.True(_project.TryGetMaterial(source, out IMaterial original));
        Assert.Equal(Colors.Red, original.Color);
    }

    /// <summary>
    /// Counting from the name's own trailing number, so a copy of a copy does not
    /// grow a second one.
    /// </summary>
    [Fact]
    public void ACopyOfACopyCountsOn()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.None, "Model");

        Guid second = _scene.PasteElement(_scene.Id, _scene.CaptureElement(source));
        Guid third = _scene.PasteElement(_scene.Id, _scene.CaptureElement(second));

        Assert.Equal("Model 2", _scene.Element(second).Name);
        Assert.Equal("Model 3", _scene.Element(third).Name);
    }

    [Fact]
    public void AnExplicitNameIsTakenAsGiven()
    {
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.None, "Model");

        Guid pasted = _scene.PasteElement(_scene.Id, _scene.CaptureElement(source), "Left wing");

        Assert.Equal("Left wing", _scene.Element(pasted).Name);
    }

    /// <summary>Scene-wide, because that is the scope an element's name has to be unambiguous in.</summary>
    [Fact]
    public void TheNameIsComposedAgainstTheWholeScene()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        _scene.CreateElement(group, ComponentKind.None, "Model 2");
        Guid source = _scene.CreateElement(_scene.Id, ComponentKind.None, "Model");

        Guid pasted = _scene.PasteElement(_scene.Id, _scene.CaptureElement(source));

        Assert.Equal("Model 3", _scene.Element(pasted).Name);
    }

    private IReadOnlyDictionary<Vector3I, Guid> Voxels(Guid elementId) =>
        ((IVoxelModel)_scene.Element(elementId).Component!).Voxels;
}
