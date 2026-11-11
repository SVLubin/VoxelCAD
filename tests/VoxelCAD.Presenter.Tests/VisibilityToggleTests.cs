using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The eye at the right edge of a project-tree row: which rows carry one, what a
/// click on it does to the document, and how the viewport follows. What the glyph
/// looks like and when it is drawn is the panel's own and lives in a Godot node —
/// the decisions are all here.
/// </summary>
public sealed class VisibilityToggleTests : IDisposable
{
    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private DocumentPresenter? _document;

    public VisibilityToggleTests() => _scene = _project.Scenes[0];

    public void Dispose() => _document?.Dispose();

    /// <summary>
    /// Only rows that stand for something hideable. A scene is the room, a
    /// material is not somewhere geometry lives, and the categories are the
    /// panel's own furniture.
    /// </summary>
    [Fact]
    public void OnlyElementRowsCarryAnEye()
    {
        Guid element = CreateElement("Model");
        Build();

        Assert.True(_hierarchy.HasEye(Element(element)));
        Assert.False(_hierarchy.HasEye(new SelectionTarget(SelectionKind.Scene, _scene.Id)));
        Assert.False(_hierarchy.HasEye(
            new SelectionTarget(SelectionKind.Material, _project.Materials[0].Id)));
        Assert.False(_hierarchy.HasEye(
            new SelectionTarget(SelectionKind.MaterialsCategory, Guid.Empty)));
    }

    [Fact]
    public void ARowShowsWhatTheDocumentSays()
    {
        Guid shown = CreateElement("Shown");
        Guid hidden = CreateElement("Hidden");

        _scene.SetVisible(hidden, false);
        Build();

        Assert.True(_hierarchy.IsVisible(Element(shown)));
        Assert.False(_hierarchy.IsVisible(Element(hidden)));
    }

    [Fact]
    public void ClickingTheEyeHidesTheElement()
    {
        Guid element = CreateElement("Model");
        Build();

        _hierarchy.RaiseVisibilityToggled(Element(element));

        Assert.False(IsVisible(element));
        Assert.Equal(ActionKind.ChangeVisibility, _project.History.Records[^1].Kind);
    }

    /// <summary>
    /// The panel reports the row, not a new state, so the second click has to
    /// read the document again rather than invert what it drew.
    /// </summary>
    [Fact]
    public void ClickingItAgainShowsIt()
    {
        Guid element = CreateElement("Model");
        Build();

        _hierarchy.RaiseVisibilityToggled(Element(element));
        _hierarchy.RaiseVisibilityToggled(Element(element));

        Assert.True(IsVisible(element));
        Assert.True(_hierarchy.IsVisible(Element(element)));
    }

    [Fact]
    public void TheRowFollowsAChangeItDidNotMake()
    {
        Guid element = CreateElement("Model");
        Build();

        _scene.SetVisible(element, false);

        Assert.False(_hierarchy.IsVisible(Element(element)));
    }

    [Fact]
    public void UndoBringsTheEyeBack()
    {
        Guid element = CreateElement("Model");
        Build();

        _hierarchy.RaiseVisibilityToggled(Element(element));
        _project.History.Undo();

        Assert.True(_hierarchy.IsVisible(Element(element)));
    }

    [Fact]
    public void TheViewportIsToldAtOnce()
    {
        Guid element = CreateElement("Model");
        Build();

        Assert.True(_view.IsVisible(element));

        _scene.SetVisible(element, false);

        Assert.False(_view.IsVisible(element));
    }

    /// <summary>
    /// The nodes are the tree, so the viewport is told about the group alone and
    /// the engine takes the subtree off screen — the same composition the
    /// exporter walks by hand.
    /// </summary>
    [Fact]
    public void OnlyTheHiddenElementIsPushedToTheViewport()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid child = _scene.CreateElement(group, ComponentKind.VoxelModel, "Child");
        Build();

        _scene.SetVisible(group, false);

        Assert.False(_view.IsVisible(group));
        Assert.True(_view.IsVisible(child));
    }

    /// <summary>
    /// The panel is the other way round: a row under a hidden one has to recede
    /// too, because it reaches nothing either — while its own eye still reads as
    /// shown, which is what its flag says and what clicking would change.
    /// </summary>
    [Fact]
    public void ARowUnderAHiddenOneRecedesButKeepsItsOwnEye()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid child = _scene.CreateElement(group, ComponentKind.VoxelModel, "Child");
        Guid grandchild = _scene.CreateElement(child, ComponentKind.VoxelModel, "Grandchild");
        Build();

        _scene.SetVisible(group, false);

        Assert.Equal(RowVisibility.Hidden, _hierarchy.VisibilityOf(Element(group)));
        Assert.Equal(RowVisibility.HiddenByAncestor, _hierarchy.VisibilityOf(Element(child)));
        Assert.Equal(RowVisibility.HiddenByAncestor, _hierarchy.VisibilityOf(Element(grandchild)));
    }

    /// <summary>
    /// Showing the group again lifts the whole subtree back — nothing was written
    /// to the children, so there is nothing to put back but the drawing.
    /// </summary>
    [Fact]
    public void ShowingTheGroupBringsTheSubtreeBack()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid child = _scene.CreateElement(group, ComponentKind.VoxelModel, "Child");
        Build();

        _scene.SetVisible(group, false);
        _scene.SetVisible(group, true);

        Assert.Equal(RowVisibility.Shown, _hierarchy.VisibilityOf(Element(group)));
        Assert.Equal(RowVisibility.Shown, _hierarchy.VisibilityOf(Element(child)));
    }

    /// <summary>
    /// A child hidden in its own right stays hidden — its own flag first, so its
    /// eye goes on saying what only its eye can change.
    /// </summary>
    [Fact]
    public void ItsOwnFlagOutranksItsAncestors()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid child = _scene.CreateElement(group, ComponentKind.VoxelModel, "Child");
        Build();

        _scene.SetVisible(child, false);
        _scene.SetVisible(group, false);

        Assert.Equal(RowVisibility.Hidden, _hierarchy.VisibilityOf(Element(child)));

        _scene.SetVisible(group, true);

        Assert.Equal(RowVisibility.Hidden, _hierarchy.VisibilityOf(Element(child)));
    }

    /// <summary>
    /// A row built under a hidden parent has to arrive receded, not only rows that
    /// were on screen when the parent was hidden.
    /// </summary>
    [Fact]
    public void ANewRowUnderAHiddenParentArrivesReceded()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Build();

        _scene.SetVisible(group, false);

        Guid child = _scene.CreateElement(group, ComponentKind.VoxelModel, "Child");

        Assert.Equal(RowVisibility.HiddenByAncestor, _hierarchy.VisibilityOf(Element(child)));
    }

    /// <summary>
    /// Dragged out from under a hidden group, a row is drawn again — the reparent
    /// rebuilds it, and what it says about visibility is rebuilt with it.
    /// </summary>
    [Fact]
    public void AReparentRedressesTheMovedSubtree()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid child = _scene.CreateElement(group, ComponentKind.VoxelModel, "Child");
        Guid grandchild = _scene.CreateElement(child, ComponentKind.VoxelModel, "Grandchild");
        Build();

        _scene.SetVisible(group, false);
        _scene.ReparentElement(child, _scene.Id, 0);

        Assert.Equal(RowVisibility.Shown, _hierarchy.VisibilityOf(Element(child)));
        Assert.Equal(RowVisibility.Shown, _hierarchy.VisibilityOf(Element(grandchild)));
    }

    /// <summary>
    /// A hidden element is still shown as hidden when the panel is rebuilt around
    /// it — the flag is the document's, not the row's.
    /// </summary>
    [Fact]
    public void ANewRowForAHiddenElementSaysSo()
    {
        Guid element = CreateElement("Model");
        Build();

        _scene.SetVisible(element, false);

        // A reparent rebuilds the row: Godot cannot move a tree item, so the
        // panel is told to remove it and add it again.
        _scene.ReparentElement(element, _scene.Id, 0);

        Assert.False(_hierarchy.IsVisible(Element(element)));
    }

    /// <summary>
    /// Reported a frame later, so the element may be gone by the time the click
    /// is answered.
    /// </summary>
    [Fact]
    public void AStaleClickIsIgnored()
    {
        Guid element = CreateElement("Model");
        Build();

        _scene.DeleteElement(element);
        int recordsBefore = _project.History.Records.Count;

        _hierarchy.RaiseVisibilityToggled(Element(element));

        Assert.Equal(recordsBefore, _project.History.Records.Count);
    }

    private static SelectionTarget Element(Guid id) => new(SelectionKind.Element, id);

    private Guid CreateElement(string name) =>
        _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, name);

    private bool IsVisible(Guid id) =>
        _scene.TryGetElement(id, out IHierarchyElement element) && element.Visible;

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
