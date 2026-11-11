using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Prefabs through the panels: rows in the library, opening one for editing, the
/// inspector's three buttons, and the one deletion in the tree that asks first.
/// </summary>
public sealed class PrefabPresenterTests : IDisposable
{
    private static readonly SelectionTarget PrefabsCategory =
        new(SelectionKind.PrefabsCategory, Guid.Empty);

    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakeSceneView _view = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly List<Guid> _activatedList = new();

    private DocumentPresenter? _document;

    public PrefabPresenterTests() => _scene = _project.Scenes[0];

    public void Dispose() => _document?.Dispose();

    [Fact]
    public void TheLibraryHasARowPerPrefab()
    {
        Guid prefabId = MakePrefab("Wheel");
        Build(_scene);

        Assert.Equal("Wheel", _hierarchy.NameOf(Prefab(prefabId)));
        Assert.Equal(PrefabsCategory, _hierarchy.ParentOf(Prefab(prefabId)));

        // Not open, so it shows no contents — the same bargain a scene row makes.
        Assert.Empty(_hierarchy.ChildrenOf(Prefab(prefabId)));
    }

    [Fact]
    public void ActivatingAPrefabRowAsksForItToBeOpened()
    {
        Guid prefabId = MakePrefab("Wheel");
        Build(_scene);

        _hierarchy.RaiseItemActivated(Prefab(prefabId));

        Assert.Equal(new[] { prefabId }, _activatedList);
    }

    /// <summary>
    /// Editing a prefab is the panel's ordinary "this is the one being worked on"
    /// state, pointed at a library entry instead of a scene.
    /// </summary>
    [Fact]
    public void AnOpenPrefabIsTheEmphasizedRowAndShowsItsContents()
    {
        Guid prefabId = MakePrefab("Wheel");
        _project.TryGetPrefab(prefabId, out IPrefab prefab);

        Build(prefab.Content);

        Assert.Equal(Prefab(prefabId), _hierarchy.Emphasized);
        Assert.Single(_hierarchy.ChildrenOf(Prefab(prefabId)));
        Assert.Equal("Wheel", _hierarchy.NameOf(_hierarchy.ChildrenOf(Prefab(prefabId))[0]));

        // And the scene the user came from is a row to go back to, nothing more.
        Assert.Empty(_hierarchy.ChildrenOf(new SelectionTarget(SelectionKind.Scene, _scene.Id)));
    }

    /// <summary>
    /// The absence is the marking: every other element row carries a bin, so a row
    /// without one is a row that stands for something derived.
    /// </summary>
    [Fact]
    public void AnInstancesContentsCarryNoBin()
    {
        MakePrefab("Wheel");
        Build(_scene);

        SelectionTarget instance = Element(_scene.Children[0].Id);
        SelectionTarget content = _hierarchy.ChildrenOf(instance)[0];

        Assert.True(_hierarchy.HasBin(instance));
        Assert.False(_hierarchy.HasBin(content));
        Assert.False(_hierarchy.AskCanDelete(content));
        Assert.False(_hierarchy.AskCanDrag(content));
    }

    [Fact]
    public void TheInspectorOffersToMakeAPrefabOfAnOrdinaryElement()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Wheel");
        Build(_scene);

        _session.SetSelection(Element(element));

        Assert.True(_inspector.PrefabShown);
        Assert.Null(_inspector.PrefabSource);
    }

    [Fact]
    public void TheInspectorNamesWhatAnInstanceStandsFor()
    {
        MakePrefab("Wheel");
        Build(_scene);

        _session.SetSelection(Element(_scene.Children[0].Id));

        Assert.True(_inspector.PrefabShown);
        Assert.Equal("Wheel", _inspector.PrefabSource);
    }

    /// <summary>Neither reading applies to derived content, so the section stays away.</summary>
    [Fact]
    public void TheInspectorShowsNoPrefabSectionForContents()
    {
        MakePrefab("Wheel");
        Build(_scene);

        _session.SetSelection(_hierarchy.ChildrenOf(Element(_scene.Children[0].Id))[0]);

        Assert.False(_inspector.PrefabShown);
    }

    /// <summary>
    /// The element the user was looking at is gone afterwards — an instance stands
    /// in its place — so the selection has to move onto it rather than to nothing.
    /// </summary>
    [Fact]
    public void MakingAPrefabSelectsTheInstanceLeftBehind()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Wheel");
        Build(_scene);

        _session.SetSelection(Element(element));
        _inspector.RaiseMakePrefabRequested();

        Assert.Single(_project.Prefabs);
        Assert.Single(_scene.Children);
        Assert.Equal(ComponentKind.PrefabInstance, _scene.Children[0].ComponentKind);
        Assert.Equal(Element(_scene.Children[0].Id), _session.ActiveTarget);
        Assert.NotEqual(element, _scene.Children[0].Id);
    }

    [Fact]
    public void TheUnpackButtonUnpacks()
    {
        MakePrefab("Wheel");
        Build(_scene);

        Guid instance = _scene.Children[0].Id;

        _session.SetSelection(Element(instance));
        _inspector.RaiseUnpackRequested();

        Assert.True(_scene.TryGetElement(instance, out IHierarchyElement unpacked));
        Assert.Equal(ComponentKind.None, unpacked.ComponentKind);
        Assert.Equal(ActionKind.UnpackInstance, _project.History.Records[^1].Kind);
    }

    [Fact]
    public void TheOpenPrefabButtonAsksForThePrefabToBeOpened()
    {
        Guid prefabId = MakePrefab("Wheel");
        Build(_scene);

        _session.SetSelection(Element(_scene.Children[0].Id));
        _inspector.RaiseOpenPrefabRequested();

        Assert.Equal(new[] { prefabId }, _activatedList);
    }

    /// <summary>
    /// A model inside an instance is read-only where it stands, and the panel says
    /// so before anybody types: which prefab it is part of, and no tools or mirrors
    /// that would only fall back or refuse.
    /// </summary>
    [Fact]
    public void AModelInsideAnInstanceSaysWhichPrefabItIsPartOf()
    {
        MakePrefab("Wheel");
        Build(_scene);

        _session.SetSelection(Element(_scene.Children[0].Children[0].Id));

        Assert.Equal("Wheel", _inspector.PrefabContentOf);
        Assert.False(_inspector.PrefabShown);
        Assert.Null(_inspector.Tools);
        Assert.Null(_inspector.Symmetry);
    }

    [Fact]
    public void AnOrdinaryModelIsNotCalledPartOfAPrefab()
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Wheel");
        Build(_scene);

        _session.SetSelection(Element(element));

        Assert.Null(_inspector.PrefabContentOf);
        Assert.True(_inspector.PrefabShown);
    }

    [Fact]
    public void OpenPrefabOnAModelInsideAnInstanceOpensThePrefabItIsPartOf()
    {
        Guid prefabId = MakePrefab("Wheel");
        Build(_scene);

        _session.SetSelection(Element(_scene.Children[0].Children[0].Id));
        _inspector.RaiseOpenPrefabRequested();

        Assert.Equal(new[] { prefabId }, _activatedList);
    }

    /// <summary>Bordered like anything selected, and no handles: nothing can move it.</summary>
    [Fact]
    public void AModelInsideAnInstanceCarriesNoHandles()
    {
        MakePrefab("Wheel");
        Build(_scene);
        Guid content = _scene.Children[0].Children[0].Id;

        _session.SetSelection(Element(content));

        Assert.Equal(new[] { content }, _view.HighlightedElements);
        Assert.Null(_view.HighlightTarget);
    }

    /// <summary>
    /// The only bin in the panel that asks first, and not out of caution about the
    /// prefab: the press reaches scenes the user cannot see from where they stand.
    /// </summary>
    [Fact]
    public void DeletingAPrefabAsksFirstAndACancelLeavesItAlone()
    {
        Guid prefabId = MakePrefab("Wheel");
        Build(_scene);

        _dialogs.Answer = ConfirmResult.Cancel;
        _hierarchy.RaiseDeleteRequested(Prefab(prefabId));

        Assert.Equal(1, _dialogs.ConfirmCount);
        Assert.Single(_project.Prefabs);
        Assert.Equal(ComponentKind.PrefabInstance, _scene.Children[0].ComponentKind);
    }

    [Fact]
    public void DeletingAPrefabGoesThroughOnceItIsConfirmed()
    {
        Guid prefabId = MakePrefab("Wheel");
        Build(_scene);

        _dialogs.Answer = ConfirmResult.Confirm;
        _hierarchy.RaiseDeleteRequested(Prefab(prefabId));

        Assert.Empty(_project.Prefabs);
        Assert.Equal(ComponentKind.None, _scene.Children[0].ComponentKind);
        Assert.Equal(ActionKind.DeletePrefab, _project.History.Records[^1].Kind);
    }

    /// <summary>The question says what it will cost, which is the reason it is asked.</summary>
    [Fact]
    public void TheQuestionSaysTheInstancesWillBeUnpacked()
    {
        Guid prefabId = MakePrefab("Wheel");
        Build(_scene);

        _hierarchy.RaiseDeleteRequested(Prefab(prefabId));

        Assert.Contains("instance", _dialogs.LastConfirmRequest!.Value.Message);

        _scene.DeleteElement(_scene.Children[0].Id);
        _hierarchy.RaiseDeleteRequested(Prefab(prefabId));

        Assert.DoesNotContain("instance", _dialogs.LastConfirmRequest!.Value.Message);
    }

    [Fact]
    public void DroppingAPrefabOntoTheSceneRowPlacesAnInstance()
    {
        Guid prefabId = MakePrefab("Wheel");
        Build(_scene);

        var drop = new HierarchyDrop(
            Prefab(prefabId), new SelectionTarget(SelectionKind.Scene, _scene.Id), 0);

        Assert.True(_hierarchy.AskCanDrag(Prefab(prefabId)));
        Assert.True(_hierarchy.AskCanDrop(drop));

        _hierarchy.RaiseItemDropped(drop);

        Assert.Equal(2, _scene.Children.Count);
        Assert.Equal(ComponentKind.PrefabInstance, _scene.Children[1].ComponentKind);
        Assert.Equal(Element(_scene.Children[1].Id), _session.ActiveTarget);
    }

    /// <summary>
    /// Drawn before the button comes up, and by the model's own rule rather than a
    /// second copy of it.
    /// </summary>
    [Fact]
    public void APrefabCannotBeDroppedIntoItself()
    {
        Guid prefabId = MakePrefab("Wheel");
        _project.TryGetPrefab(prefabId, out IPrefab prefab);

        Build(prefab.Content);

        Assert.False(_hierarchy.AskCanDrop(new HierarchyDrop(
            Prefab(prefabId), Prefab(prefabId), 0)));
    }

    private static SelectionTarget Element(Guid id) => new(SelectionKind.Element, id);

    private static SelectionTarget Prefab(Guid id) => new(SelectionKind.Prefab, id);

    /// <summary>A model with cells in it, lifted into the library.</summary>
    private Guid MakePrefab(string name)
    {
        Guid element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, name);

        _scene.SetVoxels(element, new[]
        {
            new VoxelEdit(Godot.Vector3I.Zero, _project.Materials[0].Id)
        });

        return _scene.CreatePrefab(element);
    }

    private void Build(IScene scene)
    {
        _session.CurrentMaterialId = _project.Materials[0].Id;

        _document = new DocumentPresenter(
            _project,
            scene,
            _hierarchy,
            _inspector,
            _history,
            _view,
            _session,
            _dialogs,
            new FakePreferences(),
            id => _activatedList.Add(id));
    }
}
