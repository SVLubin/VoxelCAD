using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Geometry;
using VoxelCAD.Model;
using VoxelCAD.View;
using Godot;

namespace VoxelCAD.Presenter;

/// <summary>
/// Turns a click in the viewport into a recolour of the connected volume the
/// clicked cell belongs to. The view reports where the click landed exactly as
/// it does for the brush; that under the fill tool a click gives the whole
/// six-connected run of the cell's own material the current one is decided here.
/// </summary>
/// <remarks>
/// The mirror orbit applies: the click's cell and each of its images that wears
/// the same material seed a volume of their own, and the union is written as one
/// record. An image on another material, or on empty space, seeds nothing. Both
/// the seeds and the volume are
/// <see cref="VoxelFlood.CollectOrbitConnected"/>'s answer — the same call the
/// <c>fill-connected</c> command makes and the same one the viewport's preview
/// marks — so the marker, the click and the command cannot come to name three
/// different sets of cells.
/// </remarks>
internal sealed class FillPresenter : PresenterBase
{
    private readonly IScene _scene;
    private readonly ISceneView _view;
    private readonly EditorSession _session;

    public FillPresenter(IScene scene, ISceneView view, EditorSession session)
    {
        _scene = scene;
        _view = view;
        _session = session;

        _view.Picked += OnPicked;
    }

    protected override void OnDispose() => _view.Picked -= OnPicked;

    private void OnPicked(ScenePick pick)
    {
        if (!IsAlive || _session.ActiveTool != ToolKind.Fill || !pick.HitVoxel)
            return;

        SelectionTarget selection = _session.ActiveTarget;

        // Prefab-instance contents are read-only wherever they appear, and the
        // model refuses them by throwing — which out of an input handler is a
        // backtrace in the log and a click that silently did nothing.
        if (selection.Kind != SelectionKind.Element
            || !_scene.TryGetElement(selection.Id, out IHierarchyElement element)
            || element.PrefabInstanceId != null
            || element.Component is not IVoxelModel model)
        {
            return;
        }

        Guid materialId = _session.CurrentMaterialId;
        SymmetryValues symmetry = model.Symmetry;

        IReadOnlyList<Vector3I> volume = VoxelFlood.CollectOrbitConnected(
            model.Voxels,
            pick.Cell,
            symmetry.X.Enabled ? symmetry.X.PlaneDoubled : null,
            symmetry.Y.Enabled ? symmetry.Y.PlaneDoubled : null,
            symmetry.Z.Enabled ? symmetry.Z.PlaneDoubled : null);

        var editsList = new List<VoxelEdit>(volume.Count);

        foreach (Vector3I cell in volume)
            editsList.Add(new VoxelEdit(cell, materialId));

        _scene.SetVoxels(element.Id, editsList);
    }
}
