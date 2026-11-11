using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Geometry;
using VoxelCAD.Model;
using VoxelCAD.Preferences;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>Binds the inspector to whatever the session has selected.</summary>
/// <remarks>
/// The project tree offers rows of several kinds, so this dispatches on the kind
/// of the selection rather than looking one id up in one place: an element has a
/// transform, a scene has a build region, a material has a colour.
/// </remarks>
internal sealed class InspectorPresenter : PresenterBase
{
    private static readonly TransformChannel[] Channels =
    {
        TransformChannel.Position,
        TransformChannel.Rotation,
        TransformChannel.Scale
    };

    private readonly IProject _project;
    private readonly IScene _scene;
    private readonly IInspectorView _view;
    private readonly EditorSession _session;
    private readonly IEditorPreferences _preferences;
    private readonly IDialogService _dialogs;
    private readonly Action<Guid> _activateScene;

    private bool _colorGestureOpen;

    public InspectorPresenter(
        IProject project,
        IScene scene,
        IInspectorView view,
        EditorSession session,
        IEditorPreferences preferences,
        IDialogService dialogs,
        Action<Guid> activateScene)
    {
        _project = project;
        _scene = scene;
        _view = view;
        _session = session;
        _preferences = preferences;
        _dialogs = dialogs;
        _activateScene = activateScene;

        _view.Clear();

        // Straight from the model's own rule, so the panel cannot offer Snap90
        // where the model would refuse it.
        foreach (TransformChannel channel in Channels)
            _view.SetAllowedModes(channel, FieldModeHelper.GetAllowedModes(channel));

        Refresh();

        _project.Changed += OnProjectChanged;
        _scene.Changed += OnSceneChanged;
        _session.SelectionChanged += Refresh;
        _session.ToolChanged += OnToolChanged;
        _view.ValueEdited += OnValueEdited;
        _view.ModeEdited += OnModeEdited;
        _view.SymmetryEdited += OnSymmetryEdited;
        _view.MirrorRequested += OnMirrorRequested;
        _view.CameraLensEdited += OnCameraLensEdited;
        _view.ReferenceImageEdited += OnReferenceImageEdited;
        _view.ReferenceModelEdited += OnReferenceModelEdited;
        _view.ReferencePathChangeRequested += OnReferencePathChangeRequested;
        _view.ActiveCameraRequested += OnActiveCameraRequested;
        _view.PivotEdited += OnPivotEdited;
        _view.PivotCenterRequested += OnPivotCenterRequested;
        _view.SetPresetOptions(
            MaterialPresetTable.All
                .Select(preset => new MaterialPresetOption(
                    preset,
                    MaterialLabels.For(preset),
                    MaterialPresetTable.Get(preset).Group))
                .ToList(),
            // Standard is shown and not offered: it is not a facture anybody
            // chooses, it is what a material the editor ships with wears, and
            // picking it on a material of one's own would look up a pack entry
            // under a name the pack has never heard of. It is still listed, because
            // a material can wear it — a duplicate of a shipped one, or one an
            // agent set — and a chooser with no row for it went on showing the
            // previous material's finish.
            Enum.GetValues<MaterialFinish>()
                .Select(finish => new MaterialFinishOption(
                    finish,
                    MaterialLabels.For(finish),
                    Choosable: finish != MaterialFinish.Standard))
                .ToList());

        // From the file rather than from the session: which depth somebody works
        // at, which notation they read a colour in and whether they keep the
        // preview open are how they like to work, and a preference that reset with
        // the window would have to be chosen again every morning. Panel folds and
        // the render card's own dials are stored on exactly these terms.
        _view.SetMaterialDepth(preferences.MaterialAdvanced);
        _view.SetColorFormat(preferences.MaterialColorFormat);
        _view.SetPreviewShown(preferences.MaterialPreviewShown);

        _view.ColorEdited += OnColorEdited;
        _view.PresetChosen += OnPresetChosen;
        _view.AppearanceEdited += OnAppearanceEdited;
        _view.AdvancedChosen += OnAdvancedChosen;
        _view.ColorFormatChosen += OnColorFormatChosen;
        _view.PreviewShownChanged += OnPreviewShownChanged;
        _view.TransparencyEdited += OnTransparencyEdited;
        _view.ColorEditBegan += OnColorEditBegan;
        _view.ColorEditEnded += OnColorEditEnded;
        _view.ToolSelected += OnToolSelected;
        _view.MakePrefabRequested += OnMakePrefabRequested;
        _view.OpenPrefabRequested += OnOpenPrefabRequested;
        _view.UnpackRequested += OnUnpackRequested;
    }

    protected override void OnDispose()
    {
        // A gesture must never outlive the presenter that opened it: an open
        // batch swallows every later edit, and the next one to be opened is
        // refused as a nested batch.
        OnColorEditEnded();

        _project.Changed -= OnProjectChanged;
        _scene.Changed -= OnSceneChanged;
        _session.SelectionChanged -= Refresh;
        _session.ToolChanged -= OnToolChanged;
        _view.ValueEdited -= OnValueEdited;
        _view.ModeEdited -= OnModeEdited;
        _view.SymmetryEdited -= OnSymmetryEdited;
        _view.MirrorRequested -= OnMirrorRequested;
        _view.CameraLensEdited -= OnCameraLensEdited;
        _view.ReferenceImageEdited -= OnReferenceImageEdited;
        _view.ReferenceModelEdited -= OnReferenceModelEdited;
        _view.ReferencePathChangeRequested -= OnReferencePathChangeRequested;
        _view.ActiveCameraRequested -= OnActiveCameraRequested;
        _view.PivotEdited -= OnPivotEdited;
        _view.PivotCenterRequested -= OnPivotCenterRequested;
        _view.ColorEdited -= OnColorEdited;
        _view.PresetChosen -= OnPresetChosen;
        _view.AppearanceEdited -= OnAppearanceEdited;
        _view.AdvancedChosen -= OnAdvancedChosen;
        _view.ColorFormatChosen -= OnColorFormatChosen;
        _view.PreviewShownChanged -= OnPreviewShownChanged;
        _view.TransparencyEdited -= OnTransparencyEdited;
        _view.ColorEditBegan -= OnColorEditBegan;
        _view.ColorEditEnded -= OnColorEditEnded;
        _view.ToolSelected -= OnToolSelected;
        _view.MakePrefabRequested -= OnMakePrefabRequested;
        _view.OpenPrefabRequested -= OnOpenPrefabRequested;
        _view.UnpackRequested -= OnUnpackRequested;
    }

    private void OnSceneChanged(SceneChange change)
    {
        // A change can rebuild this presenter part-way through its own dispatch;
        // see PresenterBase.
        if (!IsAlive)
            return;

        // Which camera renders is a fact about the scene rather than about one
        // element, and the change names the winner alone — so the loser's own row
        // has to be refreshed by asking rather than by matching an id.
        if (change.Kind == SceneChangeKind.ActiveCameraChanged)
        {
            Refresh();

            return;
        }

        if (_session.ActiveTarget != new SelectionTarget(SelectionKind.Element, change.ElementId))
            return;

        if (change.Kind is SceneChangeKind.TransformChanged
            or SceneChangeKind.CameraChanged
            or SceneChangeKind.ElementRenamed
            or SceneChangeKind.SymmetryChanged
            or SceneChangeKind.PivotChanged
            // A Centre pivot is the middle of the cells, so painting one moves
            // the number this panel is showing even though nothing was typed.
            or SceneChangeKind.VoxelsChanged)
        {
            Refresh();
        }
    }

    private void OnProjectChanged(ProjectChange change)
    {
        if (IsAlive && change.TargetId == _session.ActiveTarget.Id)
            Refresh();
    }

    private void Refresh()
    {
        if (!IsAlive)
            return;

        SelectionTarget selection = _session.ActiveTarget;

        switch (selection.Kind)
        {
            case SelectionKind.Element:
                ShowElement(selection.Id);
                break;

            case SelectionKind.Scene:
                ShowScene(selection.Id);
                break;

            case SelectionKind.Material:
                ShowMaterial(selection.Id);
                break;

            default:
                _view.Clear();
                break;
        }
    }

    private void ShowElement(Guid id)
    {
        if (!_scene.TryGetElement(id, out IHierarchyElement element))
        {
            _view.Clear();

            return;
        }

        _view.SetTarget(
            element.Name,
            element.Component?.Kind ?? ComponentKind.None,
            element.Transform,
            element.TransformModes);

        // Every element, including a group: it is the group whose origin sits
        // furthest from what it holds, so it is the one that needs this most.
        // The row speaks in offsets from the middle of the content — a fresh
        // element's local cells are numbered from the scene's corner, which is a
        // point nobody chose — through the same conversion the command surface
        // uses, so the two cannot disagree about what a zero means.
        _view.SetPivot(PivotUnits.ToOffset(element.PivotPoint, element));

        // Where the element stands with respect to the library. A prefab
        // instance's contents are neither a candidate to become a prefab nor an
        // instance to unpack: they are read-only here, and what the panel offers
        // instead is the way to where they can be changed — said before anybody
        // types into a row that would refuse, not after.
        if (element.PrefabInstanceId is { } owner)
            _view.SetPrefabContent(GetPrefabNameOf(owner) ?? "?");
        else
            _view.SetPrefab(GetSourceName(element));

        // Neither the tools nor the mirrors for contents: there is nothing a brush
        // may paint into and no plane worth moving, so the strip would be buttons
        // that fall straight back to the cursor.
        if (element.Component is IVoxelModel model && element.PrefabInstanceId == null)
        {
            _view.SetTools(_session.ActiveTool);

            // The rows speak in offsets from the pivot — 0 is a mirror through
            // it — while the model stores absolute cells. Same conversion the
            // command surface makes, from the same class, so the two cannot
            // disagree about what a zero means.
            _view.SetSymmetry(SymmetryUnits.ToOffsets(model.Symmetry, element.PivotPoint));
        }

        // No tools section for a camera and no symmetry: it holds no cells, so
        // there is nothing for either to act on. What it does have is a lens, and
        // the one switch that says whether the picture comes through it.
        if (element.Component is ICamera camera)
            _view.SetCamera(camera.Lens, _scene.ActiveCameraId == element.Id);

        // Nor for a reference, and for the same reason. What it has is a file, and
        // whether that file is actually there — which is the first thing anybody
        // looking at a blank reference needs to be told.
        if (element.Component is IReferenceImage image)
            _view.SetReferenceImage(image.Settings, ReferenceFile.Exists(image.Settings.Path));

        if (element.Component is IReferenceModel reference)
            _view.SetReferenceModel(
                reference.Settings,
                ReferenceFile.Exists(reference.Settings.Path));
    }

    /// <summary>
    /// The whole lens, as the field composed it. The presenter does not merge it
    /// with what the model holds: the field already did that, from the lens it was
    /// last given, which is what keeps a spin box's quantising off the four values
    /// nobody touched.
    /// </summary>
    private void OnCameraLensEdited(CameraLens lens)
    {
        SelectionTarget target = _session.ActiveTarget;

        if (target.Kind == SelectionKind.Element)
            _scene.SetCameraLens(target.Id, lens);
    }

    /// <inheritdoc cref="OnCameraLensEdited"/>
    private void OnReferenceImageEdited(ReferenceImageSettings settings)
    {
        SelectionTarget target = _session.ActiveTarget;

        if (target.Kind == SelectionKind.Element)
            _scene.SetReferenceImage(target.Id, settings);
    }

    /// <inheritdoc cref="OnCameraLensEdited"/>
    private void OnReferenceModelEdited(ReferenceModelSettings settings)
    {
        SelectionTarget target = _session.ActiveTarget;

        if (target.Kind == SelectionKind.Element)
            _scene.SetReferenceModel(target.Id, settings);
    }

    /// <summary>
    /// Points the selected reference at another file. The picker runs in the
    /// document's own layer rather than the shell's, on the project tree's
    /// precedent: what comes back is an edit to this scene and nothing else.
    /// </summary>
    private async void OnReferencePathChangeRequested()
    {
        SelectionTarget target = _session.ActiveTarget;

        if (target.Kind != SelectionKind.Element
            || !_scene.TryGetElement(target.Id, out IHierarchyElement element))
        {
            return;
        }

        string? path = await _dialogs.AskPathAsync(
            ReferenceDialogs.Open("Point this reference at", element.ComponentKind));

        // A picker outlives the panel that raised it: the document may have been
        // closed, the scene left, or the element deleted while it was up.
        if (path == null || !IsAlive || !_scene.TryGetElement(target.Id, out element))
            return;

        if (element.Component is IReferenceImage image)
            _scene.SetReferenceImage(target.Id, image.Settings with { Path = path });
        else if (element.Component is IReferenceModel reference)
            _scene.SetReferenceModel(target.Id, reference.Settings with { Path = path });
    }

    private void OnActiveCameraRequested()
    {
        SelectionTarget target = _session.ActiveTarget;

        if (target.Kind == SelectionKind.Element)
            _scene.SetActiveCamera(target.Id);
    }

    /// <summary>
    /// Any scene, not only the one being worked on: a row in the tree is worth
    /// inspecting before deciding to open it.
    /// </summary>
    private void ShowScene(Guid id)
    {
        if (_project.TryGetScene(id, out IScene scene))
            _view.SetSceneTarget(scene.Name, scene.Size);
        else
            _view.Clear();
    }

    private void ShowMaterial(Guid id)
    {
        if (_project.TryGetMaterial(id, out IMaterial material))
        {
            _view.SetMaterialTarget(
                material.Name,
                material.Color,
                material.Appearance,
                MaterialPresetTable.Match(material.Appearance));
        }
        else
            _view.Clear();
    }

    /// <summary>
    /// The name of the prefab an instance stands for, or null for anything else —
    /// which is what the section shows a "Make prefab" button for instead.
    /// </summary>
    /// <summary>The prefab whose instance <paramref name="instanceId"/> is, by name.</summary>
    private string? GetPrefabNameOf(Guid instanceId) =>
        _scene.TryGetElement(instanceId, out IHierarchyElement instance)
            ? GetSourceName(instance)
            : null;

    private string? GetSourceName(IHierarchyElement element)
    {
        if (element.Component is not IPrefabInstance instance)
            return null;

        return _project.TryGetPrefab(instance.SourceId, out IPrefab prefab) ? prefab.Name : null;
    }

    /// <summary>
    /// Lifts the selected element into the library. The element it was is gone
    /// afterwards — an instance stands in its place, at the same index under the
    /// same parent — so the selection is moved onto that, which is what the user
    /// was looking at a moment ago and expects to go on working with.
    /// </summary>
    private void OnMakePrefabRequested()
    {
        if (!IsAlive || !TryGetSelectedElement(out IHierarchyElement element))
            return;

        if (element.Component is IPrefabInstance)
            return;

        IHierarchyContainer parent = element.Parent;
        var index = IndexOf(parent, element.Id);

        _session.EndGesture();
        _scene.CreatePrefab(element.Id);

        if (index >= 0 && index < parent.Children.Count)
        {
            _session.SetSelection(
                new SelectionTarget(SelectionKind.Element, parent.Children[index].Id));
        }
    }

    /// <summary>
    /// Opens the source for editing. The same move as activating a scene row, and
    /// literally the same call — a prefab's contents are a scene.
    /// </summary>
    /// <summary>
    /// Opens the prefab the selection is about: the one an instance stands for, or
    /// the one a read-only element is part of — the instance whose contents it is.
    /// </summary>
    private void OnOpenPrefabRequested()
    {
        if (!IsAlive || FindPrefabToOpen() is not { } prefabId)
            return;

        _session.EndGesture();
        _activateScene(prefabId);
    }

    private Guid? FindPrefabToOpen()
    {
        SelectionTarget selection = _session.ActiveTarget;

        if (selection.Kind != SelectionKind.Element
            || !_scene.TryGetElement(selection.Id, out IHierarchyElement element))
        {
            return null;
        }

        if (element.PrefabInstanceId is { } owner)
        {
            return _scene.TryGetElement(owner, out IHierarchyElement instance)
                && instance.Component is IPrefabInstance ownerInstance
                    ? ownerInstance.SourceId
                    : null;
        }

        return element.Component is IPrefabInstance standing ? standing.SourceId : null;
    }

    private void OnUnpackRequested()
    {
        if (!IsAlive
            || !TryGetSelectedElement(out IHierarchyElement element)
            || element.Component is not IPrefabInstance)
        {
            return;
        }

        _session.EndGesture();
        _scene.UnpackInstance(element.Id);
    }

    /// <summary>
    /// The selected element, when what is selected is an element of this scene that
    /// may be acted on — prefab contents are read-only wherever they appear.
    /// </summary>
    private bool TryGetSelectedElement(out IHierarchyElement element)
    {
        element = null!;

        SelectionTarget selection = _session.ActiveTarget;

        return selection.Kind == SelectionKind.Element
            && _scene.TryGetElement(selection.Id, out element)
            && element.PrefabInstanceId == null;
    }

    private static int IndexOf(IHierarchyContainer parent, Guid elementId)
    {
        for (var index = 0; index < parent.Children.Count; index++)
        {
            if (parent.Children[index].Id == elementId)
                return index;
        }

        return -1;
    }

    /// <summary>
    /// The same request the tool keys make, from a button instead of a key —
    /// including taking the keyboard to the viewport, where the tool acts. The
    /// strip is only shown for an element the tools act on, so no gate here.
    /// </summary>
    private void OnToolSelected(ToolKind tool)
    {
        if (IsAlive)
            _session.RequestTool(tool);
    }

    private void OnToolChanged()
    {
        if (IsAlive)
            _view.SetActiveTool(_session.ActiveTool);
    }

    private void OnValueEdited(TransformChannel channel, Vector3 value)
    {
        if (_session.ActiveTarget.Kind == SelectionKind.Element)
            _scene.SetTransform(_session.ActiveTarget.Id, channel, value);
    }

    private void OnModeEdited(TransformChannel channel, FieldMode mode)
    {
        if (_session.ActiveTarget.Kind == SelectionKind.Element)
            _scene.SetFieldMode(_session.ActiveTarget.Id, channel, mode);
    }

    /// <summary>
    /// The half-cell rounding is a decision, so it happens here rather than
    /// being trusted to the spin box's step — the view reports what it holds,
    /// and the model receives only what a mirror plane can be. What the row
    /// holds is an <b>offset from the pivot</b>, converted to the absolute
    /// plane the model stores.
    /// </summary>
    private void OnSymmetryEdited(Vector3.Axis axis, bool enabled, float position)
    {
        if (!TryGetWritableModel(out IHierarchyElement element, out IVoxelModel model))
            return;

        // Away from zero, not banker's: Godot's own Round taught this codebase
        // what a midpoint quietly snapping to even looks like on a grid.
        var offsetDoubled = (int)Math.Round(position * 2f, MidpointRounding.AwayFromZero);

        var slot = new SymmetryPlane(
            enabled,
            SymmetryUnits.ToAbsoluteDoubled(offsetDoubled, element.PivotPoint, (int)axis));

        _scene.SetSymmetry(element.Id, WithSymmetrySlot(model.Symmetry, axis, slot));
    }

    /// <summary>
    /// "Mirror now" for one row: the image of every cell across the plane the row
    /// shows — on or off, a disabled slot keeping its position — written where
    /// empty, as one record. The plane and the cells come from the same two
    /// functions the <c>mirror-element</c> command asks, so the button and the
    /// command cannot come to write different cells. A symmetric model writes
    /// nothing, and so records nothing.
    /// </summary>
    private void OnMirrorRequested(Vector3.Axis axis)
    {
        if (!TryGetWritableModel(out IHierarchyElement element, out IVoxelModel model))
            return;

        int planeDoubled =
            SymmetryUnits.ShownPlaneDoubled(model.Symmetry, element.PivotPoint, (int)axis);

        IReadOnlyList<(Vector3I Cell, Guid Material)> images =
            VoxelSymmetry.CollectMirrorImages(model.Voxels, (int)axis, planeDoubled);

        var editsList = new List<VoxelEdit>(images.Count);

        foreach ((Vector3I cell, Guid material) in images)
            editsList.Add(new VoxelEdit(cell, material));

        _scene.SetVoxels(element.Id, editsList);
    }

    /// <summary>
    /// The selected voxel model, when there is one and it may be written to.
    /// Prefab-instance contents are read-only wherever they appear and the model
    /// refuses them by throwing — which, raised out of a button press or a field
    /// edit, is a backtrace in the log and a control that silently did nothing.
    /// The rows are still shown on such an element, because it is inspectable;
    /// what they cannot do is write.
    /// </summary>
    private bool TryGetWritableModel(out IHierarchyElement element, out IVoxelModel model)
    {
        element = null!;
        model = null!;

        if (_session.ActiveTarget.Kind != SelectionKind.Element
            || !_scene.TryGetElement(_session.ActiveTarget.Id, out IHierarchyElement found)
            || found.PrefabInstanceId != null
            || found.Component is not IVoxelModel voxels)
        {
            return false;
        }

        element = found;
        model = voxels;

        return true;
    }

    /// <summary>
    /// The row holds an offset from the content's middle; the model stores the
    /// absolute point. The half-cell rounding still belongs to the model, which
    /// owns the lattice the pivot lives on.
    /// </summary>
    private void OnPivotEdited(Vector3 point)
    {
        if (_session.ActiveTarget.Kind == SelectionKind.Element
            && _scene.TryGetElement(_session.ActiveTarget.Id, out IHierarchyElement element))
        {
            _scene.SetPivot(element.Id, PivotUnits.ToAbsolute(point, element));
        }
    }

    /// <summary>
    /// The one-shot "centre it now". Computed here rather than in the view — the
    /// middle of a model is document knowledge — and through the same
    /// <see cref="PivotCenter"/> the command surface's <c>point=center</c> asks,
    /// so the button and the command cannot disagree. Nothing to centre on
    /// changes nothing, which is what an empty group's button should do.
    /// </summary>
    private void OnPivotCenterRequested()
    {
        if (_session.ActiveTarget.Kind == SelectionKind.Element
            && _scene.TryGetElement(_session.ActiveTarget.Id, out IHierarchyElement element)
            && PivotCenter.TryCalculate(element, out Vector3 center))
        {
            _scene.SetPivot(element.Id, center);
        }
    }

    private static SymmetryValues WithSymmetrySlot(
        SymmetryValues values,
        Vector3.Axis axis,
        SymmetryPlane slot) =>
        axis switch
        {
            Vector3.Axis.X => values with { X = slot },
            Vector3.Axis.Y => values with { Y = slot },
            Vector3.Axis.Z => values with { Z = slot },
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };

    private void OnColorEdited(Color color)
    {
        if (_session.ActiveTarget.Kind == SelectionKind.Material)
            _project.SetMaterialColor(_session.ActiveTarget.Id, color);
    }

    /// <summary>
    /// One click, one record — no gesture to batch, unlike the colour's
    /// per-frame stream from an open picker.
    /// </summary>
    private void OnPresetChosen(MaterialPreset preset)
    {
        if (_session.ActiveTarget.Kind == SelectionKind.Material)
            _project.SetMaterialPreset(_session.ActiveTarget.Id, preset);
    }

    private void OnAppearanceEdited(MaterialAppearance appearance)
    {
        if (_session.ActiveTarget.Kind == SelectionKind.Material)
            _project.SetMaterialAppearance(_session.ActiveTarget.Id, appearance);
    }

    /// <summary>
    /// Which depth the panel is at is not the document's business, so it leaves no
    /// history record — and it is not the session's either: it outlives the window,
    /// like every other answer to "how do you like to work". The panel is told back
    /// rather than left to assume, on the same terms as every other push.
    /// </summary>
    private void OnAdvancedChosen(bool advanced)
    {
        _preferences.ApplyMaterialAdvanced(advanced);
        _view.SetMaterialDepth(advanced);
    }

    private void OnColorFormatChosen(ColorTextFormat format) =>
        _preferences.ApplyMaterialColorFormat(format);

    private void OnPreviewShownChanged(bool shown)
    {
        _preferences.ApplyMaterialPreviewShown(shown);
        _view.SetPreviewShown(shown);
    }

    private void OnTransparencyEdited(bool transparent)
    {
        if (_session.ActiveTarget.Kind != SelectionKind.Material)
            return;

        if (!_project.TryGetMaterial(_session.ActiveTarget.Id, out IMaterial material))
            return;

        _project.SetMaterialAppearance(
            material.Id, material.Appearance with { Transparent = transparent });
    }

    /// <summary>
    /// Dragging across the picker's gradient reports a colour every frame, and
    /// all of them are one thing the user did. The scope collects them into a
    /// single record — a gesture that changed nothing leaves none at all.
    /// </summary>
    /// <remarks>
    /// A material belongs to the project rather than to a scene, so the record is
    /// stamped with no scene: there is nowhere for an undo of it to take the
    /// editor, and every scene shows the result anyway.
    /// </remarks>
    private void OnColorEditBegan()
    {
        if (_colorGestureOpen
            || _session.ActiveTarget.Kind != SelectionKind.Material
            || !_project.TryGetMaterial(_session.ActiveTarget.Id, out IMaterial material))
        {
            return;
        }

        // Handed to the session, not held here: whatever the user does next has
        // to be able to close it without knowing which panel opened it.
        _session.BeginGesture(_project.History.CreateBatch(
            ActionKind.ChangeMaterial,
            Guid.Empty,
            material.Id,
            material.Name));

        _colorGestureOpen = true;
    }

    /// <summary>
    /// Ends only a gesture this panel opened. Somebody else may already have
    /// closed it, which is why the session's own call is a no-op when there is
    /// nothing open.
    /// </summary>
    private void OnColorEditEnded()
    {
        if (!_colorGestureOpen)
            return;

        _colorGestureOpen = false;
        _session.EndGesture();
    }
}
