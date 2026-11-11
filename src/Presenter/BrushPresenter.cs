using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Geometry;
using VoxelCAD.Model;
using VoxelCAD.View;
using Godot;

namespace VoxelCAD.Presenter;

/// <summary>
/// Turns a click in the viewport into a voxel edit. The view reports where the
/// click landed; deciding that a plain click fills the cell in front of the face
/// and a modified one clears the cell behind it happens here.
/// </summary>
/// <remarks>
/// A stroke is the same click, repeated for every cell the hover proposed while
/// the button stayed down, and it is <b>one record</b>: the view brackets the
/// cells with <see cref="ISceneView.StrokeBegan"/> and
/// <see cref="ISceneView.StrokeEnded"/>, and between the two the edits collect
/// into a history batch held by the session — handed to the session rather than
/// kept here, the gizmo's own arrangement, so that whatever the user does next
/// can close it without knowing which panel opened it. Every way out of a stroke
/// commits; a batch cannot be discarded, and the cells are already the model's.
/// </remarks>
internal sealed class BrushPresenter : PresenterBase
{
    private readonly IProject _project;
    private readonly IScene _scene;
    private readonly ISceneView _view;
    private readonly EditorSession _session;

    // The element the stroke in flight is writing to; null between strokes.
    private Guid? _strokeTarget;

    public BrushPresenter(IProject project, IScene scene, ISceneView view, EditorSession session)
    {
        _project = project;
        _scene = scene;
        _view = view;
        _session = session;

        _view.Picked += OnPicked;
        _view.StrokeBegan += OnStrokeBegan;
        _view.StrokeEnded += OnStrokeEnded;
        _session.GestureEnded += OnGestureEnded;
        _session.SelectionChanged += OnSelectionChanged;
        _session.CurrentMaterialChanged += OnCurrentMaterialChanged;

        _view.SetBrushMaterial(_session.CurrentMaterialId);
        OnSelectionChanged();
    }

    protected override void OnDispose()
    {
        // A gesture must never outlive the presenter that opened it: an open batch
        // swallows every later edit, and the next one to be opened is refused as a
        // nested batch.
        OnStrokeEnded();

        _view.Picked -= OnPicked;
        _view.StrokeBegan -= OnStrokeBegan;
        _view.StrokeEnded -= OnStrokeEnded;
        _session.GestureEnded -= OnGestureEnded;
        _session.SelectionChanged -= OnSelectionChanged;
        _session.CurrentMaterialChanged -= OnCurrentMaterialChanged;
    }

    /// <summary>
    /// Only a voxel model can be painted on, so anything else selected leaves the
    /// viewport with nothing to resolve clicks against.
    /// </summary>
    private void OnSelectionChanged()
    {
        // A change can rebuild this presenter part-way through its own dispatch;
        // see PresenterBase.
        if (IsAlive)
            _view.SetPickTarget(FindVoxelTarget());
    }

    /// <summary>
    /// Picked in the project tree, and the viewport paints with it from then on.
    /// Shared state rather than a message: neither panel knows the other exists.
    /// </summary>
    private void OnCurrentMaterialChanged() => _view.SetBrushMaterial(_session.CurrentMaterialId);

    private void OnPicked(ScenePick pick)
    {
        // The viewport reports one Picked for every cell tool; which presenter
        // a click belongs to is session state, checked by each listener rather
        // than arbitrated between them.
        if (_session.ActiveTool != ToolKind.Paint)
            return;

        if (FindVoxelTarget() is not { } targetId)
            return;

        // Nothing to clear when the click landed on the scene box.
        if (pick.Alternate && !pick.HitVoxel)
            return;

        Vector3I cell = pick.Alternate ? pick.Cell : pick.Cell + pick.Face;
        Guid? materialId = pick.Alternate ? null : _session.CurrentMaterialId;

        _scene.SetVoxels(targetId, CollectOrbitEdits(targetId, cell, materialId));
    }

    /// <summary>
    /// The press. The batch opens before the first cell arrives, so that cell is
    /// inside it; a press under another tool, or with nothing to paint on, opens
    /// nothing — and the cells that follow are then refused one at a time by
    /// <see cref="OnPicked"/> on the same grounds. The target is remembered only
    /// once the batch is open: opening one ends whatever gesture was in flight,
    /// and the listener below must not mistake that for this stroke's own end.
    /// </summary>
    private void OnStrokeBegan()
    {
        if (_session.ActiveTool != ToolKind.Paint || FindVoxelTarget() is not { } targetId)
            return;

        OpenStrokeBatch(targetId);
        _strokeTarget = targetId;
    }

    /// <summary>The release, or whatever else closed the stroke. Commits.</summary>
    private void OnStrokeEnded()
    {
        if (_strokeTarget == null)
            return;

        _strokeTarget = null;
        _session.EndGesture();
    }

    /// <summary>
    /// The gesture was ended under the stroke — an undo reaching the shell through
    /// the platform's own menu bar, an agent's camera command, anything that starts
    /// a new action first — so the stroke closes too. It has to: its record has
    /// been filed, and a stroke that went on would be painting on a surface it
    /// froze at the press against cells an undo may just have taken away. The
    /// stroke's own release comes through here as well and finds nothing to do,
    /// the target having been cleared first.
    /// </summary>
    private void OnGestureEnded()
    {
        if (_strokeTarget == null)
            return;

        _strokeTarget = null;
        _view.EndStroke();
    }

    private void OpenStrokeBatch(Guid targetId)
    {
        string name = _scene.TryGetElement(targetId, out IHierarchyElement element)
            ? element.Name
            : string.Empty;

        _session.BeginGesture(
            _project.History.CreateBatch(ActionKind.EditVoxels, _scene.Id, targetId, name));
    }

    /// <summary>
    /// A click writes the whole orbit of its cell under the element's mirror
    /// planes, as one edit list and so one history record. Painting fills every
    /// image with the same material; erasing defeats every image at once,
    /// because clearing one side alone would leave a click that visibly did
    /// nothing once its mirror was consulted. Edits that change nothing — an
    /// erase aimed at an empty image — fold away in the model.
    /// </summary>
    private IReadOnlyList<VoxelEdit> CollectOrbitEdits(
        Guid targetId,
        Vector3I cell,
        Guid? materialId)
    {
        SymmetryValues symmetry =
            _scene.TryGetElement(targetId, out IHierarchyElement element)
                && element.Component is IVoxelModel model
                ? model.Symmetry
                : default;

        IReadOnlyList<Vector3I> orbit = VoxelSymmetry.ExpandOrbit(
            cell,
            symmetry.X.Enabled ? symmetry.X.PlaneDoubled : null,
            symmetry.Y.Enabled ? symmetry.Y.PlaneDoubled : null,
            symmetry.Z.Enabled ? symmetry.Z.PlaneDoubled : null);

        var editsList = new List<VoxelEdit>(orbit.Count);

        foreach (Vector3I orbitCell in orbit)
            editsList.Add(new VoxelEdit(orbitCell, materialId));

        return editsList;
    }

    private Guid? FindVoxelTarget()
    {
        SelectionTarget selection = _session.ActiveTarget;

        // And not prefab-instance content, which is read-only wherever it appears:
        // the target is what every cell tool resolves its click against, so
        // refusing it here is what makes the viewport offer no preview and — through
        // _previewWasOffered — answer no click, rather than each tool throwing out
        // of its own handler.
        if (selection.Kind != SelectionKind.Element
            || !_scene.TryGetElement(selection.Id, out IHierarchyElement element)
            || element.PrefabInstanceId != null
            || element.ComponentKind != ComponentKind.VoxelModel)
        {
            return null;
        }

        return selection.Id;
    }
}
