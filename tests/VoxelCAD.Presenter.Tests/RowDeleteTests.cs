using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The bin at the right edge of a project-tree row: which rows carry one, which
/// of them it is live on, and what pressing it does. It acts at once and asks
/// nothing — undo is what a misclick costs.
/// </summary>
public sealed class RowDeleteTests : IDisposable
{
    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private DocumentPresenter? _document;

    public RowDeleteTests() => _scene = _project.Scenes[0];

    public void Dispose() => _document?.Dispose();

    /// <summary>
    /// Everything the document holds, and nothing the panel invented: the project
    /// row and the three category rows stand for nothing that could go.
    /// </summary>
    [Fact]
    public void ElementsScenesAndMaterialsCarryABinAndNothingElseDoes()
    {
        Guid element = CreateElement("Model");
        Build();

        Assert.True(_hierarchy.HasBin(Element(element)));
        Assert.True(_hierarchy.HasBin(new SelectionTarget(SelectionKind.Scene, _scene.Id)));
        Assert.True(_hierarchy.HasBin(Material(_project.Materials[0].Id)));

        Assert.False(_hierarchy.HasBin(new SelectionTarget(SelectionKind.Project, Guid.Empty)));
        Assert.False(_hierarchy.HasBin(
            new SelectionTarget(SelectionKind.ScenesCategory, Guid.Empty)));
        Assert.False(_hierarchy.HasBin(
            new SelectionTarget(SelectionKind.MaterialsCategory, Guid.Empty)));
    }

    [Fact]
    public void PressingItDeletesTheElement()
    {
        Guid element = CreateElement("Model");
        Build();

        _hierarchy.RaiseDeleteRequested(Element(element));

        Assert.False(_scene.TryGetElement(element, out _));
        Assert.Equal(ActionKind.DeleteElement, _project.History.Records[^1].Kind);
    }

    /// <summary>No question is asked, and that is the whole bargain: undo answers it.</summary>
    [Fact]
    public void UndoBringsItBack()
    {
        Guid element = CreateElement("Model");
        Build();

        _hierarchy.RaiseDeleteRequested(Element(element));
        _project.History.Undo();

        Assert.True(_scene.TryGetElement(element, out _));
    }

    /// <summary>The subtree goes with it — the model's rule, reached through the row.</summary>
    [Fact]
    public void DeletingAGroupTakesItsChildrenWithIt()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid child = _scene.CreateElement(group, ComponentKind.VoxelModel, "Child");
        Build();

        _hierarchy.RaiseDeleteRequested(Element(group));

        Assert.False(_scene.TryGetElement(child, out _));
    }

    [Fact]
    public void PressingItDeletesAScene()
    {
        Guid other = _project.CreateScene("Other", new Vector3I(8, 8, 8));
        Build();

        _hierarchy.RaiseDeleteRequested(new SelectionTarget(SelectionKind.Scene, other));

        Assert.False(_project.TryGetScene(other, out _));
    }

    [Fact]
    public void PressingItDeletesAMaterial()
    {
        Guid spare = _project.CreateMaterial("Spare", new Color(0.2f, 0.4f, 0.6f));
        Build();

        _hierarchy.RaiseDeleteRequested(Material(spare));

        Assert.False(_project.TryGetMaterial(spare, out _));
    }

    /// <summary>
    /// This panel's own rule, not the model's: <c>delete-scene</c> may still take
    /// the active scene, but a bin that pulls the room out from under the user is
    /// not a thing to offer in one click.
    /// </summary>
    [Fact]
    public void TheSceneBeingWorkedOnRefusesTheBin()
    {
        Guid other = _project.CreateScene("Other", new Vector3I(8, 8, 8));
        Build();

        Assert.False(_hierarchy.AskCanDelete(new SelectionTarget(SelectionKind.Scene, _scene.Id)));
        Assert.True(_hierarchy.AskCanDelete(new SelectionTarget(SelectionKind.Scene, other)));
    }

    /// <summary>And a refused press does nothing, not merely draws greyed.</summary>
    [Fact]
    public void ARefusedPressLeavesTheDocumentAlone()
    {
        Build();

        int recordsBefore = _project.History.Records.Count;

        _hierarchy.RaiseDeleteRequested(new SelectionTarget(SelectionKind.Scene, _scene.Id));

        Assert.True(_project.TryGetScene(_scene.Id, out _));
        Assert.Equal(recordsBefore, _project.History.Records.Count);
    }

    /// <summary>
    /// The model's own refusals, asked rather than restated: a material a voxel
    /// still wears, and the last material in the project.
    /// </summary>
    [Fact]
    public void AMaterialInUseRefusesTheBin()
    {
        Guid spare = _project.CreateMaterial("Spare", new Color(0.2f, 0.4f, 0.6f));
        Guid element = CreateElement("Model");
        Build();

        Assert.True(_hierarchy.AskCanDelete(Material(spare)));

        _scene.SetVoxels(element, new[] { new VoxelEdit(Vector3I.Zero, spare) });

        Assert.False(_hierarchy.AskCanDelete(Material(spare)));
    }

    [Fact]
    public void TheLastMaterialRefusesTheBin()
    {
        Build();

        Assert.False(_hierarchy.AskCanDelete(Material(_project.Materials[0].Id)));
    }

    /// <summary>Prefab contents are read-only wherever they appear, the bin included.</summary>
    [Fact]
    public void AnOrdinaryElementAcceptsTheBin()
    {
        Guid element = CreateElement("Model");
        Build();

        Assert.True(_hierarchy.AskCanDelete(Element(element)));
        Assert.False(_hierarchy.AskCanDelete(Element(Guid.NewGuid())));
    }

    /// <summary>
    /// Reported a frame later, so the row may name something already gone by the
    /// time the press is answered.
    /// </summary>
    [Fact]
    public void AStalePressIsIgnored()
    {
        Guid element = CreateElement("Model");
        Build();

        _scene.DeleteElement(element);
        int recordsBefore = _project.History.Records.Count;

        _hierarchy.RaiseDeleteRequested(Element(element));

        Assert.Equal(recordsBefore, _project.History.Records.Count);
    }

    private static SelectionTarget Element(Guid id) => new(SelectionKind.Element, id);

    private static SelectionTarget Material(Guid id) => new(SelectionKind.Material, id);

    private Guid CreateElement(string name) =>
        _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, name);

    private void Build()
    {
        _session.CurrentMaterialId = _project.Materials[0].Id;

        _document = new DocumentPresenter(
            _project,
            _scene,
            _hierarchy,
            _inspector,
            _history,
            _view,
            _session,
            new FakeDialogService(),
            new FakePreferences(),
            _ => { });
    }
}
