using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// The prefab library: making one out of an element, instancing it, editing the
/// source, unpacking an instance and deleting the entry. The rule underneath all of
/// them is that an instance's contents are <b>derived</b> — never edited, never
/// stored, rebuilt from the source whenever it changes.
/// </summary>
public sealed class PrefabTests
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _material;

    public PrefabTests()
    {
        _scene = _project.Scenes[0];
        _material = _project.Materials[0].Id;
    }

    [Fact]
    public void MakingOneLeavesAnInstanceInItsPlace()
    {
        Guid model = CreateModel("Wheel", new Vector3I(1, 1, 1));

        Guid prefabId = _scene.CreatePrefab(model);

        Assert.Single(_scene.Children);

        IHierarchyElement instance = _scene.Children[0];

        Assert.Equal(ComponentKind.PrefabInstance, instance.ComponentKind);
        Assert.Equal("Wheel", instance.Name);
        Assert.NotEqual(model, instance.Id);
        Assert.Equal(prefabId, SourceOf(instance));
    }

    [Fact]
    public void TheLibraryEntryHoldsTheContentsAndCopiesTheRoom()
    {
        Guid model = CreateModel("Wheel", new Vector3I(1, 1, 1));

        IPrefab prefab = Prefab(_scene.CreatePrefab(model));

        Assert.Equal("Wheel", prefab.Name);
        Assert.Equal(_scene.Size, prefab.Content.Size);
        Assert.Single(prefab.Content.Children);
        Assert.Equal("Wheel", prefab.Content.Children[0].Name);
        Assert.Single(Voxels(prefab.Content.Children[0]));
    }

    /// <summary>
    /// The wrapper takes the element's place in the parent frame, and the contents
    /// go to the library at identity — so nothing appears to move, and the prefab's
    /// own room shows its cells where they were painted.
    /// </summary>
    [Fact]
    public void TheInstanceTakesTheTransformAndTheContentsGoToIdentity()
    {
        Guid model = CreateModel("Wheel", new Vector3I(1, 1, 1));
        _scene.SetTransform(model, TransformChannel.Position, new Vector3(9f, 3f, 5f));
        _scene.SetTransform(model, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));

        TransformValues stood = _scene.Element(model).Transform;
        Transform3D world = _scene.Element(model).WorldTransform;

        IPrefab prefab = Prefab(_scene.CreatePrefab(model));
        IHierarchyElement instance = _scene.Children[0];

        Assert.True(
            instance.Transform.Position.IsEqualApprox(stood.Position),
            $"the instance stands at {instance.Transform.Position}, not at {stood.Position}");
        Assert.True(
            instance.Transform.RotationDegrees.IsEqualApprox(stood.RotationDegrees),
            "the instance is turned differently");

        // The contents' own frame, not the reported position: the pivot travels with
        // the cells, so a model whose pivot is at 1.5 reports 1.5 while standing at
        // the origin of the prefab's room.
        IHierarchyElement content = prefab.Content.Children[0];

        Assert.True(
            content.WorldTransform.IsEqualApprox(Transform3D.Identity),
            $"the contents went to the library at {content.WorldTransform.Origin}, not at the origin");
        Assert.Equal(Vector3.Zero, content.Transform.RotationDegrees);
        Assert.Equal(Vector3.One, content.Transform.Scale);

        // The whole promise of the two halves together: nothing appears to move.
        Assert.True(
            instance.Children[0].WorldTransform.IsEqualApprox(world),
            "the geometry moved when the prefab was made");
    }

    /// <summary>
    /// A group has no pivot of its own, and an instance whose handles sat at the
    /// local origin would put them in a corner of the room rather than on the thing.
    /// </summary>
    [Fact]
    public void AnInstanceOfSomethingWithNoPivotIsGivenTheMiddleOfIt()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Assembly");
        Guid model = _scene.CreateElement(group, ComponentKind.VoxelModel, "Body");

        _scene.SetVoxels(model, new[]
        {
            new VoxelEdit(new Vector3I(10, 0, 10), _material),
            new VoxelEdit(new Vector3I(12, 0, 12), _material)
        });

        _scene.CreatePrefab(group);

        // The middle of the cells, which for a box from 10 to 12 is 11.5 — cell
        // coordinates name corners, so the middle of one cell is at its half.
        Assert.Equal(new Vector3(11.5f, 0.5f, 11.5f), _scene.Children[0].PivotPoint);
    }

    [Fact]
    public void TheContentsAppearUnderTheInstance()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Assembly");
        Guid model = _scene.CreateElement(group, ComponentKind.VoxelModel, "Body");
        _scene.SetVoxels(model, new[] { new VoxelEdit(new Vector3I(2, 2, 2), _material) });

        _scene.CreatePrefab(group);

        IHierarchyElement instance = _scene.Children[0];

        Assert.Single(instance.Children);
        Assert.Equal("Assembly", instance.Children[0].Name);
        Assert.Single(instance.Children[0].Children);
        Assert.Equal("Body", instance.Children[0].Children[0].Name);
        Assert.Single(Voxels(instance.Children[0].Children[0]));
    }

    [Fact]
    public void EverythingUnderAnInstanceIsMarkedAsItsContents()
    {
        Guid model = CreateModel("Wheel", new Vector3I(1, 1, 1));
        _scene.CreateElement(model, ComponentKind.None, "Hub");

        _scene.CreatePrefab(model);

        IHierarchyElement instance = _scene.Children[0];

        Assert.Null(instance.PrefabInstanceId);
        Assert.Equal(instance.Id, instance.Children[0].PrefabInstanceId);
        Assert.Equal(instance.Id, instance.Children[0].Children[0].PrefabInstanceId);
    }

    [Fact]
    public void TheContentsRefuseEveryEdit()
    {
        Guid model = CreateModel("Wheel", new Vector3I(1, 1, 1));
        _scene.CreatePrefab(model);

        Guid content = _scene.Children[0].Children[0].Id;

        Assert.Throws<InvalidOperationException>(() => _scene.RenameElement(content, "No"));
        Assert.Throws<InvalidOperationException>(() => _scene.DeleteElement(content));
        Assert.Throws<InvalidOperationException>(
            () => _scene.SetVoxels(content, new[] { new VoxelEdit(Vector3I.Zero, null) }));
        Assert.Throws<InvalidOperationException>(
            () => _scene.SetTransform(content, TransformChannel.Position, Vector3.One));
        Assert.Throws<InvalidOperationException>(() => _scene.SetVisible(content, false));
    }

    /// <summary>Nothing may be put inside an instance either — its children are not a place.</summary>
    [Fact]
    public void AnInstanceRefusesChildrenOfItsOwn()
    {
        Guid model = CreateModel("Wheel", new Vector3I(1, 1, 1));
        _scene.CreatePrefab(model);

        Guid instance = _scene.Children[0].Id;

        Assert.Throws<InvalidOperationException>(
            () => _scene.CreateElement(instance, ComponentKind.None, "Nope"));
    }

    /// <summary>The element itself is ordinary: move it, rename it, hide it, delete it.</summary>
    [Fact]
    public void TheInstanceElementIsAnOrdinaryElement()
    {
        Guid model = CreateModel("Wheel", new Vector3I(1, 1, 1));
        _scene.CreatePrefab(model);

        Guid instance = _scene.Children[0].Id;
        Vector3 stood = _scene.Element(instance).Transform.Position;

        _scene.RenameElement(instance, "Front left");
        _scene.SetTransform(instance, TransformChannel.Position, stood + new Vector3(4f, 0f, 0f));
        _scene.SetVisible(instance, false);

        Assert.Equal("Front left", _scene.Element(instance).Name);
        Assert.Equal(stood + new Vector3(4f, 0f, 0f), _scene.Element(instance).Transform.Position);
        Assert.False(_scene.Element(instance).Visible);
    }

    [Fact]
    public void EditingTheSourceReachesEveryInstance()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        Guid second = _scene.CreateInstance(_scene.Id, prefabId);
        IScene content = Prefab(prefabId).Content;

        content.SetVoxels(content.Children[0].Id, new[]
        {
            new VoxelEdit(new Vector3I(5, 5, 5), _material)
        });

        Assert.Equal(2, Voxels(_scene.Children[0].Children[0]).Count);
        Assert.Equal(2, Voxels(_scene.Element(second).Children[0]).Count);
    }

    /// <summary>
    /// A rebuild is not a fresh start: the ids are derived from the instance and the
    /// source element, so a selection or a row's expansion state survives a
    /// keystroke inside the prefab.
    /// </summary>
    [Fact]
    public void ARebuildKeepsTheIdsItHadBefore()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        IScene content = Prefab(prefabId).Content;

        Guid before = _scene.Children[0].Children[0].Id;

        content.SetVoxels(content.Children[0].Id, new[]
        {
            new VoxelEdit(new Vector3I(5, 5, 5), _material)
        });

        Assert.Equal(before, _scene.Children[0].Children[0].Id);
    }

    /// <summary>Two instances of one prefab are two different elements, not the same one twice.</summary>
    [Fact]
    public void TwoInstancesHoldDifferentContents()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        Guid second = _scene.CreateInstance(_scene.Id, prefabId);

        Assert.NotEqual(
            _scene.Children[0].Children[0].Id, _scene.Element(second).Children[0].Id);
    }

    /// <summary>
    /// An instance holds no cells of its own, so a pivot left at the local origin
    /// would put its handles in a corner of the room rather than on the thing.
    /// </summary>
    [Fact]
    public void AFreshInstanceIsGivenTheMiddleOfWhatItShows()
    {
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Wheel");

        _scene.SetVoxels(model, new[]
        {
            new VoxelEdit(new Vector3I(10, 0, 10), _material),
            new VoxelEdit(new Vector3I(12, 0, 12), _material)
        });

        Guid prefabId = _scene.CreatePrefab(model);
        Guid second = _scene.CreateInstance(_scene.Id, prefabId);

        Assert.Equal(new Vector3(11.5f, 0.5f, 11.5f), _scene.Element(second).PivotPoint);
    }

    [Fact]
    public void AnInstanceIsOneRecordAndUndoTakesItAway()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        var before = _project.History.Records.Count;

        _scene.CreateInstance(_scene.Id, prefabId, "Copy");

        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.CreateInstance, _project.History.Records[^1].Kind);

        _project.History.Undo();

        Assert.Single(_scene.Children);
    }

    [Fact]
    public void MakingOneIsOneRecordAndUndoPutsTheElementBack()
    {
        Guid model = CreateModel("Wheel", new Vector3I(1, 1, 1));
        var before = _project.History.Records.Count;

        Guid prefabId = _scene.CreatePrefab(model);

        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.CreatePrefab, _project.History.Records[^1].Kind);

        _project.History.Undo();

        Assert.Empty(_project.Prefabs);
        Assert.Single(_scene.Children);
        Assert.Equal(model, _scene.Children[0].Id);
        Assert.Equal(ComponentKind.VoxelModel, _scene.Children[0].ComponentKind);

        _project.History.Redo();

        Assert.Single(_project.Prefabs);
        Assert.Equal(prefabId, _project.Prefabs[0].Id);
        Assert.Equal(ComponentKind.PrefabInstance, _scene.Children[0].ComponentKind);
    }

    [Fact]
    public void UnpackingLeavesAGroupWithOrdinaryChildren()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        Guid instance = _scene.Children[0].Id;
        Guid derived = _scene.Children[0].Children[0].Id;

        _scene.UnpackInstance(instance);

        IHierarchyElement unpacked = _scene.Element(instance);

        Assert.Equal(ComponentKind.None, unpacked.ComponentKind);
        Assert.Single(unpacked.Children);
        Assert.NotEqual(derived, unpacked.Children[0].Id);
        Assert.Null(unpacked.Children[0].PrefabInstanceId);
        Assert.Single(Voxels(unpacked.Children[0]));

        // The library entry stays: unpacking is about this instance, not the prefab.
        Assert.Single(_project.Prefabs);
        Assert.Equal(prefabId, _project.Prefabs[0].Id);
    }

    [Fact]
    public void UnpackedContentsCanThenBeEdited()
    {
        _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        Guid instance = _scene.Children[0].Id;

        _scene.UnpackInstance(instance);

        Guid child = _scene.Element(instance).Children[0].Id;

        _scene.RenameElement(child, "Mine now");

        Assert.Equal("Mine now", _scene.Element(child).Name);
    }

    [Fact]
    public void UnpackingIsOneRecordAndUndoPutsTheInstanceBack()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        Guid instance = _scene.Children[0].Id;
        var before = _project.History.Records.Count;

        _scene.UnpackInstance(instance);

        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.UnpackInstance, _project.History.Records[^1].Kind);

        _project.History.Undo();

        Assert.Equal(ComponentKind.PrefabInstance, _scene.Element(instance).ComponentKind);
        Assert.Equal(prefabId, SourceOf(_scene.Element(instance)));
        Assert.Single(_scene.Element(instance).Children);
        Assert.Equal(instance, _scene.Element(instance).Children[0].PrefabInstanceId);
    }

    /// <summary>
    /// One level. A nested instance inside the contents goes on following its own
    /// source, which is what Unity does and what makes unpacking a statement about
    /// this instance rather than about everything under it.
    /// </summary>
    [Fact]
    public void UnpackingLeavesANestedInstanceAnInstance()
    {
        Guid innerId = _scene.CreatePrefab(CreateModel("Bolt", new Vector3I(1, 1, 1)));
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");

        _scene.ReparentElement(_scene.Children[0].Id, group, 0);

        Guid outerId = _scene.CreatePrefab(group);
        Guid outer = _scene.Children[0].Id;

        _scene.UnpackInstance(outer);

        IHierarchyElement bracket = _scene.Element(outer).Children[0];

        Assert.Equal(ComponentKind.None, _scene.Element(outer).ComponentKind);
        Assert.Equal(ComponentKind.PrefabInstance, bracket.Children[0].ComponentKind);
        Assert.Equal(innerId, SourceOf(bracket.Children[0]));
        Assert.Single(bracket.Children[0].Children);
        Assert.NotEqual(outerId, innerId);
    }

    [Fact]
    public void EditingAnInnerPrefabReachesInstancesOfTheOuterOne()
    {
        Guid innerId = _scene.CreatePrefab(CreateModel("Bolt", new Vector3I(1, 1, 1)));
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");

        _scene.ReparentElement(_scene.Children[0].Id, group, 0);
        _scene.CreatePrefab(group);

        IScene inner = Prefab(innerId).Content;

        inner.SetVoxels(inner.Children[0].Id, new[]
        {
            new VoxelEdit(new Vector3I(7, 7, 7), _material)
        });

        // scene → outer instance → Bracket → inner instance → Bolt
        IHierarchyElement bolt = _scene.Children[0].Children[0].Children[0].Children[0];

        Assert.Equal(2, Voxels(bolt).Count);
    }

    [Fact]
    public void APrefabCannotBePlacedInsideItself()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        IScene content = Prefab(prefabId).Content;

        Assert.Throws<InvalidOperationException>(
            () => content.CreateInstance(content.Id, prefabId));
    }

    [Fact]
    public void APrefabCannotBePlacedInsideOneItAlreadyContains()
    {
        Guid innerId = _scene.CreatePrefab(CreateModel("Bolt", new Vector3I(1, 1, 1)));
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");

        _scene.ReparentElement(_scene.Children[0].Id, group, 0);

        Guid outerId = _scene.CreatePrefab(group);
        IScene inner = Prefab(innerId).Content;

        Assert.Throws<InvalidOperationException>(() => inner.CreateInstance(inner.Id, outerId));
    }

    /// <summary>An ordinary scene is not a library entry, so nothing can nest inside it.</summary>
    [Fact]
    public void AnOrdinarySceneTakesAnyInstance()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));

        _scene.CreateInstance(_scene.Id, prefabId);
        _scene.CreateInstance(_scene.Id, prefabId);

        Assert.Equal(3, _scene.Children.Count);
    }

    [Fact]
    public void DeletingUnpacksEveryInstanceEverywhere()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        Guid other = _project.CreateScene("Second", new Vector3I(8, 8, 8));

        _project.TryGetScene(other, out IScene second);

        Guid there = second.CreateInstance(second.Id, prefabId);
        Guid here = _scene.Children[0].Id;

        Assert.True(_project.IsPrefabInUse(prefabId));

        _project.DeletePrefab(prefabId);

        Assert.Empty(_project.Prefabs);
        Assert.Equal(ComponentKind.None, _scene.Element(here).ComponentKind);
        Assert.Single(_scene.Element(here).Children);
        Assert.Null(_scene.Element(here).Children[0].PrefabInstanceId);
        Assert.Equal(ComponentKind.None, second.Element(there).ComponentKind);
        Assert.Single(second.Element(there).Children);
    }

    [Fact]
    public void DeletingIsOneRecordAndUndoBringsTheInstancesBack()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        Guid instance = _scene.Children[0].Id;
        var before = _project.History.Records.Count;

        _project.DeletePrefab(prefabId);

        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.DeletePrefab, _project.History.Records[^1].Kind);

        _project.History.Undo();

        Assert.Single(_project.Prefabs);
        Assert.Equal(ComponentKind.PrefabInstance, _scene.Element(instance).ComponentKind);
        Assert.Single(_scene.Element(instance).Children);
        Assert.Equal(instance, _scene.Element(instance).Children[0].PrefabInstanceId);
    }

    /// <summary>An instance inside another prefab's contents is unpacked too.</summary>
    [Fact]
    public void DeletingReachesInstancesInsideOtherPrefabs()
    {
        Guid innerId = _scene.CreatePrefab(CreateModel("Bolt", new Vector3I(1, 1, 1)));
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");

        _scene.ReparentElement(_scene.Children[0].Id, group, 0);

        Guid outerId = _scene.CreatePrefab(group);

        _project.DeletePrefab(innerId);

        IScene outer = Prefab(outerId).Content;

        Assert.Equal(ComponentKind.None, outer.Children[0].Children[0].ComponentKind);
        Assert.Single(outer.Children[0].Children[0].Children);

        // And the instance of the outer prefab standing in the scene followed it.
        IHierarchyElement bracket = _scene.Children[0].Children[0];

        Assert.Equal(ComponentKind.None, bracket.Children[0].ComponentKind);
        Assert.Single(bracket.Children[0].Children);
    }

    [Fact]
    public void RenamingTheLibraryEntryLeavesTheInstancesAlone()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));

        _project.RenamePrefab(prefabId, "Roller");

        Assert.Equal("Roller", Prefab(prefabId).Name);
        Assert.Equal("Wheel", _scene.Children[0].Name);

        _project.History.Undo();

        Assert.Equal("Wheel", Prefab(prefabId).Name);
    }

    [Fact]
    public void TwoPrefabsNeverShareAName()
    {
        _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(2, 2, 2)));

        Assert.Equal("Wheel", _project.Prefabs[0].Name);
        Assert.Equal("Wheel 2", _project.Prefabs[1].Name);
    }

    /// <summary>
    /// Creation composes a free name — there the name is a default — but a rename is
    /// the user's explicit choice of words, and quietly changing it would be worse
    /// than refusing.
    /// </summary>
    [Fact]
    public void RenamingOntoATakenNameIsRefused()
    {
        Guid first = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        Guid second = _scene.CreatePrefab(CreateModel("Axle", new Vector3I(2, 2, 2)));

        Assert.Throws<InvalidOperationException>(() => _project.RenamePrefab(second, "Wheel"));
        Assert.Equal("Axle", Prefab(second).Name);

        // Renaming to its own name is a no-op, not a collision with itself.
        _project.RenamePrefab(first, "Wheel");

        Assert.Equal("Wheel", Prefab(first).Name);
    }

    /// <summary>
    /// A material worn only by cells inside a prefab is still in use: calling it
    /// free would delete it out from under the next instance of that prefab.
    /// </summary>
    [Fact]
    public void AMaterialUsedOnlyInsideAPrefabCountsAsInUse()
    {
        Guid paint = _project.CreateMaterial("Paint", new Color(1f, 0f, 0f));
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Wheel");

        _scene.SetVoxels(model, new[] { new VoxelEdit(Vector3I.Zero, paint) });
        _scene.CreatePrefab(model);
        _scene.DeleteElement(_scene.Children[0].Id);

        Assert.Empty(_scene.Children);
        Assert.True(_project.IsMaterialInUse(paint));
        Assert.Throws<InvalidOperationException>(() => _project.DeleteMaterial(paint));
    }

    [Fact]
    public void CopyingAnInstanceMakesASecondInstanceOfTheSameSource()
    {
        Guid prefabId = _scene.CreatePrefab(CreateModel("Wheel", new Vector3I(1, 1, 1)));
        Guid instance = _scene.Children[0].Id;

        Guid pasted = _scene.PasteElement(_scene.Id, _scene.CaptureElement(instance));

        Assert.Equal(ComponentKind.PrefabInstance, _scene.Element(pasted).ComponentKind);
        Assert.Equal(prefabId, SourceOf(_scene.Element(pasted)));
        Assert.Single(_project.Prefabs);
        Assert.Single(_scene.Element(pasted).Children);
    }

    /// <summary>
    /// Pasting into a document that has never heard of the prefab rebuilds the
    /// library entry, the way a paste already rebuilds a material the target has
    /// lost. The closure travels: half a library expands into half a model.
    /// </summary>
    [Fact]
    public void PastingIntoAnotherDocumentBringsThePrefabWithIt()
    {
        Guid innerId = _scene.CreatePrefab(CreateModel("Bolt", new Vector3I(1, 1, 1)));
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Bracket");

        _scene.ReparentElement(_scene.Children[0].Id, group, 0);
        _scene.CreatePrefab(group);

        ElementClip clip = _scene.CaptureElement(_scene.Children[0].Id);

        IProject other = ModelFactory.CreateProject();
        IScene target = other.Scenes[0];

        Guid pasted = target.PasteElement(target.Id, clip);

        Assert.Equal(2, other.Prefabs.Count);
        Assert.Equal(ComponentKind.PrefabInstance, target.Element(pasted).ComponentKind);

        // The ids are the target document's own, not the source document's.
        Assert.DoesNotContain(other.Prefabs, prefab => prefab.Id == innerId);

        IHierarchyElement bolt = target.Element(pasted).Children[0].Children[0].Children[0];

        Assert.Equal("Bolt", bolt.Name);
        Assert.Single(Voxels(bolt));
    }

    private Guid CreateModel(string name, Vector3I cell)
    {
        Guid id = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, name);

        _scene.SetVoxels(id, new[] { new VoxelEdit(cell, _material) });

        return id;
    }

    private IPrefab Prefab(Guid id)
    {
        Assert.True(_project.TryGetPrefab(id, out IPrefab prefab), $"No prefab {id}.");

        return prefab;
    }

    private static Guid SourceOf(IHierarchyElement element) =>
        Assert.IsAssignableFrom<IPrefabInstance>(element.Component).SourceId;

    private static IReadOnlyDictionary<Vector3I, Guid> Voxels(IHierarchyElement element) =>
        Assert.IsAssignableFrom<IVoxelModel>(element.Component).Voxels;
}
