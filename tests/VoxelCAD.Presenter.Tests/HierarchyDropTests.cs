using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Dragging a row onto another in the project tree. What the panel may accept, and
/// what the document does with it — the world position stays put, which is the
/// whole reason a reparent is a reparent and not a move.
/// </summary>
public sealed class HierarchyDropTests : IDisposable
{
    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private DocumentPresenter? _document;

    public HierarchyDropTests() => _scene = _project.Scenes[0];

    public void Dispose() => _document?.Dispose();

    [Fact]
    public void AnElementDroppedOnAnotherBecomesItsChild()
    {
        Guid parent = CreateElement("Parent");
        Guid child = CreateElement("Child");
        Build();

        Drop(child, Element(parent), index: 0);

        Assert.True(_scene.TryGetElement(child, out IHierarchyElement moved));
        Assert.Equal(parent, moved.Parent.Id);
        Assert.Equal(ActionKind.ReparentElement, _project.History.Records[^1].Kind);
    }

    /// <summary>
    /// Unity's rule, and the model's: the element does not budge in the scene, the
    /// numbers under it change to say the same thing in the new frame.
    /// </summary>
    [Fact]
    public void TheWorldPositionSurvivesTheDrop()
    {
        Guid parent = CreateElement("Parent");
        Guid child = CreateElement("Child");

        _scene.SetTransform(parent, TransformChannel.Position, new Vector3(4, 0, 0));
        _scene.SetTransform(child, TransformChannel.Position, new Vector3(7, 0, 0));
        Build();

        Drop(child, Element(parent), index: 0);

        Assert.True(_scene.TryGetElement(child, out IHierarchyElement moved));
        Assert.Equal(new Vector3(3, 0, 0), moved.Transform.Position);
    }

    /// <summary>The scene's own row is the tree's way of saying "top level".</summary>
    [Fact]
    public void DroppingOnTheSceneRowMovesItToTheTop()
    {
        Guid parent = CreateElement("Parent");
        Guid child = _scene.CreateElement(parent, ComponentKind.VoxelModel, "Child");
        Build();

        Drop(child, new SelectionTarget(SelectionKind.Scene, _scene.Id), index: 0);

        Assert.True(_scene.TryGetElement(child, out IHierarchyElement moved));
        Assert.Equal(_scene.Id, moved.Parent.Id);
    }

    [Fact]
    public void AnElementIsRefusedInsideItsOwnSubtree()
    {
        Guid parent = CreateElement("Parent");
        Guid child = _scene.CreateElement(parent, ComponentKind.VoxelModel, "Child");
        Build();

        Assert.False(_hierarchy.AskCanDrop(
            new HierarchyDrop(Element(parent), Element(child), 0)));

        Assert.False(_hierarchy.AskCanDrop(
            new HierarchyDrop(Element(parent), Element(parent), 0)));
    }

    [Fact]
    public void MaterialsAndCategoriesAreNotPlacesToLive()
    {
        Guid element = CreateElement("Element");
        Build();

        Assert.False(_hierarchy.AskCanDrop(new HierarchyDrop(
            Element(element),
            new SelectionTarget(SelectionKind.Material, _project.Materials[0].Id),
            0)));

        Assert.False(_hierarchy.AskCanDrop(new HierarchyDrop(
            Element(element),
            new SelectionTarget(SelectionKind.MaterialsCategory, Guid.Empty),
            0)));
    }

    [Fact]
    public void OnlyAnElementCanBeDragged()
    {
        Guid element = CreateElement("Element");
        Build();

        Assert.False(_hierarchy.AskCanDrop(new HierarchyDrop(
            new SelectionTarget(SelectionKind.Material, _project.Materials[0].Id),
            Element(element),
            0)));
    }

    /// <summary>
    /// Asked before the drag even starts, so a material never rides the pointer
    /// under a forbidden cursor to a drop that could never land.
    /// </summary>
    [Fact]
    public void OnlyAnElementStartsADragAtAll()
    {
        Guid element = CreateElement("Element");
        Build();

        Assert.True(_hierarchy.AskCanDrag(Element(element)));

        Assert.False(_hierarchy.AskCanDrag(
            new SelectionTarget(SelectionKind.Material, _project.Materials[0].Id)));
        Assert.False(_hierarchy.AskCanDrag(
            new SelectionTarget(SelectionKind.Scene, _scene.Id)));
        Assert.False(_hierarchy.AskCanDrag(
            new SelectionTarget(SelectionKind.ScenesCategory, Guid.Empty)));
        Assert.False(_hierarchy.AskCanDrag(
            new SelectionTarget(SelectionKind.Element, Guid.NewGuid())));
    }

    /// <summary>
    /// Another scene's row is a row to activate, not a place to drop into: its
    /// elements are not even in the tree.
    /// </summary>
    [Fact]
    public void AnotherSceneIsNotAPlaceToLive()
    {
        Guid element = CreateElement("Element");
        Guid other = _project.CreateScene("Other", new Vector3I(8, 8, 8));
        Build();

        Assert.False(_hierarchy.AskCanDrop(new HierarchyDrop(
            Element(element), new SelectionTarget(SelectionKind.Scene, other), 0)));
    }

    [Fact]
    public void ReorderingInsideOneParentPutsTheRowWhereItWasDropped()
    {
        Guid first = CreateElement("First");
        CreateElement("Second");
        CreateElement("Third");
        Build();

        // Dropped below the third row: index 3 in the tree as it looks now.
        Drop(first, new SelectionTarget(SelectionKind.Scene, _scene.Id), index: 3);

        Assert.Equal(
            new[] { "Second", "Third", "First" },
            NamesOfChildren());
    }

    [Fact]
    public void ARowDroppedHigherLandsWhereItWasDropped()
    {
        CreateElement("First");
        CreateElement("Second");
        Guid third = CreateElement("Third");
        Build();

        // Above the first row.
        Drop(third, new SelectionTarget(SelectionKind.Scene, _scene.Id), index: 0);

        Assert.Equal(
            new[] { "Third", "First", "Second" },
            NamesOfChildren());
    }

    /// <summary>
    /// The panel reports a drop a frame later, so by then the row may be gone — a
    /// stale report must not throw or move something else.
    /// </summary>
    [Fact]
    public void AStaleDropIsIgnored()
    {
        Guid parent = CreateElement("Parent");
        Guid child = CreateElement("Child");
        Build();

        _scene.DeleteElement(child);
        int recordsBefore = _project.History.Records.Count;

        Drop(child, Element(parent), index: 0);

        Assert.Equal(recordsBefore, _project.History.Records.Count);
    }

    private static SelectionTarget Element(Guid id) => new(SelectionKind.Element, id);

    private Guid CreateElement(string name) =>
        _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, name);

    private void Drop(Guid target, SelectionTarget parent, int index) =>
        _hierarchy.RaiseItemDropped(new HierarchyDrop(Element(target), parent, index));

    private string[] NamesOfChildren()
    {
        var names = new string[_scene.Children.Count];

        for (var index = 0; index < names.Length; index++)
            names[index] = _scene.Children[index].Name;

        return names;
    }

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
