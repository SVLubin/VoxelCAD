using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Binds the viewport's tools to the session: which tool is active, what it acts
/// on, and — while a handle is dragged — turning that drag into transform edits.
/// </summary>
/// <remarks>
/// A sibling of the brush presenter rather than part of it: the brush turns a
/// click into a voxel edit, this turns a drag into a transform edit, and neither
/// knows the other exists. What they share is the session, which holds the tool
/// the same way it holds the selection.
/// <para>
/// The gizmo is an input method over <c>set-transform</c>, exactly as the brush is
/// one over <c>set-voxels</c>: everything it does is reachable from the command
/// surface already, and nothing about quantisation or history is decided here.
/// Snapping in particular is not implemented — it falls out of the element's field
/// modes, because the model is handed a value and rounds it.
/// </para>
/// </remarks>
internal sealed class GizmoPresenter : PresenterBase
{
    private readonly IProject _project;
    private readonly IScene _scene;
    private readonly ISceneView _view;
    private readonly EditorSession _session;

    // Every element this drag writes to, with the value each started from — the
    // canon's start + Σdelta rule, kept per element because a group drag applies
    // one delta to several different starting values. Empty means no drag.
    private readonly List<DragEntry> _dragEntriesList = new();

    // Which of them the ghost and the readout speak for. The active element when
    // the drag writes to it, else the first one it does write to — the active one
    // is dropped when it stands under another selected element, and a readout
    // describing an element nothing is writing would be a lie.
    private Guid? _dragTarget;

    // Which entry quantises the pull for the whole group; see GroupDragPace. Not
    // necessarily the anchor: the anchor is who the drag speaks for, this is who
    // decides how big a step it may take.
    private int _dragPaceIndex;

    private TransformChannel _dragChannel;
    private Vector3 _dragStart;

    // The pivot as the drag picked it up, in the element's own cells. Captured
    // once and only for the ghost: nothing during a transform drag edits cells or
    // moves the pivot, and holding it still is the model's own rule now.
    private Vector3 _dragPivot;

    // Which axes this drag has moved, accumulated rather than read off the current
    // frame: a plane drag crosses zero on one of its two axes constantly, and an
    // axis blinking out of the readout as it does would be worse than no readout.
    private int _dragAxisMask;

    public GizmoPresenter(IProject project, IScene scene, ISceneView view, EditorSession session)
    {
        _project = project;
        _scene = scene;
        _view = view;
        _session = session;

        _view.ToolRequested += OnToolRequested;
        _view.GizmoDragBegan += OnDragBegan;
        _view.GizmoDragged += OnDragged;
        _view.GizmoDragEnded += OnDragEnded;
        _view.GizmoDragCancelled += OnDragCancelled;
        _scene.Changed += OnSceneChanged;
        _session.SelectionChanged += Refresh;
        _session.ToolChanged += Refresh;
        _session.ToolRequested += OnToolActivated;

        Refresh();
    }

    protected override void OnDispose()
    {
        // A gesture must never outlive the presenter that opened it: an open batch
        // swallows every later edit, and the next one to be opened is refused as a
        // nested batch.
        OnDragEnded();

        _view.ToolRequested -= OnToolRequested;
        _view.GizmoDragBegan -= OnDragBegan;
        _view.GizmoDragged -= OnDragged;
        _view.GizmoDragEnded -= OnDragEnded;
        _view.GizmoDragCancelled -= OnDragCancelled;
        _scene.Changed -= OnSceneChanged;
        _session.SelectionChanged -= Refresh;
        _session.ToolChanged -= Refresh;
        _session.ToolRequested -= OnToolActivated;
    }

    /// <summary>
    /// The viewport reports the key; what it means is a change of session state,
    /// which comes straight back as <see cref="ISceneView.SetTool"/>. Going the
    /// long way round is the point: the tool is shared state, not a viewport mode,
    /// so anything else that ever changes it moves the viewport with it.
    /// </summary>
    private void OnToolRequested(ToolKind tool)
    {
        if (IsAlive)
            _session.RequestTool(tool);
    }

    /// <summary>
    /// A tool was asked for, from wherever. The viewport takes the keyboard,
    /// because that is where the tool acts and where the keys that drive it have
    /// to arrive — a no-op when the request came from the viewport's own key, and
    /// the whole of the answer when it came from a panel.
    /// </summary>
    private void OnToolActivated()
    {
        if (IsAlive)
            _view.TakeKeyboard();
    }

    /// <summary>
    /// The value the whole drag is measured from, captured once. Every later frame
    /// applies <c>start + delta</c> rather than adding this frame's step to what is
    /// there now — the canon's rule, and the reason a drag inside one grid cell
    /// neither drifts nor fills the history with identical steps.
    /// </summary>
    /// <remarks>
    /// A group drag is the same gesture with more than one entry in it: <b>one
    /// delta, applied to each element's own channel about each element's own
    /// pivot</b>. Deliberately not about the selection's centre — wanting a common
    /// centre is what a group is for, and a second invisible one that existed only
    /// while several rows happened to be picked out would be a worse thing to
    /// explain. What the rule cannot be left alone with is a selected element under
    /// a selected ancestor, which <see cref="TransformTargets"/> drops.
    /// </remarks>
    private void OnDragBegan()
    {
        if (!IsAlive || ToChannel(_session.ActiveTool) is not { } channel)
            return;

        IReadOnlyList<Guid> targets =
            TransformTargets.Collect(_scene, _session.Selection.Targets);

        _dragEntriesList.Clear();

        var modesList = new List<FieldMode>(targets.Count);

        foreach (Guid targetId in targets)
        {
            if (_scene.TryGetElement(targetId, out IHierarchyElement target))
            {
                _dragEntriesList.Add(new DragEntry(
                    targetId,
                    ReadChannel(target.Transform, channel),
                    target.TransformModes.Position));

                modesList.Add(ReadMode(target.TransformModes, channel));
            }
        }

        if (_dragEntriesList.Count == 0)
            return;

        int anchorIndex = FindAnchorIndex();

        if (!_scene.TryGetElement(_dragEntriesList[anchorIndex].Id, out IHierarchyElement anchor))
        {
            _dragEntriesList.Clear();

            return;
        }

        _dragTarget = anchor.Id;
        _dragChannel = channel;
        _dragStart = ReadChannel(anchor.Transform, channel);
        _dragPivot = anchor.PivotPoint;
        _dragAxisMask = 0;
        _dragPaceIndex = GroupDragPace.Choose(modesList, anchorIndex);

        // Handed to the session, not held here: whatever the user does next has to
        // be able to close it without knowing which panel opened it. One scope
        // however many elements are in the drag — a gesture is one record, and a
        // group move that undid an element at a time would be the opposite of what
        // the user watched happen.
        _session.BeginGesture(_project.History.CreateBatch(
            ToActionKind(_session.ActiveTool),
            _scene.Id,
            anchor.Id,
            ActionLabelHelper.DescribeTargets(anchor.Name, _dragEntriesList.Count)));
    }

    /// <summary>
    /// One channel per element, whatever the tool: position is the pivot's own
    /// location, and the model holds that location still through a rotation or a
    /// scale — turning about the pivot stopped being this presenter's arithmetic
    /// the day the position channel started naming the point. Which is what makes
    /// each element of a group turn about itself for free.
    /// </summary>
    /// <remarks>
    /// The pull is quantised <b>once</b> and the granted step is what everybody
    /// else is given, so the group keeps its shape. Handing each element the raw
    /// pull instead is what this replaced, and it deformed the selection as it was
    /// dragged: measured over one cell of pull, an Integer element stood still and
    /// then jumped a whole cell while a Float one beside it glided, so the gap
    /// between them swung by a full cell and back. Which element sets the step is
    /// <see cref="GroupDragPace"/>'s answer, and it must be a constrained one where
    /// there is any, or the rest would round what they were given and the
    /// deformation would come back.
    /// <para>
    /// No rounding rule is restated here to do it: the pace-setter is written with
    /// the raw pull and the model is <em>asked what it made of it</em>. Snapping
    /// stays the model's, exactly as it is for a drag of one.
    /// </para>
    /// </remarks>
    private void OnDragged(Vector3 delta)
    {
        if (!IsAlive || _dragTarget is not { } id)
            return;

        DragEntry pace = _dragEntriesList[_dragPaceIndex];

        _scene.SetTransform(pace.Id, _dragChannel, pace.Start + delta);

        Vector3 granted = ReadGranted(pace, delta);

        for (var index = 0; index < _dragEntriesList.Count; index++)
        {
            if (index == _dragPaceIndex)
                continue;

            DragEntry entry = _dragEntriesList[index];

            _scene.SetTransform(entry.Id, _dragChannel, entry.Start + granted);
        }

        _dragAxisMask |= DragReadoutHelper.GetAxisMask(delta);

        ShowDragPreview(id, _dragStart + delta);
    }

    /// <summary>
    /// How far the pace-setter actually moved, read back out of the model. Falls
    /// back to the raw pull if the element has gone from under the drag, which
    /// leaves the group behaving as it would with nothing to pace it.
    /// </summary>
    private Vector3 ReadGranted(DragEntry pace, Vector3 delta) =>
        _scene.TryGetElement(pace.Id, out IHierarchyElement element)
            ? ReadChannel(element.Transform, _dragChannel) - pace.Start
            : delta;

    /// <summary>
    /// The element the ghost and the readout speak for: the active one when the
    /// drag writes to it, else the first one it does write to.
    /// </summary>
    private int FindAnchorIndex()
    {
        if (FindTarget() is { } active)
        {
            for (var index = 0; index < _dragEntriesList.Count; index++)
            {
                if (_dragEntriesList[index].Id == active)
                    return index;
            }
        }

        return 0;
    }

    /// <summary>
    /// What the drag asked for, beside what the model made of it. Only here can the
    /// two be compared: the raw value came from the viewport and the applied one is
    /// read back out of the model, which has by now rounded it to the channel's mode
    /// — or clamped it to the region, which this reports just as readily and for the
    /// same reason, since "the wall is holding it" is the same question as "the grid
    /// is holding it" from the hand's point of view.
    /// </summary>
    private void ShowDragPreview(Guid id, Vector3 raw)
    {
        if (!_scene.TryGetElement(id, out IHierarchyElement element))
            return;

        TransformValues applied = element.Transform;
        Vector3 appliedChannel = ReadChannel(applied, _dragChannel);
        FieldMode mode = ReadMode(element.TransformModes, _dragChannel);

        // The raw channel over the applied others, and nothing more to solve: a
        // rotation or scale holds the pivot's location by construction, so the
        // ghost turning about the pivot falls out of the same composition the
        // element itself gets.
        _view.SetDragPreview(new DragPreview(
            id,
            WriteChannel(applied, _dragChannel, raw),
            !appliedChannel.IsEqualApprox(raw),
            DragReadoutHelper.Format(_dragChannel, mode, appliedChannel, raw, _dragAxisMask),
            _dragPivot));
    }

    private void OnDragEnded()
    {
        if (_dragTarget == null)
            return;

        _dragTarget = null;
        _dragEntriesList.Clear();
        _session.EndGesture();

        // Nothing is being pulled any more, so there is nothing to be ahead of the
        // model — and a ghost left standing would claim otherwise.
        if (IsAlive)
            _view.SetDragPreview(null);
    }

    /// <summary>
    /// Abandoning the drag puts every element back where it was picked up. The
    /// gesture is then closed like any other — the model has no way to discard a
    /// batch, so a drag that had already moved something leaves one record that
    /// undoes to nothing; see the ledger.
    /// </summary>
    private void OnDragCancelled()
    {
        if (_dragTarget == null)
            return;

        // One channel back restores the values: the pivot's location was held
        // through the drag, so re-anchoring against it lands on the start state.
        // The position mode comes back separately — a turn through mixed parity
        // degrades it to Float, and a cancelled turn must not leave that behind.
        if (IsAlive)
        {
            foreach (DragEntry entry in _dragEntriesList)
            {
                _scene.SetTransform(entry.Id, _dragChannel, entry.Start);

                if (_scene.TryGetElement(entry.Id, out IHierarchyElement element)
                    && element.TransformModes.Position != entry.StartPositionMode)
                {
                    _scene.SetFieldMode(
                        entry.Id, TransformChannel.Position, entry.StartPositionMode);
                }
            }
        }

        OnDragEnded();
    }

    /// <summary>
    /// The element the gizmo hangs on may be rebuilt underneath it: reparenting
    /// makes the viewport drop the subtree and add it again, so the box and the
    /// handles have to be asked for a second time. Deleting one is the same case
    /// with nothing to ask for.
    /// </summary>
    private void OnSceneChanged(SceneChange change)
    {
        // A change can rebuild this presenter part-way through its own dispatch;
        // see PresenterBase.
        if (!IsAlive)
            return;

        // A drag writes the transform every frame while holding the pivot it
        // captured at the press. Refreshing under it would rebuild the selection
        // box and the lasers once a frame, and put back a pivot nothing asked to
        // move.
        if (_dragTarget != null)
            return;

        if (change.Kind is SceneChangeKind.ElementAdded
            or SceneChangeKind.ElementRemoved
            or SceneChangeKind.ElementMoved)
        {
            Refresh();

            return;
        }

        // The rest matter only for an element that is selected — any of them, not
        // only the one the handles are on, because every selected element wears a
        // border and a border is sized from the cells it goes round. A voxel edit
        // is here for one further case: the first edit of an empty model gives the
        // pivot its default, and PivotChanged rides inside the same record —
        // refreshing on both is cheap and misses neither half.
        if (change.Kind is SceneChangeKind.VoxelsChanged
                or SceneChangeKind.PivotChanged
                or SceneChangeKind.TransformChanged
            && _session.Selection.Contains(
                new SelectionTarget(SelectionKind.Element, change.ElementId)))
        {
            Refresh();
        }
    }

    /// <summary>
    /// Everything the viewport is told about tools, in one push — which is also
    /// what fills it from scratch when this is built.
    /// </summary>
    /// <remarks>
    /// The whole selection is pushed, and the active target with it. Every selected
    /// element wears a border; only the active one carries the handles, because a
    /// transform drag edits one channel of one element and there is no honest
    /// answer yet for what pulling on several at once would write.
    /// </remarks>
    private void Refresh()
    {
        if (!IsAlive)
            return;

        Guid? target = FindTarget();

        _view.SetSelectionHighlight(CollectSelectedElements(), target, FindPivot(target));
        _view.SetTool(_session.ActiveTool);
    }

    /// <summary>
    /// The selected elements this scene actually holds. A selection of materials or
    /// of scenes leaves the viewport with nothing to draw round, and a selection
    /// made before something was deleted may name one that has gone.
    /// </summary>
    private IReadOnlyList<Guid> CollectSelectedElements()
    {
        IReadOnlyList<SelectionTarget> targets = _session.Selection.Targets;
        var idsList = new List<Guid>(targets.Count);

        for (var index = 0; index < targets.Count; index++)
        {
            SelectionTarget target = targets[index];

            if (target.Kind == SelectionKind.Element && _scene.TryGetElement(target.Id, out _))
                idsList.Add(target.Id);
        }

        return idsList;
    }

    /// <summary>
    /// Where the handles sit on the target, in its own cells. Resolved here rather
    /// than in the viewport because the rule that places it is the model's, and
    /// pushed with the selection because it is read on the same occasions.
    /// </summary>
    private Vector3 FindPivot(Guid? target) =>
        target is { } id && _scene.TryGetElement(id, out IHierarchyElement element)
            ? element.PivotPoint
            : Vector3.Zero;

    /// <summary>
    /// Only an element has a transform to take hold of. A scene or a material row
    /// selected in the same field leaves the viewport with nothing to draw on.
    /// </summary>
    private Guid? FindTarget()
    {
        SelectionTarget selection = _session.ActiveTarget;

        // A prefab instance's contents are selected and bordered like anything
        // else, and carry no handles: a drag could not move them, and handles that
        // answer nothing are a promise the panel then has to take back.
        if (selection.Kind != SelectionKind.Element
            || !_scene.TryGetElement(selection.Id, out IHierarchyElement element)
            || element.PrefabInstanceId != null)
        {
            return null;
        }

        return selection.Id;
    }

    /// <summary>Null for the brush, which edits no channel at all.</summary>
    private static TransformChannel? ToChannel(ToolKind tool) => tool switch
    {
        ToolKind.Move => TransformChannel.Position,
        ToolKind.Rotate => TransformChannel.Rotation,
        ToolKind.Scale => TransformChannel.Scale,
        _ => null
    };

    private static ActionKind ToActionKind(ToolKind tool) => tool switch
    {
        ToolKind.Move => ActionKind.MoveElement,
        ToolKind.Rotate => ActionKind.RotateElement,
        ToolKind.Scale => ActionKind.ScaleElement,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null)
    };

    private static Vector3 ReadChannel(TransformValues values, TransformChannel channel) => channel switch
    {
        TransformChannel.Position => values.Position,
        TransformChannel.Rotation => values.RotationDegrees,
        TransformChannel.Scale => values.Scale,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
    };

    /// <summary>
    /// The element's values with one channel replaced — the whole transform the drag
    /// is asking for, since the other two channels are not being dragged and so are
    /// exactly what the model holds.
    /// </summary>
    private static TransformValues WriteChannel(
        TransformValues values,
        TransformChannel channel,
        Vector3 value) => channel switch
    {
        TransformChannel.Position => values with { Position = value },
        TransformChannel.Rotation => values with { RotationDegrees = value },
        TransformChannel.Scale => values with { Scale = value },
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
    };

    private static FieldMode ReadMode(TransformModes modes, TransformChannel channel) => channel switch
    {
        TransformChannel.Position => modes.Position,
        TransformChannel.Rotation => modes.Rotation,
        TransformChannel.Scale => modes.Scale,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
    };

    /// <summary>
    /// One element the drag writes to, as it was when the handle was grabbed.
    /// </summary>
    /// <param name="Start">
    /// The dragged channel's value at the press. Every later frame applies
    /// <c>Start + delta</c> rather than adding this frame's step to what is there
    /// now — the canon's rule, and the reason a drag inside one grid cell neither
    /// drifts nor fills the history with identical steps.
    /// </param>
    /// <param name="StartPositionMode">
    /// The position mode at the press. A rotation that puts the origin off the
    /// mode's lattice degrades the mode rather than moving the geometry, and a
    /// cancelled drag has to put that back too — restoring the channel's value
    /// alone would leave the chip reading Float for a turn that never happened.
    /// </param>
    private readonly record struct DragEntry(
        Guid Id, Vector3 Start, FieldMode StartPositionMode);
}
