using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Geometry;
using VoxelCAD.Model;
using VoxelCAD.View;
using Godot;

namespace VoxelCAD.Presenter;

/// <summary>
/// Turns an extrude gesture into an extrusion. The view measures the pull —
/// which face was grabbed and how many layers it travelled, a click being the
/// one-layer degenerate case — and reports it through the ordinary pick; that
/// the patch grows outward or is carved away is decided here, by the sign.
/// </summary>
/// <remarks>
/// The patch and the cells it writes come from <see cref="VoxelFacePatch"/>, the
/// same answer the viewport's marker is drawn from, so the preview cannot promise
/// a set of cells the release does not deliver. What is added on top is the
/// material: an extrusion continues the surface it grew out of, so it carries the
/// seed cell's material rather than whatever the palette currently has selected —
/// pushing a face out is a change of shape, not a change of colour.
/// <para>
/// One gesture is one history record however far it pulled, mirrors included —
/// not through any batch, but because the whole answer arrives as one edit list.
/// </para>
/// </remarks>
internal sealed class ExtrudePresenter : PresenterBase
{
    private readonly IScene _scene;
    private readonly ISceneView _view;
    private readonly EditorSession _session;

    public ExtrudePresenter(IScene scene, ISceneView view, EditorSession session)
    {
        _scene = scene;
        _view = view;
        _session = session;

        _view.Picked += OnPicked;
    }

    protected override void OnDispose() => _view.Picked -= OnPicked;

    private void OnPicked(ScenePick pick)
    {
        // Layers of zero is every pick that is not an extrusion — the brush's,
        // the eyedropper's — and also a gesture that pulled out and came home, whose
        // honest answer is nothing.
        if (!IsAlive
            || _session.ActiveTool != ToolKind.Extrude
            || !pick.HitVoxel
            || pick.Layers == 0)
        {
            return;
        }

        SelectionTarget selection = _session.ActiveTarget;

        if (selection.Kind != SelectionKind.Element
            || !_scene.TryGetElement(selection.Id, out IHierarchyElement element)
            || element.Component is not IVoxelModel model
            || !VoxelFaces.TryGetFaceIndex(pick.Face, out int face)
            || !model.Voxels.TryGetValue(pick.Cell, out Guid material))
        {
            return;
        }

        SymmetryValues symmetry = model.Symmetry;

        IReadOnlyList<Vector3I> cells = VoxelFacePatch.CollectExtrusion(
            model.Voxels,
            pick.Cell,
            face,
            pick.Layers,
            symmetry.X.Enabled ? symmetry.X.PlaneDoubled : null,
            symmetry.Y.Enabled ? symmetry.Y.PlaneDoubled : null,
            symmetry.Z.Enabled ? symmetry.Z.PlaneDoubled : null);

        if (cells.Count == 0)
            return;

        // A pull inward erases; a pull outward fills with the face's own material.
        Guid? written = pick.Layers < 0 ? null : material;
        var editsList = new List<VoxelEdit>(cells.Count);

        foreach (Vector3I cell in cells)
            editsList.Add(new VoxelEdit(cell, written));

        _scene.SetVoxels(element.Id, editsList);
    }
}
