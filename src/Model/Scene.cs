using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Geometry;

namespace VoxelCAD.Model;

/// <inheritdoc cref="IScene"/>
/// <remarks>
/// Public methods validate, build a command and hand it to <see cref="History"/>.
/// The internal mutators below are what commands actually call in both directions,
/// so undo produces the same change events as the original edit.
/// </remarks>
internal sealed class Scene : IScene
{
    internal static readonly Vector3I DefaultSize = new(32, 32, 32);

    private readonly Project _project;
    private readonly HierarchyContainer _children;
    private readonly Dictionary<Guid, HierarchyElement> _elementsByIdDictionary = new();
    private readonly History _history;

    // The camera somebody chose, which is not the same thing as the camera that
    // renders: the answer falls back to the first one the scene holds, so this may
    // name an element that has been deleted and is waiting for an undo to bring it
    // back. Empty means nobody has chosen.
    private Guid _chosenCameraId;

    public Scene(Project project, History history, Guid id, string name, Vector3I size)
    {
        _project = project;
        _history = history;
        Id = id;
        Name = name;
        Size = size;
        _children = new HierarchyContainer(this);
    }

    public event Action<SceneChange>? Changed;

    public Guid Id { get; }

    public string Name { get; private set; }

    public Vector3I Size { get; }

    public IReadOnlyList<IHierarchyElement> Children => _children.Children;

    /// <inheritdoc/>
    public Guid ActiveCameraId
    {
        get
        {
            if (_chosenCameraId != Guid.Empty
                && _elementsByIdDictionary.TryGetValue(_chosenCameraId, out HierarchyElement? chosen)
                && chosen.Component is Camera)
            {
                return _chosenCameraId;
            }

            return FindFirstCamera(_children) ?? Guid.Empty;
        }
    }

    public bool TryGetElement(Guid id, out IHierarchyElement element)
    {
        if (_elementsByIdDictionary.TryGetValue(id, out HierarchyElement? found))
        {
            element = found;
            return true;
        }

        element = null!;
        return false;
    }

    public Guid CreateElement(Guid parentId, ComponentKind kind, string name)
    {
        HierarchyContainer parent = GetWritableContainer(parentId);
        HierarchyElement element = BuildElement(kind, name);

        Execute(new CreateElementCommand(
            this, element, parentId, parent.Children.Count, ActionKind.CreateElement));

        return element.Id;
    }

    /// <summary>
    /// Creates a camera already standing where it is meant to stand, as
    /// <b>one</b> record. Two things make that worth its own method rather than a
    /// create followed by three edits: a camera is placed to keep a shot, and a
    /// shot arriving over four history entries is four things to undo; and the
    /// three edits would each be quantised on the way in, so the framing would
    /// have to survive a trip through the field modes before they were set.
    /// </summary>
    /// <remarks>
    /// The values are in <paramref name="parentId"/>'s frame, like any element's
    /// transform, and are taken as given: a camera's position and rotation default
    /// to <see cref="FieldMode.Float"/> precisely so that a place and an aim are
    /// kept rather than snapped.
    /// </remarks>
    public Guid CreateCamera(
        Guid parentId,
        string name,
        Vector3 position,
        Vector3 rotationDegrees,
        CameraLens lens)
    {
        HierarchyContainer parent = GetWritableContainer(parentId);
        HierarchyElement element = BuildElement(ComponentKind.Camera, name);

        element.ElementTransform.Set(TransformChannel.Position, position);
        element.ElementTransform.Set(TransformChannel.Rotation, rotationDegrees);
        ((Camera)element.Component!).ApplyLens(lens);

        Execute(new CreateElementCommand(
            this, element, parentId, parent.Children.Count, ActionKind.CreateElement));

        return element.Id;
    }

    public Guid CreateReference(Guid parentId, string name, string path)
    {
        HierarchyContainer parent = GetWritableContainer(parentId);

        EnsureAbsolute(path);

        if (!ReferenceFormats.TryGetKind(path, out ComponentKind kind))
        {
            throw new ArgumentException(
                $"Nothing here can stand for '{path}'. "
                + $"Pictures: {string.Join(", ", ReferenceFormats.ImageExtensions)}. "
                + $"Models: {string.Join(", ", ReferenceFormats.ModelExtensions)}.",
                nameof(path));
        }

        HierarchyElement element = BuildElement(kind, name);

        if (element.Component is ReferenceImage image)
        {
            // One pixel on one voxel is where a picture starts: it is what tracing
            // pixel art wants, and it is the only size that is not a guess.
            var measured = Measure(ReferenceImageSettings.Default with { Path = path });

            image.ApplySettings(measured with { SizeInCells = measured.OneToOneSize });

            // Inside the create rather than beside it, the way the first voxel
            // edit gives a model its pivot: the grip belongs on the picture, and a
            // quarter-turn about its bottom corner would swing the whole drawing
            // out of the room.
            element.ApplyPivot(RoundToHalfCells(PivotUnits.Anchor(element)));
        }
        else
            ((ReferenceModel)element.Component!).ApplySettings(
                ReferenceModelSettings.Default with { Path = path });

        Execute(new CreateElementCommand(
            this, element, parentId, parent.Children.Count, ActionKind.CreateElement));

        return element.Id;
    }

    /// <summary>
    /// The same settings carrying the picture's own resolution. The size is left
    /// alone: what the document may not do is claim a resolution the file
    /// contradicts, and how big the plane is is somebody's decision rather than a
    /// fact about the file. A picture that cannot be measured keeps the resolution
    /// it was handed — a stale number beside a missing-file mark says more than a
    /// nought.
    /// </summary>
    private static ReferenceImageSettings Measure(ReferenceImageSettings settings) =>
        ImageHeader.TryRead(settings.Path, out Vector2I pixels)
            ? settings with { PixelSize = pixels }
            : settings;

    private static HierarchyElement BuildElement(ComponentKind kind, string name)
    {
        var element = new HierarchyElement(Guid.NewGuid(), name);
        IElementComponent? component = ElementComponentFactory.Create(kind, element);

        if (component != null)
            element.SetComponent(component);

        // The modes before anything is written, or a value would be quantised by
        // the mode it is about to replace.
        element.ElementTransform.SetModes(ElementTransformDefaults.For(kind));

        return element;
    }

    public void DeleteElement(Guid id)
    {
        HierarchyElement element = GetWritableElement(id);

        Execute(new DeleteElementCommand(this, element, element.Parent.Id, IndexOf(element)));
    }

    /// <summary>
    /// Prefab content is copyable even though it is not editable: the copy is an
    /// ordinary element, and reading it was never the thing being refused.
    /// </summary>
    public ElementClip CaptureElement(Guid id)
    {
        ElementNode root = ElementClipFactory.Capture(GetElement(id));
        var nodesList = new[] { root };

        return new ElementClip(
            root,
            _project.CaptureClipMaterials(nodesList),
            _project.CaptureClipPrefabs(nodesList));
    }

    /// <summary>
    /// The element is built detached and handed to one command, which is what makes
    /// a paste one record however deep the subtree is — and what lets the materials
    /// it needs arrive in the same breath as the cells that name them.
    /// </summary>
    public Guid PasteElement(Guid parentId, ElementClip clip, string? name = null)
    {
        HierarchyContainer parent = GetWritableContainer(parentId);
        var materialsList = new List<Material>();
        var prefabsList = new List<Prefab>();
        ClipResolution resolution =
            _project.ResolveClip(clip.Materials, clip.Prefabs, materialsList, prefabsList);

        // A clip may carry instances, and this scene may be a prefab's own
        // contents — in which case pasting one of its ancestors into it would
        // close a loop. Refused before anything is built.
        _project.EnsureInstancesAllowed(Id, clip.Root, resolution);

        HierarchyElement element = ElementClipFactory.Create(
            clip.Root,
            resolution,
            name ?? CopyNameHelper.Compose(clip.Name, CollectElementNames()));

        Execute(new PasteElementCommand(
            this, _project, element, parentId, parent.Children.Count, materialsList, prefabsList));

        return element.Id;
    }

    /// <summary>
    /// Lifts the element into the project's prefab library and leaves an instance
    /// of it standing in its place.
    /// </summary>
    /// <remarks>
    /// The instance takes the element's transform, its field modes and its pivot,
    /// and the contents go to the library at identity — so what stands in the scene
    /// afterwards occupies exactly the space the element did, and the prefab's own
    /// room shows its cells where they were painted. An element with no pivot of
    /// its own is given the middle of what it holds, because the instance's handles
    /// would otherwise sit at the local origin, which for a model built anywhere in
    /// a room is a corner of it.
    /// <para>
    /// The library entry's region is copied from this scene: a prefab is edited in a
    /// room of its own, and the room it was made in is the only one anybody has
    /// expressed an opinion about.
    /// </para>
    /// </remarks>
    public Guid CreatePrefab(Guid elementId, string? name = null)
    {
        HierarchyElement source = GetWritableElement(elementId);
        Prefab prefab = _project.CreateDetachedPrefab(
            name ?? source.Name, Size, ElementClipFactory.Capture(source));

        var instance = new HierarchyElement(Guid.NewGuid(), source.Name);

        instance.SetComponent(new PrefabInstance(instance, prefab.Id));
        instance.ElementTransform.Restore(source.ElementTransform.Values, source.TransformModes);
        instance.ApplyPivot(source.PivotDoubled ?? CalculateContentPivot(source));
        instance.SetVisible(source.Visible);

        Execute(new CreatePrefabCommand(
            this, _project, prefab, source, instance, source.Parent.Id, IndexOf(source)));

        return prefab.Id;
    }

    /// <summary>
    /// Adds an instance of a prefab as the last child of <paramref name="parentId"/>.
    /// Its contents are derived from the source and appear at once.
    /// </summary>
    public Guid CreateInstance(Guid parentId, Guid prefabId, string? name = null)
    {
        HierarchyContainer parent = GetWritableContainer(parentId);

        if (!_project.TryGetPrefabContent(prefabId, out Scene? content))
            throw new ArgumentException($"No prefab with id {prefabId}.", nameof(prefabId));

        _project.EnsureInstanceAllowed(Id, prefabId);

        var element = new HierarchyElement(
            Guid.NewGuid(),
            name ?? CopyNameHelper.Compose(content.Name, CollectElementNames()));

        element.SetComponent(new PrefabInstance(element, prefabId));

        // The middle of what it will show, for the reason a fresh prefab's instance
        // gets one: an instance holds no cells of its own, so handles at its local
        // origin would sit in a corner of the room rather than on the thing.
        element.ApplyPivot(CalculateContentPivot(content));

        Execute(new CreateElementCommand(
            this, element, parentId, parent.Children.Count, ActionKind.CreateInstance));

        return element.Id;
    }

    /// <summary>
    /// Turns an instance into ordinary elements: the element stays where it is as a
    /// grouping element, and its contents become a plain copy with ids of their own.
    /// </summary>
    /// <remarks>
    /// One level. A nested instance inside the contents stays an instance, still
    /// following its own source — the same thing Unity does, and the reason is the
    /// same: unpacking is about this instance's relationship to its source, and
    /// dissolving everything underneath it would be a different, much larger
    /// request.
    /// </remarks>
    public void UnpackInstance(Guid elementId)
    {
        HierarchyElement instance = GetWritableElement(elementId);

        if (instance.Component is not PrefabInstance component)
        {
            throw new InvalidOperationException(
                $"Element {elementId} is not a prefab instance ({instance.ComponentKind}).");
        }

        Execute(new UnpackInstanceCommand(this, instance, component, PrepareUnpacked(instance)));
    }

    public void ReparentElement(Guid id, Guid newParentId, int index)
    {
        HierarchyElement element = GetWritableElement(id);
        GetWritableContainer(newParentId);

        if (newParentId == id || IsDescendantOfElement(newParentId, element))
        {
            throw new InvalidOperationException(
                $"Cannot reparent element {id} under {newParentId}: that would create a cycle.");
        }

        // A move that lands exactly where the element already stands records
        // nothing — the same bargain RenameElement makes with an unchanged name,
        // and the case a drag that wanders and comes home produces. The index is
        // counted after removal, so the clamp measures against the siblings the
        // element would rejoin, not the list it is still in.
        if (element.Parent.Id == newParentId
            && Math.Clamp(index, 0, element.Parent.Children.Count - 1) == IndexOf(element))
        {
            return;
        }

        Execute(new ReparentElementCommand(this, element, newParentId, index));
    }

    public void RenameElement(Guid id, string name)
    {
        HierarchyElement element = GetWritableElement(id);

        if (element.Name == name)
            return;

        Execute(new RenameElementCommand(this, element, name));
    }

    public void SetVisible(Guid id, bool visible)
    {
        HierarchyElement element = GetWritableElement(id);

        if (element.Visible == visible)
            return;

        Execute(new SetVisibleCommand(this, element, visible));
    }

    internal void ApplyVisible(HierarchyElement element, bool visible)
    {
        element.SetVisible(visible);

        Raise(SceneChangeKind.VisibilityChanged, element.Id);
    }

    /// <summary>
    /// Position is the <b>pivot's</b> location in the parent frame; rotation and
    /// scale hold that location still. So a position write moves the element, a
    /// rotation turns it about the pivot, and a scale grows it about the pivot —
    /// each as one record, because the snapshot pair carries the whole transform.
    /// </summary>
    /// <remarks>
    /// The snapshots hold the stored form — the local origin — and the pivot maths
    /// happens here, once, on the way in. Anchoring reads the <em>quantised</em>
    /// rotation or scale, not the requested one: solving the origin from the raw
    /// value would slide the element continuously while the angle snapped in
    /// steps, which is the opposite of what a quantised channel is for.
    /// <para>
    /// Nothing bounds where a position may put the element. The region is the room
    /// and the ruler, not a fence — the same reason nothing bounds the cells.
    /// </para>
    /// </remarks>
    public void SetTransform(Guid id, TransformChannel channel, Vector3 value)
    {
        HierarchyElement element = GetWritableElement(id);
        TransformSnapshot before = CaptureTransform(element);
        Vector3 pivot = element.PivotPoint;
        Vector3 quantised = Quantise(before.ModeOf(channel), value);

        TransformSnapshot after = channel == TransformChannel.Position
            ? before.WithValue(
                TransformChannel.Position,
                Quantise(
                    before.Modes.Position,
                    value - TransformHelper.ToTransform(before.Values).Basis * pivot))
            : AnchorPivot(before.WithValue(channel, quantised), before, pivot);

        if (after == before)
            return;

        Execute(new TransformCommand(this, element, ToActionKind(channel), before, after));
    }

    /// <summary>
    /// Rewrites the snapshot's origin so its pivot stands where it stood before
    /// the rotation or scale changed: <c>origin = anchor − basis · pivot</c>,
    /// where the anchor is read off <paramref name="before"/>.
    /// </summary>
    /// <remarks>
    /// The origin is <b>never rounded here</b>, and that is the whole invariant:
    /// this method's one promise is that the pivot does not move, and a rounded
    /// origin breaks it — worse, the next call derives its anchor from the rounded
    /// origin, so the error feeds back and the element walks with every turn.
    /// Measured before the rule below existed: three quarter-turns about the
    /// middle of a 3×4×3 model left it a full cell from where it started, at the
    /// same rotation. Where the exact origin does not land on the position mode's
    /// lattice — a quarter-turn about a pivot whose axes mix whole and half cells
    /// genuinely puts the cells on half positions — <b>the mode degrades to Float
    /// and the geometry stays</b>: the reparent rule, applied one more time.
    /// Quantising is kept only where it changes nothing but float noise, which is
    /// what keeps a parity-clean turn writing exact integers.
    /// </remarks>
    private static TransformSnapshot AnchorPivot(
        TransformSnapshot after,
        TransformSnapshot before,
        Vector3 pivot)
    {
        if (pivot == Vector3.Zero)
            return after;

        Vector3 anchor = before.Values.Position
            + TransformHelper.ToTransform(before.Values).Basis * pivot;

        Vector3 origin = anchor - TransformHelper.ToTransform(after.Values).Basis * pivot;
        Vector3 quantised = Quantise(after.Modes.Position, origin);

        if (quantised.IsEqualApprox(origin))
            return after.WithValue(TransformChannel.Position, quantised);

        return after
            .WithMode(TransformChannel.Position, FieldMode.Float)
            .WithValue(TransformChannel.Position, origin);
    }

    public void SetFieldMode(Guid id, TransformChannel channel, FieldMode mode)
    {
        if (!FieldModeHelper.IsAllowed(channel, mode))
            throw new ArgumentException($"{mode} is not a valid mode for {channel}.", nameof(mode));

        HierarchyElement element = GetWritableElement(id);
        TransformSnapshot before = CaptureTransform(element);

        if (before.ModeOf(channel) == mode)
            return;

        TransformSnapshot after = before
            .WithMode(channel, mode)
            .WithValue(channel, Quantise(mode, before.ValueOf(channel)));

        Execute(new TransformCommand(this, element, ActionKind.ChangeFieldMode, before, after));
    }

    public void SetSymmetry(Guid elementId, SymmetryValues values)
    {
        HierarchyElement element = GetWritableElement(elementId);
        VoxelModel model = GetVoxelModel(element);

        if (model.Symmetry == values)
            return;

        Execute(new SetSymmetryCommand(this, element, model, model.Symmetry, values));
    }

    internal void ApplySymmetry(HierarchyElement element, VoxelModel model, SymmetryValues values)
    {
        model.ApplySymmetry(values);

        Raise(SceneChangeKind.SymmetryChanged, element.Id);
    }

    /// <summary>
    /// Moves the pivot and nothing else: the stored origin stays put, so the
    /// geometry does not move — what moves is the point the position channel
    /// names, and the reported position follows it. Any element, not only one
    /// holding voxels: a group's handles need somewhere to sit as much as a
    /// model's. The point is rounded to the half-cell lattice on the way in.
    /// </summary>
    public void SetPivot(Guid elementId, Vector3 point)
    {
        HierarchyElement element = GetWritableElement(elementId);
        Vector3I doubled = RoundToHalfCells(point);

        if (element.PivotDoubled == doubled)
            return;

        Execute(new SetPivotCommand(this, element, element.PivotDoubled, doubled));
    }

    internal void ApplyPivot(HierarchyElement element, Vector3I? pivotDoubled)
    {
        element.ApplyPivot(pivotDoubled);

        Raise(SceneChangeKind.PivotChanged, element.Id);
    }

    public void SetCameraLens(Guid elementId, CameraLens lens)
    {
        HierarchyElement element = GetWritableElement(elementId);
        Camera camera = GetCamera(element);
        CameraLens wanted = lens.Clamped();

        if (camera.Lens == wanted)
            return;

        Execute(new SetCameraLensCommand(this, element, camera, camera.Lens, wanted));
    }

    internal void ApplyCameraLens(HierarchyElement element, Camera camera, CameraLens lens)
    {
        camera.ApplyLens(lens);

        Raise(SceneChangeKind.CameraChanged, element.Id);
    }

    public void SetReferenceImage(Guid elementId, ReferenceImageSettings settings)
    {
        HierarchyElement element = GetWritableElement(elementId);
        ReferenceImage reference = GetReferenceImage(element);
        ReferenceImageSettings wanted = settings.Clamped();

        EnsureAbsolute(wanted.Path);

        // The document may not carry a resolution that contradicts the file it
        // names, so a new path is measured here rather than trusted from a caller
        // — which is also what makes re-pointing a plane at another picture arrive
        // knowing how big that picture is.
        if (!string.Equals(wanted.Path, reference.Settings.Path, StringComparison.Ordinal))
            wanted = Measure(wanted with { PixelSize = reference.Settings.PixelSize });

        if (reference.Settings == wanted)
            return;

        Execute(new SetReferenceImageCommand(
            this, element, reference, reference.Settings, wanted));
    }

    internal void ApplyReferenceImage(
        HierarchyElement element,
        ReferenceImage reference,
        ReferenceImageSettings settings)
    {
        reference.ApplySettings(settings);

        Raise(SceneChangeKind.ReferenceChanged, element.Id);
    }

    public void SetReferenceModel(Guid elementId, ReferenceModelSettings settings)
    {
        HierarchyElement element = GetWritableElement(elementId);
        ReferenceModel reference = GetReferenceModel(element);
        ReferenceModelSettings wanted = settings.Clamped();

        EnsureAbsolute(wanted.Path);

        if (reference.Settings == wanted)
            return;

        Execute(new SetReferenceModelCommand(
            this, element, reference, reference.Settings, wanted));
    }

    /// <summary>
    /// A reference's path is absolute in memory or it is nothing. The storage layer
    /// stores a non-rooted path unchanged and resolves it against the directory of
    /// whatever file it reads it back from — so a relative path saved into a
    /// recovery snapshot would be re-based on the snapshot's directory and then
    /// written, absolute and wrong, into the user's own file on the next save.
    /// Every caller already passes an absolute path; this makes the model hold the
    /// invariant rather than trust them. Empty is the placeholder and stays legal.
    /// </summary>
    private static void EnsureAbsolute(string path)
    {
        if (path.Length > 0 && !System.IO.Path.IsPathRooted(path))
            throw new ArgumentException($"A reference path must be absolute: '{path}'.", nameof(path));
    }

    internal void ApplyReferenceModel(
        HierarchyElement element,
        ReferenceModel reference,
        ReferenceModelSettings settings)
    {
        reference.ApplySettings(settings);

        Raise(SceneChangeKind.ReferenceChanged, element.Id);
    }

    public void SetActiveCamera(Guid elementId)
    {
        // Not GetWritableElement: choosing which camera renders is a fact about
        // the scene rather than an edit to the element, so a camera inside a
        // prefab instance may be chosen even though nothing about it may be
        // changed. Refusing would leave a scene whose only camera is unusable.
        HierarchyElement element = GetElement(elementId);

        GetCamera(element);

        if (_chosenCameraId == elementId)
            return;

        Execute(new SetActiveCameraCommand(this, element, _chosenCameraId, elementId));
    }

    internal void ApplyActiveCamera(Guid elementId)
    {
        _chosenCameraId = elementId;

        Raise(SceneChangeKind.ActiveCameraChanged, ActiveCameraId);
    }

    /// <summary>
    /// Restores the choice as a file left it, without a record: loading is not an
    /// edit. An id naming nothing is kept rather than dropped — the fallback
    /// answers over it — because a file whose camera arrives later in the read
    /// would otherwise lose the choice it recorded.
    /// </summary>
    internal void RestoreActiveCamera(Guid elementId) => _chosenCameraId = elementId;

    /// <summary>
    /// The choice as it was made, which is what a file has to carry: writing the
    /// resolved answer instead would turn a fallback into a decision the moment the
    /// project was saved.
    /// </summary>
    internal Guid ChosenCameraId => _chosenCameraId;

    /// <summary>
    /// Depth-first in tree order, so which camera stands in for a choice nobody
    /// made is the one at the top of the panel rather than whichever the
    /// dictionary happened to hand back.
    /// </summary>
    private static Guid? FindFirstCamera(HierarchyContainer container)
    {
        foreach (IHierarchyElement child in container.Children)
        {
            if (child.Component is ICamera)
                return child.Id;

            if (FindFirstCamera(((HierarchyElement)child).ChildContainer) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Away from zero, not banker's: Godot's own Round taught this codebase what
    /// a midpoint quietly snapping to even looks like on a grid.
    /// </summary>
    private static Vector3I RoundToHalfCells(Vector3 point) => new(
        (int)Math.Round(point.X * 2f, MidpointRounding.AwayFromZero),
        (int)Math.Round(point.Y * 2f, MidpointRounding.AwayFromZero),
        (int)Math.Round(point.Z * 2f, MidpointRounding.AwayFromZero));

    public void SetVoxels(Guid elementId, IReadOnlyList<VoxelEdit> edits)
    {
        HierarchyElement element = GetWritableElement(elementId);
        VoxelModel model = GetVoxelModel(element);

        foreach (VoxelEdit edit in edits)
        {
            if (edit.MaterialId != null && !_project.ContainsMaterial(edit.MaterialId.Value))
            {
                throw new ArgumentException(
                    $"No material with id {edit.MaterialId} in this project.", nameof(edits));
            }
        }

        IReadOnlyList<VoxelChange> changesList = CollectVoxelChanges(model, edits);

        if (changesList.Count == 0)
            return;

        Execute(new SetVoxelsCommand(
            this, element, model, changesList, CalculateDefaultPivot(element, model, changesList)));
    }

    public void ReplaceMaterial(Guid fromId, Guid toId, Guid? elementId)
    {
        if (!_project.ContainsMaterial(toId))
            throw new ArgumentException($"No material with id {toId} in this project.", nameof(toId));

        if (fromId == toId)
            return;

        // One record for the whole scene: a batch, opened here rather than by a
        // caller, because the scene is the one thing that knows every element it
        // reaches. An element named outright needs no batch and gets one anyway,
        // so the record reads the same either way and a batch that collected
        // nothing leaves nothing.
        using (_history.CreateBatch(
                   ActionKind.ReplaceMaterial, Id, fromId, _project.DescribeMaterial(fromId)))
        {
            ReplaceMaterialInBatch(fromId, toId, elementId);
        }
    }

    /// <summary>
    /// The replacement itself, inside whatever batch the caller has open — the
    /// scene's own, or the project's spanning every scene. An element named
    /// outright that is prefab content is refused as every other edit refuses it;
    /// the scene-wide walk passes such elements over, because their cells are
    /// derived from a source and writing them would be writing a copy.
    /// </summary>
    /// <remarks>
    /// So a scene-scope replace does <b>not</b> reach the cells an instance shows:
    /// they belong to a prefab, which is not this scene. Only the project scope
    /// walks the prefabs' own scenes, which is why it is the one after which the
    /// old material is deletable — and why the answer says whether it still is.
    /// </remarks>
    internal void ReplaceMaterialInBatch(Guid fromId, Guid toId, Guid? elementId)
    {
        if (elementId is { } id)
        {
            HierarchyElement element = GetWritableElement(id);
            ReplaceMaterialIn(element, GetVoxelModel(element), fromId, toId);

            return;
        }

        // Snapshotted first: writing raises change events, and a prefab source
        // being rewritten rebuilds instances in this very dictionary.
        var elementsList = new List<HierarchyElement>(_elementsByIdDictionary.Values);

        foreach (HierarchyElement element in elementsList)
        {
            if (element.PrefabInstanceId != null || element.Component is not VoxelModel model)
                continue;

            ReplaceMaterialIn(element, model, fromId, toId);
        }
    }

    private void ReplaceMaterialIn(HierarchyElement element, VoxelModel model, Guid fromId, Guid toId)
    {
        var editsList = new List<VoxelEdit>();

        foreach ((Vector3I cell, Guid worn) in model.Voxels)
        {
            if (worn == fromId)
                editsList.Add(new VoxelEdit(cell, toId));
        }

        if (editsList.Count == 0)
            return;

        IReadOnlyList<VoxelChange> changesList = CollectVoxelChanges(model, editsList);

        Execute(new SetVoxelsCommand(
            this, element, model, changesList, CalculateDefaultPivot(element, model, changesList)));
    }

    /// <summary>
    /// The pivot's default: the middle of what the element's <b>first</b> voxel
    /// edit filled, on the half-cell lattice by construction. Set here, inside the
    /// voxel record, because "first" is a fact about this moment that nothing could
    /// reconstruct later — and one record, because the edit and its default are one
    /// thing the user did. Null everywhere else: for a model that already holds
    /// cells, and for a pivot that was set explicitly — even to zero — which the
    /// default must not overwrite.
    /// </summary>
    private static Vector3I? CalculateDefaultPivot(
        HierarchyElement element,
        VoxelModel model,
        IReadOnlyList<VoxelChange> changesList)
    {
        if (element.PivotDoubled != null || model.Voxels.Count > 0)
            return null;

        var filledList = new List<Vector3I>(changesList.Count);

        foreach (VoxelChange change in changesList)
        {
            if (change.After != null)
                filledList.Add(change.Cell);
        }

        VoxelExtent extent = VoxelExtent.Measure(filledList);

        return extent.IsEmpty ? null : extent.CenterDoubled;
    }

    internal void ApplyVoxels(
        HierarchyElement element,
        VoxelModel model,
        IReadOnlyList<VoxelChange> changesList,
        bool forward)
    {
        foreach (VoxelChange change in changesList)
            model.Apply(change.Cell, forward ? change.After : change.Before);

        Raise(SceneChangeKind.VoxelsChanged, element.Id, MeasureRegion(changesList));
    }

    /// <summary>
    /// Builds an element straight from a file: no history, no change events, and
    /// no placement rules. Cells on disk were legal when they were written, and
    /// the region rule governs placement rather than storage.
    /// </summary>
    internal HierarchyElement AddLoadedElement(
        Guid id,
        Guid parentId,
        string name,
        ComponentKind kind,
        Guid? sourceId)
    {
        var element = new HierarchyElement(id, name);
        IElementComponent? component = sourceId is { } source
            ? new PrefabInstance(element, source)
            : ElementComponentFactory.Create(kind, element);

        if (component != null)
            element.SetComponent(component);

        _elementsByIdDictionary.Add(id, element);
        ContainerOfId(parentId).Add(element);

        return element;
    }

    /// <summary>Every top-level element of this scene as clip nodes — the scene half of a copy.</summary>
    internal IReadOnlyList<ElementNode> CaptureNodes()
    {
        var nodesList = new List<ElementNode>(_children.Children.Count);

        foreach (HierarchyElement child in _children.Children)
            nodesList.Add(ElementClipFactory.Capture(child));

        return nodesList;
    }

    /// <summary>
    /// Puts a prepared element into a scene that has not been attached yet — no
    /// history and no change events, on the same terms as
    /// <see cref="AddLoadedElement"/>: a pasted scene arrives whole, as one
    /// <see cref="ProjectChangeKind.SceneAdded"/>, not as a stream of its parts.
    /// </summary>
    internal void AddClipElement(HierarchyElement element)
    {
        PrefabExpander.FillSubtree(_project, element);
        Register(element);
        _children.Add(element);
    }

    /// <summary>What an instance of a prefab expands into: this scene's top level.</summary>
    internal IReadOnlyList<HierarchyElement> TopLevelElements => _children.Children;

    /// <summary>
    /// Throws away an instance's contents and derives them again. What the source
    /// says now is the whole answer, so nothing is diffed: the ids are derived from
    /// the instance and the source element, which is what keeps a selection, a row's
    /// expansion state and an id an agent just read from being lost on every
    /// keystroke inside the prefab.
    /// </summary>
    internal void RebuildInstanceContent(HierarchyElement instance)
    {
        DetachChildren(instance);
        PrefabExpander.Fill(_project, instance);

        var childrenList = new List<HierarchyElement>(instance.ChildContainer.Children);

        for (var index = 0; index < childrenList.Count; index++)
        {
            Register(childrenList[index]);
            Raise(SceneChangeKind.ElementAdded, childrenList[index].Id, instance.Id, index);
        }
    }

    internal void RebuildInstancesOf(Guid prefabId)
    {
        foreach (HierarchyElement instance in CollectInstances(prefabId, writableOnly: false))
            RebuildInstanceContent(instance);
    }

    /// <summary>Every instance in this scene, whatever it points at — used after a load.</summary>
    internal void RebuildEveryInstance()
    {
        foreach (HierarchyElement instance in CollectInstances(null, writableOnly: false))
            RebuildInstanceContent(instance);
    }

    /// <summary>
    /// The instances of a prefab that a user could act on. Derived ones are left
    /// out deliberately: an instance inside another instance's contents is not a
    /// thing to unpack on its own — it goes when the source it belongs to is
    /// rebuilt.
    /// </summary>
    internal IReadOnlyList<HierarchyElement> CollectWritableInstancesOf(Guid prefabId) =>
        CollectInstances(prefabId, writableOnly: true);

    /// <summary>
    /// The ordinary elements an instance's contents become when it is unpacked:
    /// a plain copy with fresh ids, built before anything is changed so that the
    /// command can replay it.
    /// </summary>
    internal IReadOnlyList<HierarchyElement> PrepareUnpacked(HierarchyElement instance)
    {
        var childrenList = new List<HierarchyElement>(instance.ChildContainer.Children.Count);

        foreach (HierarchyElement child in instance.ChildContainer.Children)
        {
            childrenList.Add(ElementClipFactory.Create(
                ElementClipFactory.Capture(child), ClipResolution.Identity, child.Name));
        }

        return childrenList;
    }

    internal void ApplyUnpack(
        HierarchyElement instance,
        IReadOnlyList<HierarchyElement> childrenList)
    {
        DetachChildren(instance);
        instance.ApplyComponent(null);

        Raise(SceneChangeKind.ComponentChanged, instance.Id);

        for (var index = 0; index < childrenList.Count; index++)
        {
            HierarchyElement child = childrenList[index];

            instance.ChildContainer.Insert(child, index);
            PrefabExpander.FillSubtree(_project, child);
            Register(child);

            Raise(SceneChangeKind.ElementAdded, child.Id, instance.Id, index);
        }
    }

    internal void ApplyRepack(HierarchyElement instance, PrefabInstance component)
    {
        DetachChildren(instance);
        instance.ApplyComponent(component);

        Raise(SceneChangeKind.ComponentChanged, instance.Id);

        RebuildInstanceContent(instance);
    }

    /// <summary>
    /// The instances in this scene. A null <paramref name="prefabId"/> takes them
    /// all, whatever they point at.
    /// </summary>
    internal IReadOnlyList<HierarchyElement> CollectInstances(Guid? prefabId, bool writableOnly)
    {
        var instancesList = new List<HierarchyElement>();

        foreach (HierarchyElement element in _elementsByIdDictionary.Values)
        {
            if (element.Component is not PrefabInstance instance)
                continue;

            if (prefabId != null && instance.SourceId != prefabId.Value)
                continue;

            if (writableOnly && element.PrefabInstanceId != null)
                continue;

            instancesList.Add(element);
        }

        return instancesList;
    }

    private void DetachChildren(HierarchyElement parent)
    {
        var childrenList = new List<HierarchyElement>(parent.ChildContainer.Children);

        parent.ChildContainer.Clear();

        foreach (HierarchyElement child in childrenList)
        {
            Unregister(child);

            Raise(SceneChangeKind.ElementRemoved, child.Id);
        }
    }

    /// <summary>
    /// Where the handles of a fresh instance should sit: the middle of what the
    /// element holds, on the half-cell lattice. Null when there is nothing to
    /// measure, which leaves the pivot where it was — at the local origin.
    /// </summary>
    private static Vector3I? CalculateContentPivot(HierarchyElement element) =>
        PivotCenter.TryCalculate(element, out Vector3 center) ? RoundToHalfCells(center) : null;

    private static Vector3I? CalculateContentPivot(IHierarchyContainer container) =>
        PivotCenter.TryCalculate(container, out Vector3 center) ? RoundToHalfCells(center) : null;

    /// <summary>
    /// Every name in the scene, at any depth — what a pasted root's name is composed
    /// against. Scene-wide rather than among the new siblings, because that is the
    /// scope a name has to be unambiguous in for anyone addressing an element by it.
    /// </summary>
    internal HashSet<string> CollectElementNames()
    {
        var namesHashSet = new HashSet<string>();

        foreach (HierarchyElement element in _elementsByIdDictionary.Values)
            namesHashSet.Add(element.Name);

        return namesHashSet;
    }

    /// <summary>
    /// Any instance in the arriving subtree is expanded <b>before</b> the subtree is
    /// registered or announced, so what a view is told about is already complete.
    /// It happens on every attach, undo included: a source may have changed while
    /// the subtree sat in a delete command, and stale contents are the one thing
    /// derived data must never be.
    /// </summary>
    internal void AttachElement(HierarchyElement element, Guid parentId, int index)
    {
        ContainerOfId(parentId).Insert(element, index);

        PrefabExpander.FillSubtree(_project, element);
        Register(element);

        Raise(SceneChangeKind.ElementAdded, element.Id, parentId, index);
    }

    internal void DetachElement(HierarchyElement element)
    {
        ContainerOf(element.Parent).Remove(element);
        Unregister(element);

        Raise(SceneChangeKind.ElementRemoved, element.Id);
    }

    /// <summary>
    /// Puts the element under another parent at a given position among its
    /// siblings — the position it ends up in, counted after it has left wherever it
    /// was.
    /// </summary>
    /// <remarks>
    /// The index is clamped rather than refused, on the terms the rest of the model
    /// quantises a transform: a caller asking for a place past the end means the
    /// end, and an out-of-range index used to leave an
    /// <see cref="ArgumentOutOfRangeException"/> escaping through
    /// <c>reparent-element</c> to whoever typed it.
    /// </remarks>
    internal void MoveElement(HierarchyElement element, Guid parentId, int index)
    {
        ContainerOf(element.Parent).Remove(element);

        HierarchyContainer parent = ContainerOfId(parentId);
        int placed = Math.Clamp(index, 0, parent.Children.Count);

        parent.Insert(element, placed);

        Raise(SceneChangeKind.ElementMoved, element.Id, parentId, placed);
    }

    internal void ApplyName(HierarchyElement element, string name)
    {
        element.SetName(name);

        Raise(SceneChangeKind.ElementRenamed, element.Id);
    }

    /// <summary>
    /// The scene's own name. It is the project that owns the list of scenes, so
    /// the change event for this one comes from there rather than from here.
    /// </summary>
    internal void SetName(string name) => Name = name;

    /// <summary>
    /// The stored form — the local origin — not the public pivot form: snapshots
    /// are what undo replays, and replaying must be exact rather than a round trip
    /// through the pivot arithmetic.
    /// </summary>
    internal TransformSnapshot CaptureTransform(HierarchyElement element) =>
        new(element.ElementTransform.Values, element.TransformModes);

    internal void RestoreTransform(HierarchyElement element, TransformSnapshot snapshot)
    {
        element.ElementTransform.Restore(snapshot.Values, snapshot.Modes);

        Raise(SceneChangeKind.TransformChanged, element.Id);
    }

    internal int IndexOf(HierarchyElement element) => ContainerOf(element.Parent).IndexOf(element);

    /// <summary>Walked on demand — deleting a material is rare, and a stale usage index would not be.</summary>
    internal bool UsesMaterial(Guid materialId)
    {
        foreach (HierarchyElement element in _elementsByIdDictionary.Values)
        {
            if (element.Component is VoxelModel model && model.UsesMaterial(materialId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Writes the local transform that keeps <paramref name="world"/> where it was.
    /// Where a field mode cannot represent the result, the mode degrades instead of
    /// the geometry shifting — a child of a rotated group simply is not grid-aligned
    /// any more.
    /// </summary>
    internal void FitLocalToWorld(HierarchyElement element, Transform3D world)
    {
        Transform3D parentWorld = CalculateWorldTransform(element.Parent);
        TransformValues values = TransformHelper.ToValues(parentWorld.AffineInverse() * world);
        ElementTransform transform = element.ElementTransform;

        ApplyChannel(transform, TransformChannel.Position, values.Position);
        ApplyChannel(transform, TransformChannel.Rotation, values.RotationDegrees);
        ApplyChannel(transform, TransformChannel.Scale, values.Scale);

        Raise(SceneChangeKind.TransformChanged, element.Id);
    }

    internal static Transform3D CalculateWorldTransform(IHierarchyContainer container)
    {
        var result = Transform3D.Identity;

        // The stored form composes directly: the pivot only re-labels which point
        // the position channel names, it never changes the frame itself.
        for (IHierarchyContainer current = container;
            current is HierarchyElement element;
            current = element.Parent)
        {
            result = TransformHelper.ToTransform(element.ElementTransform.Values) * result;
        }

        return result;
    }

    private static void ApplyChannel(
        ElementTransform transform,
        TransformChannel channel,
        Vector3 desired)
    {
        transform.Set(channel, desired);

        if (transform.Get(channel).IsEqualApprox(desired))
            return;

        transform.SetMode(channel, FieldMode.Float);
        transform.Set(channel, desired);
    }

    /// <summary>
    /// Folds the requested edits into one change per cell — a later edit to the
    /// same cell wins, but "before" stays the value the model actually started
    /// with, so undo is exact even when a stroke crosses itself. Cells that end
    /// up where they began drop out, and an edit list of only those records
    /// nothing at all.
    /// </summary>
    /// <remarks>
    /// Where a cell lands is not asked. The buildable region is the room and the
    /// ruler; it bounds neither the cells of a model — which may legitimately hang
    /// out of the box, and had to be extendable and erasable where they hang — nor
    /// where the element holding them stands.
    /// </remarks>
    private static IReadOnlyList<VoxelChange> CollectVoxelChanges(
        VoxelModel model,
        IReadOnlyList<VoxelEdit> edits)
    {
        var changesByCellDictionary = new Dictionary<Vector3I, VoxelChange>();

        foreach (VoxelEdit edit in edits)
        {
            Guid? before = changesByCellDictionary.TryGetValue(edit.Cell, out VoxelChange existing)
                ? existing.Before
                : model.TryGetVoxel(edit.Cell, out Guid current) ? current : null;

            changesByCellDictionary[edit.Cell] = new VoxelChange(edit.Cell, before, edit.MaterialId);
        }

        var changesList = new List<VoxelChange>(changesByCellDictionary.Count);

        foreach (VoxelChange change in changesByCellDictionary.Values)
        {
            if (change.Before != change.After)
                changesList.Add(change);
        }

        return changesList;
    }

    private static VoxelModel GetVoxelModel(HierarchyElement element)
    {
        if (element.Component is not VoxelModel model)
        {
            throw new InvalidOperationException(
                $"Element {element.Id} has no voxel model ({element.ComponentKind}).");
        }

        return model;
    }

    private static Camera GetCamera(HierarchyElement element)
    {
        if (element.Component is not Camera camera)
        {
            throw new InvalidOperationException(
                $"Element {element.Id} is not a camera ({element.ComponentKind}).");
        }

        return camera;
    }

    private static ReferenceImage GetReferenceImage(HierarchyElement element)
    {
        if (element.Component is not ReferenceImage reference)
        {
            throw new InvalidOperationException(
                $"Element {element.Id} is not a reference picture ({element.ComponentKind}).");
        }

        return reference;
    }

    private static ReferenceModel GetReferenceModel(HierarchyElement element)
    {
        if (element.Component is not ReferenceModel reference)
        {
            throw new InvalidOperationException(
                $"Element {element.Id} is not a reference model ({element.ComponentKind}).");
        }

        return reference;
    }

    private static Vector3 Quantise(FieldMode mode, Vector3 value) =>
        FieldVector3Factory.Create(mode, value).AsFloat;

    private static ActionKind ToActionKind(TransformChannel channel) => channel switch
    {
        TransformChannel.Position => ActionKind.MoveElement,
        TransformChannel.Rotation => ActionKind.RotateElement,
        TransformChannel.Scale => ActionKind.ScaleElement,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
    };

    private static HierarchyContainer ContainerOf(IHierarchyContainer container) => container switch
    {
        HierarchyElement element => element.ChildContainer,
        Scene scene => scene._children,
        _ => throw new ArgumentOutOfRangeException(nameof(container))
    };

    private static void EnsureWritable(HierarchyElement element)
    {
        if (element.PrefabInstanceId != null)
        {
            throw new InvalidOperationException(
                $"'{element.Name}' is prefab instance content and cannot be edited.");
        }
    }

    private HierarchyContainer ContainerOfId(Guid id) =>
        id == Id ? _children : GetElement(id).ChildContainer;

    private bool IsDescendantOfElement(Guid candidateId, HierarchyElement ancestor)
    {
        if (!_elementsByIdDictionary.TryGetValue(candidateId, out HierarchyElement? candidate))
            return false;

        for (IHierarchyContainer current = candidate.Parent;
            current is HierarchyElement element;
            current = element.Parent)
        {
            if (ReferenceEquals(element, ancestor))
                return true;
        }

        return false;
    }

    private HierarchyElement GetElement(Guid id)
    {
        if (!_elementsByIdDictionary.TryGetValue(id, out HierarchyElement? element))
            throw new ArgumentException($"No element with id {id} in this scene.", nameof(id));

        return element;
    }

    private HierarchyElement GetWritableElement(Guid id)
    {
        HierarchyElement element = GetElement(id);
        EnsureWritable(element);

        return element;
    }

    private HierarchyContainer GetWritableContainer(Guid id)
    {
        if (id == Id)
            return _children;

        HierarchyElement element = GetWritableElement(id);

        if (element.ComponentKind == ComponentKind.PrefabInstance)
        {
            throw new InvalidOperationException(
                $"Element {id} is a prefab instance; its contents are derived from the source.");
        }

        return element.ChildContainer;
    }

    private void Register(HierarchyElement element)
    {
        _elementsByIdDictionary[element.Id] = element;

        foreach (HierarchyElement child in element.ChildContainer.Children)
            Register(child);
    }

    private void Unregister(HierarchyElement element)
    {
        _elementsByIdDictionary.Remove(element.Id);

        foreach (HierarchyElement child in element.ChildContainer.Children)
            Unregister(child);
    }

    /// <summary>
    /// Every edit this scene makes is stamped with its id, so that undoing one
    /// can bring the right scene up. Going through here rather than through the
    /// history directly is what keeps a new command from forgetting to say so.
    /// </summary>
    private void Execute(ICommand command) => _history.Execute(command, Id);

    private void Raise(SceneChangeKind kind, Guid elementId) =>
        Raise(kind, elementId, Guid.Empty, -1);

    private void Raise(SceneChangeKind kind, Guid elementId, CellRegion? region) =>
        Changed?.Invoke(new SceneChange(kind, elementId, Guid.Empty, -1, region));

    /// <summary>
    /// The box the edited cells occupy, or null when there were none. Measured
    /// from the edits rather than from the model, because what a consumer needs
    /// is what <em>changed</em> — a model that grew by one cell is otherwise
    /// indistinguishable from one rebuilt whole.
    /// </summary>
    private static CellRegion? MeasureRegion(IReadOnlyList<VoxelChange> changesList)
    {
        if (changesList.Count == 0)
            return null;

        Vector3I min = changesList[0].Cell;
        Vector3I max = min;

        for (var i = 1; i < changesList.Count; i++)
        {
            Vector3I cell = changesList[i].Cell;

            min = new Vector3I(
                Math.Min(min.X, cell.X), Math.Min(min.Y, cell.Y), Math.Min(min.Z, cell.Z));

            max = new Vector3I(
                Math.Max(max.X, cell.X), Math.Max(max.Y, cell.Y), Math.Max(max.Z, cell.Z));
        }

        return new CellRegion(min, max);
    }

    private void Raise(SceneChangeKind kind, Guid elementId, Guid parentId, int index) =>
        Changed?.Invoke(new SceneChange(kind, elementId, parentId, index));
}
