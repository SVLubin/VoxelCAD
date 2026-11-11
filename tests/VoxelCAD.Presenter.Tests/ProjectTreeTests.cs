using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The panel shows the project, not one scene: the scenes, the prefabs and the
/// materials, with the element tree living inside the one scene being worked on.
/// </summary>
public sealed class ProjectTreeTests : IDisposable
{
    private static readonly SelectionTarget Root = new(SelectionKind.Project, Guid.Empty);
    private static readonly SelectionTarget Scenes = new(SelectionKind.ScenesCategory, Guid.Empty);
    private static readonly SelectionTarget Prefabs = new(SelectionKind.PrefabsCategory, Guid.Empty);
    private static readonly SelectionTarget Materials = new(SelectionKind.MaterialsCategory, Guid.Empty);

    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    public ProjectTreeTests() => _shell = new ShellPresenter(_view, new FakePreferences());

    public void Dispose() => _shell.Dispose();

    private FakeHierarchyView Tree => _view.HierarchyView;

    [Fact]
    public void TheProjectIsTheRootAndTheCategoriesAreUnderIt()
    {
        Assert.Equal(Root, Tree.Root);
        Assert.Equal(new[] { Scenes, Prefabs, Materials }, Tree.ChildrenOf(Root));
        Assert.Equal("Project", Tree.NameOf(Root));
    }

    [Fact]
    public void ScenesAndMaterialsSitUnderTheirOwnCategories()
    {
        Assert.Equal(new[] { ActiveScene }, Tree.ChildrenOf(Scenes));
        Assert.Single(Tree.ChildrenOf(Materials));
        Assert.Empty(Tree.ChildrenOf(Prefabs));
        Assert.Equal("Default", Tree.NameOf(Tree.ChildrenOf(Materials)[0]));
    }

    /// <summary>A material is largely what it looks like, so the row carries it.</summary>
    [Fact]
    public void AMaterialRowCarriesItsColour()
    {
        SelectionTarget material = Tree.ChildrenOf(Materials)[0];

        Assert.True(Tree.Swatches.ContainsKey(material));

        _view.HierarchyView.RaiseItemClicked(material);
        _view.InspectorView.RaiseColorEdited(new Color(0.2f, 0.4f, 0.6f));

        Assert.Equal(new Color(0.2f, 0.4f, 0.6f), Tree.Swatches[material]);
    }

    [Fact]
    public void ElementsSitUnderTheSceneRowRatherThanTheRoot()
    {
        _view.RaiseCreateGroup();

        Assert.Equal(new[] { Tree.Selected }, Tree.ChildrenOf(ActiveScene));
    }

    /// <summary>
    /// The target's kind alone cannot tell a grouping row from a voxel model, so
    /// the panel is told what sits in the component slot — it is what the row's
    /// icon is chosen from.
    /// </summary>
    [Fact]
    public void AnElementRowSaysWhatItsComponentIs()
    {
        _view.RaiseCreateGroup();
        SelectionTarget group = Tree.Selected;

        _view.RaiseCreate(CreateKind.VoxelModel);
        SelectionTarget model = Tree.Selected;

        Assert.Equal(ComponentKind.None, Tree.ComponentOf(group));
        Assert.Equal(ComponentKind.VoxelModel, Tree.ComponentOf(model));
    }

    /// <summary>
    /// The constructor fills the tree in bulk and does not chase the last row —
    /// a freshly opened project shows its top — but a live add brings the newest
    /// element into view so the user is not left scrolling for what they made.
    /// </summary>
    /// <remarks>
    /// Twice, and deliberately: a create is two things happening — a row appearing
    /// and that row becoming the selection — and each brings a row into view on its
    /// own terms. They agree here, and they do not always: an agent's create
    /// selects nothing, and a click in the viewport adds no row.
    /// </remarks>
    [Fact]
    public void CreatingAnElementScrollsToItWhileTheInitialFillDoesNot()
    {
        Assert.Equal(0, Tree.ScrollCount);

        _view.RaiseCreateGroup();

        Assert.Equal(Tree.Selected, Tree.ScrolledTo);
        Assert.Equal(2, Tree.ScrollCount);
    }

    [Fact]
    public void CreatingAMaterialScrollsToItsRow()
    {
        _view.RaiseCreate(CreateKind.Material);

        Assert.Equal(Tree.Selected, Tree.ScrolledTo);
    }

    /// <summary>
    /// Only the active scene shows its contents. That is what tells the two kinds
    /// of scene row apart, and it is why activating one rebuilds the panel.
    /// </summary>
    [Fact]
    public void AnInactiveSceneShowsNoContents()
    {
        _view.RaiseCreateGroup();
        SelectionTarget first = ActiveScene;

        _view.RaiseCreate(CreateKind.Scene);

        Assert.NotEqual(first, ActiveScene);
        Assert.Empty(Tree.ChildrenOf(first));
        Assert.Equal(2, Tree.ChildrenOf(Scenes).Count);
    }

    [Fact]
    public void ActivatingASceneBringsItsContentsUp()
    {
        _view.RaiseCreateGroup();
        SelectionTarget first = ActiveScene;

        _view.RaiseCreate(CreateKind.Scene);
        SelectionTarget second = ActiveScene;

        Tree.RaiseItemActivated(first);

        Assert.Equal(first, ActiveScene);
        Assert.Single(Tree.ChildrenOf(first));
        Assert.Empty(Tree.ChildrenOf(second));
    }

    /// <summary>
    /// Activating is one gesture across the tree: it makes a row the current one
    /// of its kind. For a material that means the brush paints with it.
    /// </summary>
    [Fact]
    public void ActivatingAMaterialMakesItWhatTheBrushPaintsWith()
    {
        _view.RaiseCreate(CreateKind.Material);
        SelectionTarget created = Tree.ChildrenOf(Materials)[1];

        Assert.NotEqual(created.Id, _view.SceneView.BrushMaterialId);

        Tree.RaiseItemActivated(created);

        Assert.Equal(created.Id, _view.SceneView.BrushMaterialId);
    }

    [Fact]
    public void ANewSceneIsNamedApartFromTheOneBeforeIt()
    {
        _view.RaiseCreate(CreateKind.Scene);

        Assert.Equal(
            new[] { "Scene", "Scene 2" },
            Tree.ChildrenOf(Scenes).Select(scene => Tree.NameOf(scene)));
    }

    [Fact]
    public void ANewMaterialIsSelectedAndInspectable()
    {
        _view.RaiseCreate(CreateKind.Material);

        Assert.Equal(SelectionKind.Material, Tree.Selected.Kind);
        Assert.Equal("Material", _view.InspectorView.TargetName);
        Assert.NotNull(_view.InspectorView.MaterialColor);
    }

    [Fact]
    public void EditingAMaterialsColourRepaintsTheViewport()
    {
        SelectionTarget material = Tree.ChildrenOf(Materials)[0];

        Tree.RaiseItemClicked(material);
        _view.InspectorView.RaiseColorEdited(new Color(1f, 0f, 0f));

        Assert.Equal(new Color(1f, 0f, 0f), _view.SceneView.Materials[material.Id]);
    }

    /// <summary>
    /// Elements are made in the scene being worked on, so the buttons go quiet
    /// where there is nowhere to put one.
    /// </summary>
    [Fact]
    public void TheElementButtonsFollowWhatIsSelected()
    {
        Assert.True(_view.IsCreateEnabled(CreateKind.Group));

        Tree.RaiseItemClicked(Tree.ChildrenOf(Materials)[0]);
        Assert.False(_view.IsCreateEnabled(CreateKind.Group));
        Assert.False(_view.IsCreateEnabled(CreateKind.VoxelModel));

        Tree.RaiseItemClicked(ActiveScene);
        Assert.True(_view.IsCreateEnabled(CreateKind.Group));
    }

    [Fact]
    public void ASceneThatIsNotBeingWorkedOnIsNoPlaceForAnElement()
    {
        SelectionTarget first = ActiveScene;
        _view.RaiseCreate(CreateKind.Scene);

        Tree.RaiseItemClicked(first);

        Assert.False(_view.IsCreateEnabled(CreateKind.Group));
    }

    [Fact]
    public void TheLastMaterialCannotBeDeletedAndAnUnusedOneCan()
    {
        Tree.RaiseItemClicked(Tree.ChildrenOf(Materials)[0]);
        Assert.False(_view.DeleteEnabled);

        _view.RaiseCreate(CreateKind.Material);
        Assert.True(_view.DeleteEnabled);

        _view.RaiseDeleteSelection();

        Assert.Single(Tree.ChildrenOf(Materials));
        Assert.Equal(default, Tree.Selected);
    }

    /// <summary>
    /// The model refuses to strand voxels on a material that no longer exists.
    /// That refusal is an answer for the user, not an exception.
    /// </summary>
    [Fact]
    public void DeletingAMaterialInUseIsReportedRatherThanThrown()
    {
        _view.RaiseCreateVoxelModel();

        // A material is only in use once something is painted with it, and the
        // editor opens on the cursor — so the brush is asked for first.
        _view.SceneView.RaiseToolRequested(ToolKind.Paint);
        _view.SceneView.RaisePicked(new ScenePick(Vector3I.Zero, Vector3I.Up, false, false));

        Tree.RaiseItemClicked(new SelectionTarget(
            SelectionKind.Material,
            _view.SceneView.BrushMaterialId));

        _view.RaiseCreate(CreateKind.Material);
        Tree.RaiseItemClicked(new SelectionTarget(
            SelectionKind.Material,
            _view.SceneView.BrushMaterialId));

        _view.RaiseDeleteSelection();

        (string Title, string Message) error = Assert.Single(_view.Errors);
        Assert.Equal("Cannot delete", error.Title);
        Assert.Equal(2, Tree.ChildrenOf(Materials).Count);

        // The card is read by a person, who knows the material by its name — an id
        // in that sentence is a value nobody on that side of the screen can use.
        Assert.Contains("'Default'", error.Message);
        Assert.DoesNotContain(_view.SceneView.BrushMaterialId.ToString(), error.Message);
    }

    [Fact]
    public void DeletingTheActiveSceneFallsBackToAnother()
    {
        SelectionTarget first = ActiveScene;
        _view.RaiseCreate(CreateKind.Scene);

        _view.RaiseDeleteSelection();

        Assert.Equal(first, ActiveScene);
        Assert.Single(Tree.ChildrenOf(Scenes));
    }

    /// <summary>
    /// Undo reverses the last thing that happened, whichever scene it happened
    /// in — so the editor goes there rather than appearing to do nothing.
    /// </summary>
    [Fact]
    public void UndoBringsUpTheSceneTheActionBelongsTo()
    {
        _view.RaiseCreateGroup();
        SelectionTarget first = ActiveScene;

        _view.RaiseCreate(CreateKind.Scene);
        SelectionTarget second = ActiveScene;
        _view.RaiseCreateGroup();

        // Back past the group in the second scene, then past the scene itself,
        // and finally the group that was made in the first.
        _view.RaiseUndo();
        _view.RaiseUndo();

        Assert.Equal(first, ActiveScene);

        _view.RaiseUndo();

        Assert.Equal(first, ActiveScene);
        Assert.Empty(Tree.ChildrenOf(first));
        Assert.DoesNotContain(second, Tree.ChildrenOf(Scenes));
    }

    [Fact]
    public void RedoingASceneBringsItBackUp()
    {
        SelectionTarget first = ActiveScene;

        _view.RaiseCreate(CreateKind.Scene);
        SelectionTarget second = ActiveScene;

        _view.RaiseUndo();
        Assert.Equal(first, ActiveScene);

        _view.RaiseRedo();

        Assert.Equal(second, ActiveScene);
    }

    private SelectionTarget ActiveScene => Tree.Emphasized;
}
