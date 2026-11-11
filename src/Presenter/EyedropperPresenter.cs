using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Turns a click in the viewport into the brush's material. The view reports
/// where the click landed exactly as it does for the brush; that under the
/// eyedropper a click reads the cell instead of writing it is decided here.
/// </summary>
/// <remarks>
/// What it writes is session state, not the document: sampling chooses what the
/// brush paints with next, exactly like clicking the material's row in the
/// project tree, so it leaves no history record. The selection is left alone on
/// purpose — moving it to the material's row would take the pick target away
/// from the very model being sampled, and the inspector with it.
/// <para>
/// A successful sample then hands the viewport back to the brush. Picking up a
/// colour is a step in painting rather than a mode to sit in — the tool exists to
/// answer "paint with that", and the answer is only useful under the brush — so
/// staying in it would make every sample two gestures. A sample that read nothing
/// changes neither the material nor the tool: the tool follows the material, and
/// where there was no material there is nothing to follow.
/// </para>
/// </remarks>
internal sealed class EyedropperPresenter : PresenterBase
{
    private readonly IScene _scene;
    private readonly ISceneView _view;
    private readonly EditorSession _session;

    public EyedropperPresenter(IScene scene, ISceneView view, EditorSession session)
    {
        _scene = scene;
        _view = view;
        _session = session;

        _view.Picked += OnPicked;
    }

    protected override void OnDispose() => _view.Picked -= OnPicked;

    private void OnPicked(ScenePick pick)
    {
        if (!IsAlive || _session.ActiveTool != ToolKind.Eyedropper || !pick.HitVoxel)
            return;

        SelectionTarget selection = _session.ActiveTarget;

        if (selection.Kind != SelectionKind.Element
            || !_scene.TryGetElement(selection.Id, out IHierarchyElement element)
            || element.Component is not IVoxelModel model
            || !model.TryGetVoxel(pick.Cell, out Guid materialId))
        {
            return;
        }

        _session.CurrentMaterialId = materialId;

        // Through RequestTool rather than the setter, so this arrives exactly as a
        // tool key would: the viewport is handed the keyboard along with the brush,
        // and the request stands even where the brush was already the tool — which
        // is the case an eyedropper used from the key and released cannot be.
        _session.RequestTool(ToolKind.Paint);
    }
}
