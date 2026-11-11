using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The gizmo against the model directly, without the shell in the way — which is
/// how the cases the panels cannot reach get covered: a reparent rebuilding the
/// element's node underneath the handles.
/// </summary>
public sealed class GizmoPresenterTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly IScene _scene;
    private readonly GizmoPresenter _gizmo;
    private readonly Guid _elementId;
    private readonly Guid _groupId;
    private readonly int _recordsAtSetup;

    public GizmoPresenterTests()
    {
        _scene = _project.Scenes[0];
        _elementId = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _groupId = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");
        _recordsAtSetup = _project.History.Records.Count;

        _gizmo = new GizmoPresenter(_project, _scene, _view, _session);
    }

    public void Dispose() => _gizmo.Dispose();

    /// <summary>Everything the setup itself filed is not what a test is asserting about.</summary>
    private IReadOnlyList<HistoryRecord> RecordsSinceSetup =>
        _project.History.Records.Skip(_recordsAtSetup).ToList();

    private IHierarchyElement Element
    {
        get
        {
            Assert.True(_scene.TryGetElement(_elementId, out IHierarchyElement element));

            return element;
        }
    }

    /// <summary>What the drag in flight last proposed, asserted to exist at all.</summary>
    private DragPreview Preview
    {
        get
        {
            Assert.NotNull(_view.DragPreview);

            return _view.DragPreview.Value;
        }
    }

    private void Select(ToolKind tool)
    {
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, _elementId));
        _session.ActiveTool = tool;
    }

    /// <summary>A presenter's constructor fills its view from scratch.</summary>
    [Fact]
    public void ItStartsFromWhateverTheSessionAlreadyHeld()
    {
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, _elementId));
        _session.ActiveTool = ToolKind.Scale;

        var second = new GizmoPresenter(_project, _scene, _view, _session);

        try
        {
            Assert.Equal(_elementId, _view.HighlightTarget);
            Assert.Equal(ToolKind.Scale, _view.Tool);
        }
        finally
        {
            second.Dispose();
        }
    }

    /// <summary>
    /// Reparenting makes the viewport drop the subtree and add it again, so the
    /// box and the handles are gone with the node they hung on. Nothing says so
    /// but the change itself, which is why this is asked for a second time rather
    /// than assumed to have survived.
    /// </summary>
    [Fact]
    public void AReparentAsksForTheHighlightAgain()
    {
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, _elementId));
        int before = _view.HighlightCount;

        _scene.ReparentElement(_elementId, _groupId, 0);

        Assert.True(_view.HighlightCount > before);
        Assert.Equal(_elementId, _view.HighlightTarget);
    }

    [Fact]
    public void EditsElsewhereLeaveTheHighlightAlone()
    {
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, _elementId));
        int before = _view.HighlightCount;

        _scene.SetTransform(_groupId, TransformChannel.Position, new Godot.Vector3(1f, 0f, 0f));

        Assert.Equal(before, _view.HighlightCount);
    }

    [Fact]
    public void ADragAppliesTheDeltaToTheStartValue()
    {
        // Float, so that what is asserted is the sum and not the grid: quantised
        // drags are the next test's subject.
        _scene.SetFieldMode(_elementId, TransformChannel.Position, FieldMode.Float);
        Select(ToolKind.Move);

        _view.RaiseGizmoDrag(new Vector3(1f, 0f, 0f), new Vector3(2.5f, 0f, 0f));

        Assert.Equal(new Vector3(2.5f, 0f, 0f), Element.Transform.Position);
    }

    /// <summary>
    /// The canon's anti-drift rule, in the one case that can tell the two apart:
    /// with the channel quantised, adding each frame's step to the rounded value
    /// left behind by the last one accumulates the rounding. Measuring from where
    /// the drag started does not.
    /// </summary>
    [Fact]
    public void QuantisationDoesNotAccumulateAcrossADrag()
    {
        _scene.SetFieldMode(_elementId, TransformChannel.Position, FieldMode.Integer);
        Select(ToolKind.Move);

        _view.RaiseGizmoDrag(
            new Vector3(0.4f, 0f, 0f),
            new Vector3(0.9f, 0f, 0f),
            new Vector3(1.4f, 0f, 0f));

        Assert.Equal(new Vector3(1f, 0f, 0f), Element.Transform.Position);
    }

    /// <summary>
    /// A plane patch reports two non-zero components in one delta, and nothing on
    /// this seam had to learn about it: the rule that adds the delta to the value the
    /// drag started from moves two axes for the same reason it moves one, and the
    /// third is left exactly where it was.
    /// </summary>
    [Fact]
    public void APlaneDragMovesTwoAxesAtOnceAndLeavesTheThird()
    {
        // Off zero on the axis the patch does not slide, so "left alone" is an
        // assertion rather than the value it started at anyway.
        _scene.SetTransform(_elementId, TransformChannel.Position, new Vector3(0f, 3f, 0f));
        Select(ToolKind.Move);
        int recordsBefore = _project.History.Records.Count;

        _view.RaiseGizmoDrag(new Vector3(1f, 0f, 1f), new Vector3(2f, 0f, 4f));

        Assert.Equal(new Vector3(2f, 3f, 4f), Element.Transform.Position);
        Assert.Equal(recordsBefore + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.MoveElement, RecordsSinceSetup[^1].Kind);
    }

    [Fact]
    public void AWholeDragIsOneRecord()
    {
        Select(ToolKind.Move);

        _view.RaiseGizmoDrag(new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f));

        Assert.Equal(ActionKind.MoveElement, Assert.Single(RecordsSinceSetup).Kind);
    }

    [Fact]
    public void UndoTakesBackTheWholeDrag()
    {
        Select(ToolKind.Move);

        _view.RaiseGizmoDrag(new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f));
        _project.History.Undo();

        Assert.Equal(Vector3.Zero, Element.Transform.Position);
    }

    /// <summary>Grabbing a handle and letting go without pulling it is not an edit.</summary>
    [Fact]
    public void ADragThatMovedNothingLeavesNoRecord()
    {
        Select(ToolKind.Move);

        _view.RaiseGizmoDrag();

        Assert.Empty(RecordsSinceSetup);
    }

    [Fact]
    public void RotateWritesDegreesOnTheRotationChannel()
    {
        Select(ToolKind.Rotate);

        _view.RaiseGizmoDrag(new Vector3(0f, 90f, 0f));

        Assert.Equal(new Vector3(0f, 90f, 0f), Element.Transform.RotationDegrees);
        Assert.Equal(Vector3.Zero, Element.Transform.Position);
        Assert.Equal(ActionKind.RotateElement, Assert.Single(RecordsSinceSetup).Kind);
    }

    /// <summary>Scale starts at one, so the delta is what is added to it, not to zero.</summary>
    [Fact]
    public void ScaleWritesOnTheScaleChannel()
    {
        Select(ToolKind.Scale);

        _view.RaiseGizmoDrag(new Vector3(0f, 0f, 1f));

        Assert.Equal(new Vector3(1f, 1f, 2f), Element.Transform.Scale);
        Assert.Equal(ActionKind.ScaleElement, Assert.Single(RecordsSinceSetup).Kind);
    }

    /// <summary>
    /// The whole point of the preview: with the channel quantised, a drag inside one
    /// cell leaves the element where it was, and the viewport is told where the
    /// pointer actually is so it can show the gap.
    /// </summary>
    [Fact]
    public void ADragInsideOneCellStillReportsWhereThePointerIs()
    {
        _scene.SetFieldMode(_elementId, TransformChannel.Position, FieldMode.Integer);
        Select(ToolKind.Move);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(0.3f, 0f, 0f));

        DragPreview preview = Preview;

        Assert.Equal(Vector3.Zero, Element.Transform.Position);
        Assert.Equal(new Vector3(0.3f, 0f, 0f), preview.Values.Position);
        Assert.True(preview.Pending);
        Assert.Equal("X 0 (+0.3)", preview.Readout);
    }

    /// <summary>
    /// Under a channel the model takes literally there is nothing to be ahead of, so
    /// the ghost is not offered — it would sit exactly on the element and fight its
    /// box for depth. The readout still says where the element is.
    /// </summary>
    [Fact]
    public void AFloatDragHasNothingPending()
    {
        _scene.SetFieldMode(_elementId, TransformChannel.Position, FieldMode.Float);
        Select(ToolKind.Move);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(0.3f, 0f, 0f));

        DragPreview preview = Preview;

        Assert.False(preview.Pending);
        Assert.Equal("X 0.3", preview.Readout);
    }

    /// <summary>
    /// The dragged channel carries the raw value and the other two carry what the
    /// model holds — so the ghost is the element's own transform with one channel
    /// pulled ahead, and a rotated element's ghost stays rotated.
    /// </summary>
    [Fact]
    public void ThePreviewCarriesTheOtherChannelsUntouched()
    {
        _scene.SetTransform(_elementId, TransformChannel.Rotation, new Vector3(0f, 90f, 0f));
        _scene.SetTransform(_elementId, TransformChannel.Scale, new Vector3(2f, 2f, 2f));
        Select(ToolKind.Move);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(0.4f, 0f, 0f));

        DragPreview preview = Preview;

        Assert.Equal(new Vector3(0f, 90f, 0f), preview.Values.RotationDegrees);
        Assert.Equal(new Vector3(2f, 2f, 2f), preview.Values.Scale);
    }

    /// <summary>
    /// An axis the drag has moved stays in the readout even on a frame where its own
    /// component happens to be back at zero — a plane drag crosses that constantly,
    /// and a line that reflowed each time would be unreadable.
    /// </summary>
    [Fact]
    public void AnAxisOnceMovedStaysInTheReadout()
    {
        _scene.SetFieldMode(_elementId, TransformChannel.Position, FieldMode.Integer);
        Select(ToolKind.Move);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(0.4f, 0f, 0.4f));
        _view.RaiseGizmoDragged(new Vector3(0.4f, 0f, 0f));

        DragPreview preview = Preview;

        Assert.Equal("X 0 (+0.4)   Z 0", preview.Readout);
    }

    [Fact]
    public void LettingGoClearsThePreview()
    {
        Select(ToolKind.Move);

        _view.RaiseGizmoDrag(new Vector3(0.3f, 0f, 0f));

        Assert.Null(_view.DragPreview);
    }

    [Fact]
    public void CancellingClearsThePreview()
    {
        Select(ToolKind.Move);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(0.3f, 0f, 0f));
        _view.RaiseGizmoDragCancelled();

        Assert.Null(_view.DragPreview);
    }

    /// <summary>
    /// Escape puts the element back where it was picked up. The gesture closes
    /// either way — leaving it open would swallow whatever the user does next.
    /// </summary>
    [Fact]
    public void CancellingPutsTheElementBack()
    {
        Select(ToolKind.Move);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(4f, 0f, 0f));
        _view.RaiseGizmoDragCancelled();

        Assert.Equal(Vector3.Zero, Element.Transform.Position);

        // The gesture is closed, so the next edit is a record of its own rather
        // than part of the drag that was abandoned.
        _scene.RenameElement(_elementId, "Renamed");

        Assert.Equal(ActionKind.RenameElement, RecordsSinceSetup[^1].Kind);
    }

    [Fact]
    public void CancellingBeforeAnythingMovedLeavesNoRecord()
    {
        Select(ToolKind.Move);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragCancelled();

        Assert.Empty(RecordsSinceSetup);
    }

    /// <summary>The brush has no channel to write, so its handles do not exist to grab.</summary>
    [Fact]
    public void PaintDragsNothing()
    {
        Select(ToolKind.Paint);

        _view.RaiseGizmoDrag(new Vector3(3f, 0f, 0f));

        Assert.Equal(Vector3.Zero, Element.Transform.Position);
        Assert.Empty(RecordsSinceSetup);
    }

    [Fact]
    public void ADragWithNothingSelectedChangesNothing()
    {
        _session.ActiveTool = ToolKind.Move;

        _view.RaiseGizmoDrag(new Vector3(3f, 0f, 0f));

        Assert.Equal(Vector3.Zero, Element.Transform.Position);
        Assert.Empty(RecordsSinceSetup);
    }

    /// <summary>
    /// A drag left open when the document changes would swallow the next thing
    /// anyone does, and the batch after it would be refused as a nested one.
    /// </summary>
    [Fact]
    public void DisposingClosesADragInFlight()
    {
        Select(ToolKind.Move);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(2f, 0f, 0f));
        _gizmo.Dispose();

        _scene.RenameElement(_elementId, "Renamed");

        Assert.Equal(
            new[] { ActionKind.MoveElement, ActionKind.RenameElement },
            RecordsSinceSetup.Select(record => record.Kind));
    }

    // --- the pivot ---

    /// <summary>
    /// Where the handles sit travels with the selection, and the first paint of an
    /// empty model gives it its default — the middle of what that paint filled.
    /// The whole complaint the pivot answers is a gizmo left at the origin of a
    /// model built somewhere else.
    /// </summary>
    [Fact]
    public void TheHighlightCarriesThePivotAndTheFirstPaintPlacesIt()
    {
        Select(ToolKind.Move);

        Assert.Equal(Vector3.Zero, _view.HighlightPivot);

        FillCube();

        Assert.Equal(new Vector3(2f, 2f, 2f), _view.HighlightPivot);
    }

    [Fact]
    public void SettingThePivotPushesItToTheViewport()
    {
        Select(ToolKind.Move);

        _scene.SetPivot(_elementId, new Vector3(7f, 0f, 0f));

        Assert.Equal(new Vector3(7f, 0f, 0f), _view.HighlightPivot);
    }

    /// <summary>
    /// The gesture the pivot exists for. Position names the pivot, so a rotation
    /// drag writes one channel, the number stands still, and the middle of the
    /// cube stays exactly where it was — no compensation for anyone to write.
    /// </summary>
    [Fact]
    public void ARotationTurnsAboutThePivotAndTheNumberStandsStill()
    {
        FillCube();
        Select(ToolKind.Rotate);

        _view.RaiseGizmoDrag(new Vector3(0f, 90f, 0f));

        // The stored origin is exact; the reported position is derived through
        // the rotated basis and carries float epsilon, so it is compared the way
        // any float product is.
        Assert.Equal(new Vector3(0f, 90f, 0f), Element.Transform.RotationDegrees);
        Assert.True(Element.Transform.Position.IsEqualApprox(new Vector3(2f, 2f, 2f)));
        Assert.True(PivotInParent().IsEqualApprox(new Vector3(2f, 2f, 2f)));
    }

    [Fact]
    public void TurningAboutThePivotIsOneRecordAndUndoesWhole()
    {
        FillCube();
        Select(ToolKind.Rotate);

        _view.RaiseGizmoDrag(new Vector3(0f, 45f, 0f), new Vector3(0f, 90f, 0f));

        Assert.Equal(
            new[] { ActionKind.EditVoxels, ActionKind.RotateElement },
            RecordsSinceSetup.Select(record => record.Kind));

        _project.History.Undo();

        Assert.Equal(Vector3.Zero, Element.Transform.RotationDegrees);
        Assert.Equal(new Vector3(2f, 2f, 2f), Element.Transform.Position);
    }

    /// <summary>One channel back is the whole restore: the pivot never moved.</summary>
    [Fact]
    public void CancellingARotationRestoresEverything()
    {
        FillCube();
        Select(ToolKind.Rotate);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(0f, 90f, 0f));
        _view.RaiseGizmoDragCancelled();

        Assert.Equal(Vector3.Zero, Element.Transform.RotationDegrees);
        Assert.Equal(new Vector3(2f, 2f, 2f), Element.Transform.Position);
    }

    /// <summary>
    /// A pitch about a mixed-parity pivot degrades the position mode rather than
    /// moving the geometry, and a cancelled drag puts the mode back too — the
    /// chip must not read Float for a turn that never happened.
    /// </summary>
    [Fact]
    public void CancellingADegradingRotationRestoresTheMode()
    {
        FillCube();
        _scene.SetPivot(_elementId, new Vector3(2.5f, 2f, 2.5f));
        Select(ToolKind.Rotate);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(90f, 0f, 0f));

        Assert.Equal(FieldMode.Float, Element.TransformModes.Position);

        _view.RaiseGizmoDragCancelled();

        Assert.Equal(Vector3.Zero, Element.Transform.RotationDegrees);
        Assert.Equal(FieldMode.Integer, Element.TransformModes.Position);
        Assert.True(Element.Transform.Position.IsEqualApprox(new Vector3(2.5f, 2f, 2.5f)));
    }

    /// <summary>
    /// A pivot at the element's own origin is the old behaviour, and it has to be
    /// reachable: position and origin coincide, turning moves neither.
    /// </summary>
    [Fact]
    public void APivotAtTheOriginTurnsWithoutMoving()
    {
        FillCube();
        _scene.SetPivot(_elementId, Vector3.Zero);
        Select(ToolKind.Rotate);

        _view.RaiseGizmoDrag(new Vector3(0f, 90f, 0f));

        Assert.Equal(new Vector3(0f, 90f, 0f), Element.Transform.RotationDegrees);
        // Position tracked the pivot when it moved to the origin, and the origin
        // is where the geometry hangs — so the number reads zero and holds.
        Assert.Equal(Vector3.Zero, Element.Transform.Position);
    }

    /// <summary>
    /// A scale about the pivot: the number stands still, the geometry grows around
    /// it — which is where leaving the pivot out showed worst, doubling a model
    /// parked far from its origin threw it twice as far away again.
    /// </summary>
    [Fact]
    public void AScaleGrowsAboutThePivot()
    {
        FillCube();
        Select(ToolKind.Scale);

        _view.RaiseGizmoDrag(new Vector3(1f, 0f, 0f));

        Assert.Equal(new Vector3(2f, 1f, 1f), Element.Transform.Scale);
        Assert.Equal(new Vector3(2f, 2f, 2f), Element.Transform.Position);
        Assert.True(PivotInParent().IsEqualApprox(new Vector3(2f, 2f, 2f)));
    }

    /// <summary>A move drag slides the pivot itself: start plus delta, in the open.</summary>
    [Fact]
    public void AMoveDragSlidesThePivot()
    {
        FillCube();
        Select(ToolKind.Move);

        _view.RaiseGizmoDrag(new Vector3(3f, 0f, 0f));

        Assert.Equal(new Vector3(5f, 2f, 2f), Element.Transform.Position);
        Assert.Equal(
            new[] { ActionKind.EditVoxels, ActionKind.MoveElement },
            RecordsSinceSetup.Select(record => record.Kind));
    }

    /// <summary>
    /// The ghost wears the raw angle over the applied position, and carries the
    /// pivot so the view can compose its node the way it composes the element's —
    /// the raw values need no solving here precisely because position names the
    /// point the rotation holds.
    /// </summary>
    [Fact]
    public void TheGhostCarriesTheRawAngleAndThePivot()
    {
        FillCube();
        Select(ToolKind.Rotate);

        _view.RaiseGizmoDragBegan();
        _view.RaiseGizmoDragged(new Vector3(0f, 30f, 0f));

        Assert.Equal(new Vector3(0f, 30f, 0f), Preview.Values.RotationDegrees);
        Assert.Equal(new Vector3(2f, 2f, 2f), Preview.Values.Position);
        Assert.Equal(new Vector3(2f, 2f, 2f), Preview.PivotPoint);
    }

    /// <summary>Where the element's pivot stands in its parent's frame.</summary>
    private Vector3 PivotInParent() =>
        TransformHelper.ToTransform(Element.Transform, Element.PivotPoint) * Element.PivotPoint;

    /// <summary>
    /// Cells 0..3 on every axis, so the first-paint default centres the pivot on
    /// (2, 2, 2) — a whole number of cells.
    /// </summary>
    private void FillCube()
    {
        var editsList = new List<VoxelEdit>();

        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                for (var z = 0; z < 4; z++)
                    editsList.Add(new VoxelEdit(new Vector3I(x, y, z), _project.Materials[0].Id));
            }
        }

        _scene.SetVoxels(_elementId, editsList);
    }

    [Fact]
    public void ADisposedPresenterStopsListening()
    {
        _gizmo.Dispose();
        int before = _view.HighlightCount;

        _session.SetSelection(new SelectionTarget(SelectionKind.Element, _elementId));
        _view.RaiseToolRequested(ToolKind.Move);

        Assert.Equal(before, _view.HighlightCount);
        Assert.Equal(ToolKind.Select, _session.ActiveTool);
    }
}
