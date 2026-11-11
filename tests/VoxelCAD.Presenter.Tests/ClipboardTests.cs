using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Copy, cut, paste and duplicate as the chords reach them: what may be copied,
/// where a paste lands, and what is selected afterwards.
/// </summary>
public sealed class ClipboardTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    public ClipboardTests() => _shell = new ShellPresenter(_view, new FakePreferences());

    public void Dispose() => _shell.Dispose();

    /// <summary>
    /// A paste goes <em>into</em> what is selected, so copying a row and pasting it
    /// straight back nests the copy inside the original. That is the rule doing what
    /// it says rather than a quirk — landing beside the original is what duplicate
    /// is for.
    /// </summary>
    [Fact]
    public void APasteLandsInsideWhateverIsSelected()
    {
        _view.RaiseCreateGroup();
        SelectionTarget original = _view.HierarchyView.Selected;

        _view.RaiseCopy();
        _view.RaisePaste();

        Assert.Single(Children(Scene));
        Assert.Equal(new[] { "Group 2" }, Children(original).Select(_view.HierarchyView.NameOf));
    }

    [Fact]
    public void PastingIntoTheSceneLeavesTwoTopLevelElements()
    {
        _view.RaiseCreateGroup();

        _view.RaiseCopy();
        _view.HierarchyView.RaiseItemClicked(Scene);
        _view.RaisePaste();

        Assert.Equal(
            new[] { "Group", "Group 2" },
            Children(Scene).Select(_view.HierarchyView.NameOf));
    }

    /// <summary>The next thing the user does is about the copy, not about the original.</summary>
    [Fact]
    public void ThePastedElementIsSelected()
    {
        _view.RaiseCreateGroup();
        SelectionTarget original = _view.HierarchyView.Selected;

        _view.RaiseCopy();
        _view.HierarchyView.RaiseItemClicked(Scene);
        _view.RaisePaste();

        Assert.NotEqual(original, _view.HierarchyView.Selected);
        Assert.Equal(SelectionKind.Element, _view.HierarchyView.Selected.Kind);
        Assert.Equal("Group 2", _view.HierarchyView.NameOf(_view.HierarchyView.Selected));
    }

    [Fact]
    public void APastedElementLandsUnderTheSelectedElement()
    {
        _view.RaiseCreateGroup();
        SelectionTarget source = _view.HierarchyView.Selected;

        _view.HierarchyView.RaiseItemClicked(Scene);
        _view.RaiseCreateVoxelModel();
        SelectionTarget target = _view.HierarchyView.Selected;

        _view.HierarchyView.RaiseItemClicked(source);
        _view.RaiseCopy();
        _view.HierarchyView.RaiseItemClicked(target);
        _view.RaisePaste();

        Assert.Equal(target, _view.HierarchyView.ParentOf(_view.HierarchyView.Selected));
    }

    /// <summary>
    /// The fallback the paste target rule exists for: a material is not a place an
    /// element can live, so the copy goes to the scene's root instead of the paste
    /// doing nothing.
    /// </summary>
    [Fact]
    public void AnElementPastedOntoAMaterialLandsInTheScene()
    {
        _view.RaiseCreateGroup();
        _view.RaiseCopy();

        _view.RaiseCreate(CreateKind.Material);
        _view.RaisePaste();

        Assert.Equal(Scene, _view.HierarchyView.ParentOf(_view.HierarchyView.Selected));
        Assert.Equal(SelectionKind.Element, _view.HierarchyView.Selected.Kind);
    }

    /// <summary>
    /// And the other way round: a material has one place to live whatever is
    /// selected, so it goes into the project's registry.
    /// </summary>
    [Fact]
    public void AMaterialPastedOntoAnElementLandsInTheProject()
    {
        _view.RaiseCreate(CreateKind.Material);
        _view.RaiseCopy();

        _view.RaiseCreateGroup();
        _view.RaisePaste();

        Assert.Equal(SelectionKind.Material, _view.HierarchyView.Selected.Kind);
        Assert.Equal("Material 2", _view.HierarchyView.NameOf(_view.HierarchyView.Selected));
    }

    /// <summary>
    /// The copy becomes the scene being worked on, as a created one does — and
    /// selecting it has to happen after that switch, which drops the selection.
    /// </summary>
    [Fact]
    public void ASceneIsCopiedWholeAndBecomesTheOneBeingWorkedOn()
    {
        SelectionTarget original = Scene;

        _view.RaiseCreateGroup();
        _view.HierarchyView.RaiseItemClicked(original);

        _view.RaiseCopy();
        _view.RaisePaste();

        SelectionTarget pasted = _view.HierarchyView.Selected;

        Assert.Equal(SelectionKind.Scene, pasted.Kind);
        Assert.NotEqual(original, pasted);
        Assert.Equal("Scene 2", _view.HierarchyView.NameOf(pasted));
        Assert.Equal(pasted, _view.HierarchyView.Emphasized);

        // Whole: the group came with it, in the copy rather than shared with it.
        Assert.Single(Children(pasted));
        Assert.Empty(Children(original));
    }

    [Fact]
    public void CuttingTakesTheElementAndPasteBringsItBack()
    {
        _view.RaiseCreateGroup();

        _view.RaiseCut();

        Assert.Empty(Children(Scene));

        _view.RaisePaste();

        // The name was free again, so a cut and a paste put it back unchanged.
        Assert.Equal(new[] { "Group" }, Children(Scene).Select(_view.HierarchyView.NameOf));
    }

    /// <summary>
    /// A cut is one gesture that does two things, and the second is what makes it a
    /// cut. Refused, it must leave both the document and the clipboard alone.
    /// </summary>
    [Fact]
    public void ARefusedCutTakesNothingAndKeepsTheClipboardAsItWas()
    {
        _view.RaiseCreateGroup();
        _view.RaiseCopy();

        _view.HierarchyView.RaiseItemClicked(Scene);
        _view.RaiseCut();

        // The last scene cannot go, so nothing was cut...
        Assert.Single(_view.HierarchyView.Items.Keys, target => target.Kind == SelectionKind.Scene);

        // ...and what was on the clipboard is still the group.
        _view.RaisePaste();

        Assert.Equal(SelectionKind.Element, _view.HierarchyView.Selected.Kind);
        Assert.Equal("Group 2", _view.HierarchyView.NameOf(_view.HierarchyView.Selected));
    }

    /// <summary>
    /// The refusal the model makes rather than the panel: a material still worn by
    /// a voxel cannot go, and a cut is a deletion like any other. The clipboard is
    /// left as it was, and the reason is shown rather than thrown.
    /// </summary>
    [Fact]
    public void CuttingAMaterialAVoxelStillWearsIsRefusedWithAReason()
    {
        _view.RaiseCreate(CreateKind.Material);
        SelectionTarget material = _view.HierarchyView.Selected;

        // Activating is what makes it the material the brush paints with.
        _view.HierarchyView.RaiseItemActivated(material);

        _view.HierarchyView.RaiseItemClicked(Scene);
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Paint);
        _view.SceneView.RaisePicked(
            new ScenePick(Vector3I.Zero, new Vector3I(0, 1, 0), HitVoxel: false, Alternate: false));

        _view.HierarchyView.RaiseItemClicked(material);
        _view.RaiseCut();

        Assert.Equal("Cannot cut", _view.Errors[0].Title);
        Assert.Equal(2, MaterialCount());
    }

    [Fact]
    public void DuplicatingLeavesTheCopyBesideTheOriginal()
    {
        _view.RaiseCreateGroup();
        SelectionTarget parent = _view.HierarchyView.Selected;

        _view.RaiseCreateVoxelModel();

        _view.RaiseDuplicate();

        Assert.Equal(parent, _view.HierarchyView.ParentOf(_view.HierarchyView.Selected));
        Assert.Equal(
            new[] { "Model", "Model 2" },
            Children(parent).Select(_view.HierarchyView.NameOf));
    }

    /// <summary>
    /// A duplicate is about the row that is selected. Taking the clipboard with it
    /// would be a second thing happening that nobody asked for.
    /// </summary>
    [Fact]
    public void DuplicatingLeavesTheClipboardAlone()
    {
        _view.RaiseCreateGroup();
        _view.RaiseCopy();

        _view.RaiseCreateVoxelModel();
        _view.RaiseDuplicate();

        _view.HierarchyView.RaiseItemClicked(Scene);
        _view.RaisePaste();

        Assert.Equal("Group 2", _view.HierarchyView.NameOf(_view.HierarchyView.Selected));
    }

    [Fact]
    public void DuplicatingAMaterialLeavesACopyInTheRegistry()
    {
        _view.RaiseCreate(CreateKind.Material);

        _view.RaiseDuplicate();

        Assert.Equal(3, MaterialCount());
        Assert.Equal("Material 2", _view.HierarchyView.NameOf(_view.HierarchyView.Selected));
    }

    /// <summary>
    /// Nothing under the project's own row or a category row is a thing, so the
    /// chords have nothing to take and must not invent one.
    /// </summary>
    [Fact]
    public void ACategoryRowIsNotSomethingToCopy()
    {
        _view.RaiseCreateGroup();
        _view.HierarchyView.RaiseItemClicked(new SelectionTarget(SelectionKind.MaterialsCategory, Guid.Empty));

        _view.RaiseCopy();
        _view.RaiseDuplicate();
        _view.RaisePaste();

        Assert.Single(Children(Scene));
    }

    [Fact]
    public void PastingWithAnEmptyClipboardDoesNothing()
    {
        _view.RaiseCreateGroup();

        _view.RaisePaste();

        Assert.Single(Children(Scene));
    }

    /// <summary>One press, one step back — a subtree included.</summary>
    [Fact]
    public void APasteUndoesInOneStep()
    {
        _view.RaiseCreateGroup();
        SelectionTarget parent = _view.HierarchyView.Selected;
        _view.RaiseCreateVoxelModel();

        _view.HierarchyView.RaiseItemClicked(parent);
        _view.RaiseCopy();
        _view.HierarchyView.RaiseItemClicked(Scene);
        _view.RaisePaste();

        Assert.Equal(2, Children(Scene).Count);

        _view.RaiseUndo();

        Assert.Single(Children(Scene));
    }

    private SelectionTarget Scene => new(SelectionKind.Scene, _view.HierarchyView.SceneId);

    private IReadOnlyList<SelectionTarget> Children(SelectionTarget parent) =>
        _view.HierarchyView.ChildrenOf(parent);

    private int MaterialCount() =>
        _view.HierarchyView.Items.Keys.Count(target => target.Kind == SelectionKind.Material);
}
