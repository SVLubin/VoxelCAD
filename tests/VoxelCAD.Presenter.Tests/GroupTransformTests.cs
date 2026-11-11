using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Dragging a handle with several elements selected. The rule is one delta applied
/// to each element's own channel about each element's own pivot — deliberately not
/// about the selection's centre, which is what a group is for.
/// </summary>
public sealed class GroupTransformTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly IScene _scene;
    private readonly GizmoPresenter _gizmo;
    private readonly Guid _first;
    private readonly Guid _second;
    private readonly int _recordsAtSetup;

    public GroupTransformTests()
    {
        _scene = _project.Scenes[0];
        _first = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "First");
        _second = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Second");

        _scene.SetTransform(_first, TransformChannel.Position, new Vector3(4f, 0f, 0f));
        _scene.SetTransform(_second, TransformChannel.Position, new Vector3(10f, 0f, 0f));

        _recordsAtSetup = _project.History.Records.Count;
        _gizmo = new GizmoPresenter(_project, _scene, _view, _session);
    }

    public void Dispose() => _gizmo.Dispose();

    [Fact]
    public void EveryOneOfThemMovesByTheSameDelta()
    {
        SelectBoth(ToolKind.Move);

        _view.RaiseGizmoDrag(new Vector3(3f, 0f, 0f));

        Assert.Equal(new Vector3(7f, 0f, 0f), PositionOf(_first));
        Assert.Equal(new Vector3(13f, 0f, 0f), PositionOf(_second));
    }

    /// <summary>
    /// And each turns about its own pivot, which is what "relative to themselves"
    /// means: the model holds the pivot's location through a rotation, so nothing
    /// here has to compute an orbit — neither element's position moves at all.
    /// </summary>
    [Fact]
    public void EveryOneOfThemTurnsAboutItsOwnPivot()
    {
        SelectBoth(ToolKind.Rotate);

        _view.RaiseGizmoDrag(new Vector3(0f, 90f, 0f));

        Assert.Equal(90f, RotationOf(_first).Y);
        Assert.Equal(90f, RotationOf(_second).Y);
        Assert.Equal(new Vector3(4f, 0f, 0f), PositionOf(_first));
        Assert.Equal(new Vector3(10f, 0f, 0f), PositionOf(_second));
    }

    [Fact]
    public void EveryOneOfThemScalesAboutItsOwnPivot()
    {
        SelectBoth(ToolKind.Scale);

        _view.RaiseGizmoDrag(new Vector3(2f, 0f, 0f));

        Assert.Equal(3f, ScaleOf(_first).X);
        Assert.Equal(3f, ScaleOf(_second).X);
        Assert.Equal(new Vector3(4f, 0f, 0f), PositionOf(_first));
    }

    /// <summary>
    /// One gesture, one record — a group move that undid an element at a time
    /// would be the opposite of what the user watched happen.
    /// </summary>
    [Fact]
    public void TheWholeGroupDragIsOneRecord()
    {
        SelectBoth(ToolKind.Move);

        _view.RaiseGizmoDrag(new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f));

        Assert.Single(RecordsSinceSetup);
        Assert.Equal(ActionKind.MoveElement, RecordsSinceSetup[0].Kind);
    }

    [Fact]
    public void AndOneUndoTakesBothBack()
    {
        SelectBoth(ToolKind.Move);

        _view.RaiseGizmoDrag(new Vector3(3f, 0f, 0f));
        _project.History.Undo();

        Assert.Equal(new Vector3(4f, 0f, 0f), PositionOf(_first));
        Assert.Equal(new Vector3(10f, 0f, 0f), PositionOf(_second));
    }

    /// <summary>The record says what it acted on rather than naming one of several.</summary>
    [Fact]
    public void TheRecordNamesTheGroupRatherThanOneOfIt()
    {
        SelectBoth(ToolKind.Move);

        _view.RaiseGizmoDrag(new Vector3(3f, 0f, 0f));

        Assert.Equal("Move Second and 1 more", ActionLabelHelper.Describe(RecordsSinceSetup[0]));
    }

    /// <summary>A single selection still reads as itself, not as "and 0 more".</summary>
    [Fact]
    public void OneElementIsStillNamedPlainly()
    {
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, _first));
        _session.ActiveTool = ToolKind.Move;

        _view.RaiseGizmoDrag(new Vector3(3f, 0f, 0f));

        Assert.Equal("Move First", ActionLabelHelper.Describe(RecordsSinceSetup[0]));
    }

    /// <summary>
    /// A selected element under a selected ancestor is carried by its parent, so
    /// writing its own channel as well would move it twice — at double speed, in a
    /// case a Shift range reaches immediately, since a group and its children are
    /// adjacent rows.
    /// </summary>
    [Fact]
    public void AChildOfASelectedParentIsNotMovedTwice()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        Guid child = _scene.CreateElement(group, ComponentKind.VoxelModel, "Child");

        _scene.SetTransform(child, TransformChannel.Position, new Vector3(2f, 0f, 0f));

        _session.SetSelection(new SelectionState(
            new[]
            {
                new SelectionTarget(SelectionKind.Element, group),
                new SelectionTarget(SelectionKind.Element, child)
            },
            new SelectionTarget(SelectionKind.Element, group)));
        _session.ActiveTool = ToolKind.Move;

        _view.RaiseGizmoDrag(new Vector3(5f, 0f, 0f));

        Assert.Equal(new Vector3(5f, 0f, 0f), PositionOf(group));
        Assert.Equal(new Vector3(2f, 0f, 0f), PositionOf(child));
    }

    /// <summary>
    /// The group keeps its shape whatever the pull. Handing each element the raw
    /// pull and letting each round it its own way did not: over one cell of pull an
    /// Integer element stood still and then jumped a whole cell while a Float one
    /// beside it glided, so the gap between them swung by a full cell and back —
    /// which is what a selection deforming under the hand looks like.
    /// </summary>
    [Fact]
    public void MixedModesStillMoveAsOne()
    {
        _scene.SetFieldMode(_second, TransformChannel.Position, FieldMode.Float);
        _scene.SetTransform(_second, TransformChannel.Position, new Vector3(10.3f, 0f, 0f));

        SelectBoth(ToolKind.Move);

        _view.RaiseGizmoDragBegan();

        // Under half a cell of pull, the Integer element has not moved — so nothing
        // has, gap intact.
        _view.RaiseGizmoDragged(new Vector3(0.4f, 0f, 0f));

        Assert.Equal(4f, PositionOf(_first).X);
        Assert.Equal(10.3f, PositionOf(_second).X, 3);

        // Past it, both take the same whole step.
        _view.RaiseGizmoDragged(new Vector3(0.8f, 0f, 0f));

        Assert.Equal(5f, PositionOf(_first).X);
        Assert.Equal(11.3f, PositionOf(_second).X, 3);

        _view.RaiseGizmoDragEnded();
    }

    /// <summary>
    /// And the constrained one paces it even when the handles are on the free one:
    /// the group's shape wins over whose handle was grabbed.
    /// </summary>
    [Fact]
    public void TheConstrainedOneSetsTheStepEvenWhenItIsNotTheAnchor()
    {
        _scene.SetFieldMode(_second, TransformChannel.Position, FieldMode.Float);
        _scene.SetTransform(_second, TransformChannel.Position, new Vector3(10.3f, 0f, 0f));

        // The second is the active one, and it is the unconstrained one.
        SelectBoth(ToolKind.Move);

        _view.RaiseGizmoDrag(new Vector3(0.4f, 0f, 0f));

        Assert.Equal(4f, PositionOf(_first).X);
        Assert.Equal(10.3f, PositionOf(_second).X, 3);
    }

    /// <summary>
    /// A group with nothing constrained in it still moves freely — the pacing rule
    /// must not quietly turn a Float selection into a snapping one.
    /// </summary>
    [Fact]
    public void AnUnconstrainedGroupStillGlides()
    {
        _scene.SetFieldMode(_first, TransformChannel.Position, FieldMode.Float);
        _scene.SetFieldMode(_second, TransformChannel.Position, FieldMode.Float);

        SelectBoth(ToolKind.Move);

        _view.RaiseGizmoDrag(new Vector3(0.4f, 0f, 0f));

        Assert.Equal(4.4f, PositionOf(_first).X, 3);
        Assert.Equal(10.4f, PositionOf(_second).X, 3);
    }

    [Fact]
    public void CancellingPutsEveryOneOfThemBack()
    {
        SelectBoth(ToolKind.Move);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(6f, 0f, 0f));
        _view.RaiseGizmoDragCancelled();

        Assert.Equal(new Vector3(4f, 0f, 0f), PositionOf(_first));
        Assert.Equal(new Vector3(10f, 0f, 0f), PositionOf(_second));
    }

    /// <summary>
    /// The ghost and the readout speak for one element — the active one — because
    /// they say where the hand is against what the model made of it, and the delta
    /// they describe is shared by the whole group anyway.
    /// </summary>
    [Fact]
    public void TheGhostSpeaksForTheActiveElement()
    {
        SelectBoth(ToolKind.Move);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(3.4f, 0f, 0f));

        Assert.NotNull(_view.DragPreview);
        Assert.Equal(_second, _view.DragPreview!.Value.ElementId);

        _view.RaiseGizmoDragEnded();
    }

    private IReadOnlyList<HistoryRecord> RecordsSinceSetup =>
        _project.History.Records.Skip(_recordsAtSetup).ToList();

    /// <summary>The second one is active, which is what a click on it leaves behind.</summary>
    private void SelectBoth(ToolKind tool)
    {
        _session.SetSelection(new SelectionState(
            new[]
            {
                new SelectionTarget(SelectionKind.Element, _first),
                new SelectionTarget(SelectionKind.Element, _second)
            },
            new SelectionTarget(SelectionKind.Element, _second)));

        _session.ActiveTool = tool;
    }

    private Vector3 PositionOf(Guid id) => TransformOf(id).Position;

    private Vector3 RotationOf(Guid id) => TransformOf(id).RotationDegrees;

    private Vector3 ScaleOf(Guid id) => TransformOf(id).Scale;

    private TransformValues TransformOf(Guid id)
    {
        Assert.True(_scene.TryGetElement(id, out IHierarchyElement element));

        return element.Transform;
    }
}
