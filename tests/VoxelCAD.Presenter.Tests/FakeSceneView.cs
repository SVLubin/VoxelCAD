using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Godot;

namespace VoxelCAD.Presenter.Tests;

/// <summary>Remembers what the viewport was told, so a test can read it back.</summary>
internal sealed class FakeSceneView : ISceneView
{
    private readonly HashSet<Guid> _elementIdsHashSet = new();
    private readonly Dictionary<Guid, Color> _materialsByIdDictionary = new();
    private readonly Dictionary<Guid, MaterialAppearance> _appearancesByIdDictionary = new();

    /// <summary>Every preview asked for, in order.</summary>
    public List<(Color Color, MaterialAppearance Appearance, int Size)> PreviewRequests { get; } = new();

    /// <summary>Which way the block was turned for each preview asked for.</summary>
    public List<MaterialPreviewTurn> PreviewTurns { get; } = new();
    private readonly Dictionary<Guid, SymmetryValues> _symmetryByElementDictionary = new();
    private readonly Dictionary<Guid, bool> _visibilityByElementDictionary = new();

    public event Action<ScenePick>? Picked;

    public event Action? StrokeBegan;

    public event Action? StrokeEnded;

    public event Action<ElementPick>? ElementPicked;

    public event Action? KeyboardLost;

    public event Action? FocusRequested;

    public event Action<ToolKind>? ToolRequested;

    public event Action? GizmoDragBegan;

    public event Action<Vector3>? GizmoDragged;

    public event Action? GizmoDragEnded;

    public event Action? GizmoDragCancelled;

    public int ClearCount { get; private set; }

    /// <summary>Null until something has been framed.</summary>
    public Guid? FocusTarget { get; private set; }

    public Guid RootId { get; private set; }

    public Vector3I Size { get; private set; }

    public Guid? PickTarget { get; private set; }

    /// <summary>Which element wears the handles and the lasers; null for none.</summary>
    public Guid? HighlightTarget { get; private set; }

    /// <summary>Every element wearing a border, in the order they were pushed.</summary>
    public IReadOnlyList<Guid> HighlightedElements { get; private set; } = Array.Empty<Guid>();

    /// <summary>Where on that element the handles were last told to sit.</summary>
    public Vector3 HighlightPivot { get; private set; }

    /// <summary>
    /// How many times the highlight was pushed, so a test can tell a re-push —
    /// after the viewport rebuilt the element's node — from the same value simply
    /// still being there.
    /// </summary>
    public int HighlightCount { get; private set; }

    /// <summary>Which tool the viewport was last told it is in.</summary>
    public ToolKind Tool { get; private set; }

    /// <summary>
    /// How many times the viewport was asked for the keyboard. A count rather than
    /// a flag: the real surface answers a second request with a no-op, so what a
    /// test needs to see is that the asking happened.
    /// </summary>
    public int KeyboardCount { get; private set; }

    public Guid BrushMaterialId { get; private set; }

    /// <summary>The pose the camera holds — what an aim writes and a save reads.</summary>
    public CameraPose CameraPose { get; set; }

    public CameraView CameraView { get; set; }

    /// <summary>How many times a frame has been captured.</summary>
    public int CaptureCount { get; private set; }

    /// <summary>The options the last capture was asked for, so a test can read the dials back.</summary>
    public FrameCaptureOptions LastCaptureOptions { get; private set; }

    /// <summary>
    /// The window in input coordinates, which a capture reports back so an agent
    /// can map image pixels onto points to aim at.
    /// </summary>
    public Vector2I WindowSize { get; set; } = new(1200, 800);

    /// <summary>Whether an agent is currently marked as holding the editor.</summary>
    public bool AgentControlled { get; private set; }

    public IReadOnlyCollection<Guid> Elements => _elementIdsHashSet;

    public IReadOnlyDictionary<Guid, Color> Materials => _materialsByIdDictionary;

    public IReadOnlyDictionary<Guid, MaterialAppearance> Appearances => _appearancesByIdDictionary;

    /// <summary>The mirror-plane slots each element was last pushed.</summary>
    public IReadOnlyDictionary<Guid, SymmetryValues> Symmetry => _symmetryByElementDictionary;

    public void SetPickTarget(Guid? elementId) => PickTarget = elementId;

    public void SetBrushMaterial(Guid materialId) => BrushMaterialId = materialId;

    public void SetSelectionHighlight(
        IReadOnlyList<Guid> elementIds, Guid? activeId, Vector3 pivotPoint)
    {
        HighlightedElements = elementIds.ToList();
        HighlightTarget = activeId;
        HighlightPivot = pivotPoint;
        HighlightCount++;
    }

    public void SetTool(ToolKind tool) => Tool = tool;

    /// <summary>
    /// What the drag in flight last proposed, or null if it was cleared. The ghost
    /// and the readout are the same push, so this is both of them.
    /// </summary>
    public DragPreview? DragPreview { get; private set; }

    public void SetDragPreview(DragPreview? preview) => DragPreview = preview;

    public void TakeKeyboard() => KeyboardCount++;

    public void Clear()
    {
        ClearCount++;
        RootId = Guid.Empty;
        _elementIdsHashSet.Clear();
        _materialsByIdDictionary.Clear();
        _appearancesByIdDictionary.Clear();
        _symmetryByElementDictionary.Clear();
        _visibilityByElementDictionary.Clear();
        Cameras.Clear();
    }

    public void SetRoot(Guid sceneId, Vector3I size)
    {
        RootId = sceneId;
        Size = size;
        _elementIdsHashSet.Add(sceneId);
    }

    public void AddElement(Guid id, Guid parentId) => _elementIdsHashSet.Add(id);

    public void RemoveElement(Guid id) => _elementIdsHashSet.Remove(id);

    public void SetElementTransform(Guid id, TransformValues values, Vector3 pivotPoint)
    {
    }

    public void SetElementVisible(Guid id, bool visible) =>
        _visibilityByElementDictionary[id] = visible;

    /// <summary>What the viewport was last told about an element; shown until told otherwise.</summary>
    public bool IsVisible(Guid id) =>
        !_visibilityByElementDictionary.TryGetValue(id, out bool visible) || visible;

    /// <summary>
    /// The region of the last voxel push per element, and whether it was carried
    /// at all. Recorded because it is the whole of the chunked remesh at this
    /// seam: null means every chunk, and a presenter that quietly stopped
    /// carrying the box would be indistinguishable from one that never did —
    /// the picture would still be right, and every edit would cost the model.
    /// </summary>
    public IReadOnlyDictionary<Guid, CellRegion?> VoxelRegions => _regionsByElementDictionary;

    public void SetElementVoxels(
        Guid id,
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        CellRegion? region)
    {
        _voxelsByElementDictionary[id] = voxels;
        _regionsByElementDictionary[id] = region;
    }

    /// <summary>The cells the viewport was last told an element holds.</summary>
    public IReadOnlyDictionary<Vector3I, Guid> VoxelsOf(Guid id) =>
        _voxelsByElementDictionary.TryGetValue(id, out IReadOnlyDictionary<Vector3I, Guid>? cells)
            ? cells
            : new Dictionary<Vector3I, Guid>();

    public void SetElementSymmetry(Guid id, SymmetryValues values) =>
        _symmetryByElementDictionary[id] = values;

    /// <summary>
    /// Every camera the viewport has been told about, and whether each is the one
    /// the scene renders through. An entry whose lens is null is an element that
    /// is not a camera, which is what the real view is told to take a gizmo away.
    /// </summary>
    public Dictionary<Guid, (CameraLens? Lens, bool Active)> Cameras { get; } = new();

    /// <summary>The shape of the picture a frustum is drawn at.</summary>
    public float CameraAspect { get; private set; }

    /// <summary>
    /// What each element was last told it stands for. Null is a fact rather than an
    /// absence — "this element is not a picture" — so both tables carry an entry
    /// for every element that has been pushed at all.
    /// </summary>
    public Dictionary<Guid, ReferenceImageSettings?> ReferenceImages { get; } = new();

    public Dictionary<Guid, ReferenceModelSettings?> ReferenceModels { get; } = new();

    /// <summary>
    /// Whether references are drawn at all. Starts true, as the viewport's own does
    /// — the switch is for looking at the model clean for a moment.
    /// </summary>
    public bool ReferencesVisible { get; private set; } = true;

    /// <summary>
    /// What the viewport was last told to run. Recorded rather than counted: the
    /// push is idempotent, so what matters is the answer standing, not how often
    /// it was written.
    /// </summary>
    /// <remarks>
    /// It starts at <b>everything off</b>, which is deliberately not the default
    /// the presenter pushes. Born holding <c>ViewportEffects.Full</c>, the test
    /// asserting that a viewport nobody configured runs everything passed with
    /// the push deleted — the fake was answering with the expected value rather
    /// than with what it had been told. A fake that is quieter than the real
    /// thing hides its consumers; one that agrees with the answer in advance
    /// hides whether there was a question.
    /// </remarks>
    public ViewportEffects Effects { get; private set; } = new(false, false, false);

    /// <summary>Whether the viewport was ever told what to run at all.</summary>
    public bool EffectsPushed { get; private set; }

    /// <summary>
    /// Every effects push in order, so a test can say what a material was built
    /// against — the effects must arrive before the materials do.
    /// </summary>
    public List<string> EffectsAndMaterialsLog { get; } = new();

    private readonly Dictionary<Guid, CellRegion?> _regionsByElementDictionary = new();

    private readonly Dictionary<Guid, IReadOnlyDictionary<Vector3I, Guid>>
        _voxelsByElementDictionary = new();

    public void SetElementCamera(Guid id, CameraLens? lens, bool active) =>
        Cameras[id] = (lens, active);

    public void SetElementReference(
        Guid id,
        ReferenceImageSettings? picture,
        ReferenceModelSettings? model)
    {
        ReferenceImages[id] = picture;
        ReferenceModels[id] = model;
    }

    public void SetReferencesVisible(bool visible) => ReferencesVisible = visible;

    public void SetCameraAspect(float aspect) => CameraAspect = aspect;

    public void SetViewportEffects(ViewportEffects effects)
    {
        Effects = effects;
        EffectsPushed = true;
        EffectsAndMaterialsLog.Add($"effects {effects.Occlusion} {effects.Shadows} {effects.Factures}");
    }

    public void SetMaterial(Guid id, string name, Color color, MaterialAppearance appearance)
    {
        EffectsAndMaterialsLog.Add($"material {name}");

        _materialsByIdDictionary[id] = color;
        _appearancesByIdDictionary[id] = appearance;
    }

    /// <summary>
    /// Answers a square of nothing. The fake exists to record what a presenter
    /// asked for, and a preview is a picture — what it looks like is verified by
    /// looking, which is what the preview is for in the first place.
    /// </summary>
    public FrameCapture CapturePreview(
        string name,
        Color color,
        MaterialAppearance appearance,
        MaterialPreviewTurn turn,
        int size,
        FrameFormat format,
        float quality)
    {
        PreviewRequests.Add((color, appearance, size));
        PreviewTurns.Add(turn);

        return new FrameCapture(
            Array.Empty<byte>(),
            size,
            size,
            new Vector2I(size, size),
            Vector2.Zero,
            Vector2.One);
    }

    public void RemoveMaterial(Guid id)
    {
        _materialsByIdDictionary.Remove(id);
        _appearancesByIdDictionary.Remove(id);
    }

    public void FocusOn(Guid targetId) => FocusTarget = targetId;

    public CameraPose GetCameraPose() => CameraPose;

    public void SetCameraPose(CameraPose pose) => CameraPose = pose;

    public CameraView GetCameraView() => CameraView;

    /// <summary>
    /// Crops, scales and reports the mapping the same way the real viewport does,
    /// so a test can check that what a command echoes back lines up with what it
    /// asked for.
    /// </summary>
    public FrameCapture CaptureFrame(FrameCaptureOptions options)
    {
        CaptureCount++;
        LastCaptureOptions = options;

        Vector2I origin = options.Region?.Position ?? Vector2I.Zero;
        Vector2I size = options.Region?.Size ?? WindowSize;

        return new FrameCapture(
            new byte[] { 1, 2, 3 },
            (int)(size.X * options.Scale),
            (int)(size.Y * options.Scale),
            WindowSize,
            new Vector2(origin.X, origin.Y),
            new Vector2(options.Scale, options.Scale));
    }

    public void SetAgentControlled(bool controlled) => AgentControlled = controlled;

    /// <summary>The fake holds no gesture of its own, so there is never one to end.</summary>
    public bool TryAnswerEscape() => false;

    public void RaisePicked(ScenePick pick) => Picked?.Invoke(pick);

    public void RaiseStrokeBegan() => StrokeBegan?.Invoke();

    /// <summary>How many times a presenter closed the stroke from its side.</summary>
    public int EndStrokeCount { get; private set; }

    /// <summary>Closes like the real view: it says so, whether or not a stroke was open.</summary>
    public void EndStroke()
    {
        EndStrokeCount++;
        StrokeEnded?.Invoke();
    }

    public void RaiseStrokeEnded() => StrokeEnded?.Invoke();

    /// <summary>
    /// A whole brush stroke as the real viewport reports one: the press, then one
    /// pick per cell the hover proposed while the button stayed down (the first
    /// of them being the press's own), then the release.
    /// </summary>
    public void RaiseBrushStroke(params ScenePick[] picks)
    {
        RaiseStrokeBegan();

        foreach (ScenePick pick in picks)
            RaisePicked(pick);

        RaiseStrokeEnded();
    }

    /// <summary>
    /// A cursor click, as the real viewport reports one: the elements the ray
    /// crossed, nearest first, and where the click was — the same point twice
    /// meaning "again, in the same spot".
    /// </summary>
    public void RaiseElementPicked(Vector2 position, params Guid[] elements) =>
        RaiseElementPicked(SelectionIntent.Replace, position, elements);

    /// <summary>The same click carrying a modifier.</summary>
    public void RaiseElementPicked(
        SelectionIntent intent, Vector2 position, params Guid[] elements) =>
        ElementPicked?.Invoke(new ElementPick(elements, position, intent));

    public void RaiseFocusRequested() => FocusRequested?.Invoke();

    /// <summary>The keyboard went to a panel or to another application.</summary>
    public void RaiseKeyboardLost() => KeyboardLost?.Invoke();

    /// <summary>A tool key was pressed in the viewport.</summary>
    public void RaiseToolRequested(ToolKind tool) => ToolRequested?.Invoke(tool);

    public void RaiseGizmoDragBegan() => GizmoDragBegan?.Invoke();

    /// <summary>Cumulative from the press, the way the real handles report it.</summary>
    public void RaiseGizmoDragged(Vector3 delta) => GizmoDragged?.Invoke(delta);

    public void RaiseGizmoDragEnded() => GizmoDragEnded?.Invoke();

    public void RaiseGizmoDragCancelled() => GizmoDragCancelled?.Invoke();

    /// <summary>A whole drag: press, the steps it went through, release.</summary>
    public void RaiseGizmoDrag(params Vector3[] deltas)
    {
        RaiseGizmoDragBegan();

        foreach (Vector3 delta in deltas)
            RaiseGizmoDragged(delta);

        RaiseGizmoDragEnded();
    }
}
