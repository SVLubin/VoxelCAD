using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Preferences;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>Mirrors the scene into the 3D viewport, and the project's materials with it.</summary>
/// <remarks>
/// Only local transforms are pushed. The view keeps an engine node per element in
/// the same shape as the document tree, so world transforms are the engine's job
/// and are never computed — let alone cached — on this side.
/// </remarks>
internal sealed class ViewportPresenter : PresenterBase
{
    private readonly IProject _project;
    private readonly IScene _scene;
    private readonly ISceneView _view;
    private readonly EditorSession _session;
    private readonly IEditorPreferences _preferences;

    // Which ids the view has already been given a material for, so a shipped one
    // is built once however many cells come to wear it.
    private readonly HashSet<Guid> _pushedStandardHashSet = new();

    public ViewportPresenter(
        IProject project,
        IScene scene,
        ISceneView view,
        EditorSession session,
        IEditorPreferences preferences)
    {
        _project = project;
        _scene = scene;
        _view = view;
        _session = session;
        _preferences = preferences;

        _view.Clear();
        _view.SetRoot(scene.Id, scene.Size);

        PushCameraAspect();

        // Before any material is pushed, because a material's look is derived from
        // these — pushed afterwards, every one of them would be built twice.
        PushViewportEffects();

        // The project's own, and then whichever shipped materials the scene
        // actually wears. Not the whole library: building one costs its maps —
        // five images decoded and a normal differentiated out of the height — so
        // pushing three hundred at every document open would decode fifteen
        // hundred images to draw a model that wears two of them.
        foreach (IMaterial material in project.Materials)
            PushMaterial(material);

        foreach (IHierarchyElement child in scene.Children)
            AddSubtree(child, scene.Id);

        // After the elements, so a scene opened with the switch already off does
        // not draw its references for a frame first.
        PushReferencesVisible();

        _scene.Changed += OnSceneChanged;
        _project.Changed += OnProjectChanged;
        _preferences.Changed += OnPreferencesChanged;
        _session.ReferencesVisibleChanged += PushReferencesVisible;
        _view.FocusRequested += OnFocusRequested;
    }

    protected override void OnDispose()
    {
        _scene.Changed -= OnSceneChanged;
        _project.Changed -= OnProjectChanged;
        _preferences.Changed -= OnPreferencesChanged;
        _session.ReferencesVisibleChanged -= PushReferencesVisible;
        _view.FocusRequested -= OnFocusRequested;
    }

    /// <summary>
    /// Framing follows the selection, and falls back to the scene — which frames
    /// the buildable region, so the key doubles as "put me back where I can see
    /// everything" when nothing is selected.
    /// </summary>
    /// <remarks>
    /// A material or a scene row is selected in the same field as an element, so
    /// the kind is checked before the id: a material's id names nothing in this
    /// scene, and framing whatever it happened to collide with would be worse
    /// than framing nothing.
    /// </remarks>
    private void OnFocusRequested()
    {
        if (!IsAlive)
            return;

        SelectionTarget selection = _session.ActiveTarget;

        bool hasElement = selection.Kind == SelectionKind.Element
            && _scene.TryGetElement(selection.Id, out _);

        _view.FocusOn(hasElement ? selection.Id : _scene.Id);
    }

    private void OnSceneChanged(SceneChange change)
    {
        // A change can rebuild this presenter part-way through its own dispatch;
        // see PresenterBase.
        if (!IsAlive)
            return;

        switch (change.Kind)
        {
            case SceneChangeKind.ElementAdded:
                if (_scene.TryGetElement(change.ElementId, out IHierarchyElement added))
                    AddSubtree(added, change.ParentId);
                break;

            case SceneChangeKind.ElementRemoved:
                _view.RemoveElement(change.ElementId);
                break;

            case SceneChangeKind.ElementMoved:
                // Engine nodes could be reparented, but rebuilding keeps this in
                // step with the hierarchy panel, which has no such luxury.
                _view.RemoveElement(change.ElementId);

                if (_scene.TryGetElement(change.ElementId, out IHierarchyElement moved))
                    AddSubtree(moved, change.ParentId);
                break;

            case SceneChangeKind.TransformChanged:
                if (_scene.TryGetElement(change.ElementId, out IHierarchyElement transformed))
                {
                    _view.SetElementTransform(
                        change.ElementId, transformed.Transform, transformed.PivotPoint);
                }
                break;

            // The pivot moved, the geometry did not — but Position names the
            // pivot, so the pair the view composes the node's origin from has
            // changed in both halves at once. Re-pushing lands on the same
            // origin; skipping would leave the view holding a stale pair.
            case SceneChangeKind.PivotChanged:
                if (_scene.TryGetElement(change.ElementId, out IHierarchyElement repivoted))
                {
                    _view.SetElementTransform(
                        change.ElementId, repivoted.Transform, repivoted.PivotPoint);
                }
                break;

            case SceneChangeKind.VoxelsChanged:
                PushNewlyUsedMaterials(change.ElementId, change.Region);
                PushVoxels(change.ElementId, change.Region);
                break;

            case SceneChangeKind.SymmetryChanged:
                PushSymmetry(change.ElementId);
                break;

            // Only the element that changed is pushed. What is under it follows
            // in the view for the same reason it follows in the model: the nodes
            // are the tree, and the engine draws nothing under something hidden.
            case SceneChangeKind.VisibilityChanged:
                if (_scene.TryGetElement(change.ElementId, out IHierarchyElement shown))
                    _view.SetElementVisible(change.ElementId, shown.Visible);
                break;

            case SceneChangeKind.ReferenceChanged:
                PushReference(change.ElementId);
                break;

            case SceneChangeKind.CameraChanged:
                PushCamera(change.ElementId);
                break;

            // Which camera renders is a fact about two of them at once — the one
            // that gained it and the one that lost it — and the change names only
            // the winner, so every camera is re-dressed rather than guessed at.
            case SceneChangeKind.ActiveCameraChanged:
                PushEveryCamera();
                break;
        }

        // A camera added, removed or moved changes which one the scene falls back
        // to, and the fallback is answered rather than announced — so the three
        // kinds that can move it re-dress the lot. Below the switch because the
        // element has to be in the view before it can wear a gizmo.
        if (change.Kind is SceneChangeKind.ElementAdded
            or SceneChangeKind.ElementRemoved
            or SceneChangeKind.ElementMoved)
        {
            PushEveryCamera();
        }
    }

    private void OnProjectChanged(ProjectChange change)
    {
        if (!IsAlive)
            return;

        switch (change.Kind)
        {
            case ProjectChangeKind.MaterialAdded:
            case ProjectChangeKind.MaterialChanged:
                if (_project.TryGetMaterial(change.TargetId, out IMaterial material))
                    _view.SetMaterial(material.Id, material.Name, material.Color, material.Appearance);
                break;

            case ProjectChangeKind.MaterialRemoved:
                _view.RemoveMaterial(change.TargetId);
                break;
        }
    }

    /// <summary>
    /// How big the picture would be, which is what a frustum has to be drawn at:
    /// what the wireframe encloses must be what the render will contain.
    /// </summary>
    private void OnPreferencesChanged()
    {
        if (!IsAlive)
            return;

        PushCameraAspect();
        PushViewportEffects();
    }

    /// <summary>
    /// Tells the viewport which of its optional effects are running. Pushed rather
    /// than read, on the ordinary seam rule: the view knows nothing about where a
    /// preference is kept.
    /// </summary>
    private void PushViewportEffects() =>
        _view.SetViewportEffects(new ViewportEffects(
            Occlusion: _preferences.ViewportOcclusion,
            Shadows: _preferences.ViewportShadows,
            Factures: _preferences.ViewportFactures));

    private void PushCameraAspect()
    {
        RenderDials dials = _preferences.RenderDials;

        _view.SetCameraAspect(dials.Height <= 0 ? 1f : dials.Width / (float)dials.Height);
    }

    private void PushEveryCamera()
    {
        foreach (IHierarchyElement child in _scene.Children)
            PushCameras(child);
    }

    private void PushCameras(IHierarchyElement element)
    {
        PushCamera(element.Id);

        foreach (IHierarchyElement child in element.Children)
            PushCameras(child);
    }

    private void PushReferencesVisible()
    {
        if (IsAlive)
            _view.SetReferencesVisible(_session.ReferencesVisible);
    }

    /// <summary>
    /// What a reference stands for, or null on both counts where the element is
    /// not one — which is as much a fact the view needs as the other two, since an
    /// element it has never been told about would keep whatever it was last told.
    /// </summary>
    private void PushReference(Guid elementId)
    {
        if (!_scene.TryGetElement(elementId, out IHierarchyElement element))
            return;

        _view.SetElementReference(
            elementId,
            (element.Component as IReferenceImage)?.Settings,
            (element.Component as IReferenceModel)?.Settings);
    }

    private void PushCamera(Guid elementId)
    {
        if (!_scene.TryGetElement(elementId, out IHierarchyElement element))
            return;

        _view.SetElementCamera(
            elementId,
            (element.Component as ICamera)?.Lens,
            _scene.ActiveCameraId == elementId);
    }

    /// <summary>
    /// An element and everything under it, arriving in the view.
    /// </summary>
    /// <remarks>
    /// The shipped materials it wears go first, and that is the whole of this
    /// wrapper. Only the constructor used to push them, so a subtree that arrived
    /// <em>afterwards</em> — a paste, an instantiated prefab, an element moved
    /// (which is a remove and an add) — brought none, and its cells drew with no
    /// material at all. The order matters as much as the call: the view builds a
    /// mesh out of the cells it is given, so a material handed over after them is
    /// handed over after the mesh that needed it.
    /// <para>
    /// Once for the subtree rather than per node, because the walk below recurses
    /// too and the two together would re-walk every element once per ancestor.
    /// </para>
    /// </remarks>
    private void AddSubtree(IHierarchyElement element, Guid parentId)
    {
        PushUsedStandardMaterials(element);
        AddNodes(element, parentId);
    }

    private void AddNodes(IHierarchyElement element, Guid parentId)
    {
        _view.AddElement(element.Id, parentId);
        _view.SetElementTransform(element.Id, element.Transform, element.PivotPoint);
        _view.SetElementVisible(element.Id, element.Visible);

        PushVoxels(element.Id);
        PushSymmetry(element.Id);
        PushCamera(element.Id);
        PushReference(element.Id);

        foreach (IHierarchyElement child in element.Children)
            AddNodes(child, element.Id);
    }

    /// <summary>One material, as the document describes it.</summary>
    private void PushMaterial(IMaterial material) =>
        _view.SetMaterial(material.Id, material.Name, material.Color, material.Appearance);

    /// <summary>
    /// Every shipped material worn anywhere under an element — walked when the
    /// scene is bound, and again whenever a subtree arrives.
    /// </summary>
    private void PushUsedStandardMaterials(IHierarchyElement element)
    {
        if (element.Component is IVoxelModel model)
        {
            foreach (Guid id in model.Voxels.Values)
                PushIfNewStandard(id);
        }

        foreach (IHierarchyElement child in element.Children)
            PushUsedStandardMaterials(child);
    }

    /// <summary>
    /// A shipped material the view has not been told about yet, because it came
    /// into use only now — painting with one is what puts it in the scene, and the
    /// view builds its maps the first time it is asked.
    /// </summary>
    /// <remarks>
    /// Only the cells the edit reached are looked at, which is what the region is
    /// for: re-walking the model to find out whether a new material turned up would
    /// undo the whole point of carrying the region in the first place.
    /// </remarks>
    private void PushNewlyUsedMaterials(Guid elementId, CellRegion? region)
    {
        if (!_scene.TryGetElement(elementId, out IHierarchyElement element)
            || element.Component is not IVoxelModel model)
        {
            return;
        }

        if (region is not CellRegion box)
        {
            foreach (Guid id in model.Voxels.Values)
                PushIfNewStandard(id);

            return;
        }

        for (int z = box.Min.Z; z <= box.Max.Z; z++)
        {
            for (int y = box.Min.Y; y <= box.Max.Y; y++)
            {
                for (int x = box.Min.X; x <= box.Max.X; x++)
                {
                    if (model.Voxels.TryGetValue(new Vector3I(x, y, z), out Guid id))
                        PushIfNewStandard(id);
                }
            }
        }
    }

    private void PushIfNewStandard(Guid id)
    {
        if (!_pushedStandardHashSet.Add(id))
            return;

        if (_project.TryGetMaterial(id, out IMaterial material) && material.IsStandard)
            PushMaterial(material);
    }

    private void PushVoxels(Guid elementId, CellRegion? region = null)
    {
        if (_scene.TryGetElement(elementId, out IHierarchyElement element)
            && element.Component is IVoxelModel model)
        {
            _view.SetElementVoxels(elementId, model.Voxels, region);
        }
    }

    private void PushSymmetry(Guid elementId)
    {
        if (_scene.TryGetElement(elementId, out IHierarchyElement element)
            && element.Component is IVoxelModel model)
        {
            _view.SetElementSymmetry(elementId, model.Symmetry);
        }
    }
}
