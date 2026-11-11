using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Geometry;

namespace VoxelCAD.View;

/// <inheritdoc cref="ISceneView"/>
internal sealed partial class SceneView : Node3D, ISceneView
{
    /// <summary>
    /// The selection border's own colour, and deliberately not the accent. White
    /// belongs to no material and to no axis, so a border in it cannot be mistaken
    /// for either — which is what the accent had started to be, with the ghost
    /// wearing it too. Slightly under full white so it sits on the scene rather
    /// than glaring off it.
    /// </summary>
    private static readonly Color SelectionBorderColor = new(1f, 1f, 1f, 0.88f);

    /// <summary>
    /// The drag ghost's own colour. Grey and faint on purpose: it belongs to no
    /// axis and to no material, so it cannot be read as either, and it stays out of
    /// the way of the border it is offset from and of the handles being pulled.
    /// </summary>
    private static readonly Color DragGhostColor = new(0.82f, 0.84f, 0.87f, 0.3f);

    private readonly Dictionary<Guid, Node3D> _nodesByElementDictionary = new();
    private readonly Dictionary<Node3D, Guid> _elementsByNodeDictionary = new();

    // What each camera element sees through, and whether it is the one the scene
    // renders with. Held rather than asked for, like the symmetry slots beside it:
    // the drawing is rebuilt by four different things — the lens, the choice, the
    // picture's shape and the room's size — and only one of them is a scene change.
    private readonly Dictionary<Guid, (CameraLens Lens, bool Active)>
        _camerasByElementDictionary = new();

    private readonly Dictionary<Guid, MeshInstance3D> _cameraGizmosByElementDictionary = new();

    private readonly Dictionary<Guid, ReferenceImageSettings> _referenceImagesByElementDictionary =
        new();

    private readonly Dictionary<Guid, ReferenceModelSettings> _referenceModelsByElementDictionary =
        new();

    private readonly Dictionary<Guid, ReferenceNode> _referenceNodesByElementDictionary = new();

    /// <summary>
    /// Whether references are drawn. Not the document's — it is a switch for
    /// looking at the model clean for a moment — so it deliberately survives
    /// <see cref="Clear"/> the way the viewport's effects do.
    /// </summary>
    private bool _referencesVisible = true;

    private StandardMaterial3D? _activeCameraMaterial;
    private StandardMaterial3D? _idleCameraMaterial;

    // 16:9, the card's own default, until the render form says otherwise.
    private float _cameraAspect = 16f / 9f;

    /// <summary>
    /// How far down its aim a camera's frustum is drawn, in cells. Derived from
    /// the room rather than written down, the rule this file already keeps for the
    /// zoom limit, the far plane and the shadow budget: a mark sized for a 32-cell
    /// room is a speck in a 208-cell one.
    /// </summary>
    private float CameraGizmoReach { get; set; } = MinimumCameraGizmoReach;

    /// <summary>
    /// A fifth of the room's shortest side: enough that the cone reads as a cone
    /// from across the scene, little enough that it does not swallow the model it
    /// is pointed at.
    /// </summary>
    private const float CameraGizmoFraction = 0.2f;

    private const float MinimumCameraGizmoReach = 3f;
    private readonly Dictionary<Guid, VoxelChunkMeshes> _meshesByElementDictionary = new();
    private readonly Dictionary<Guid, float> _opacityByMaterialDictionary = new();

    // Which layer of the shipped arrays a material reads, or absent for one of
    // the project's own. See StandardMaterialAtlas.
    private readonly Dictionary<Guid, AtlasPlacement> _atlasByMaterialDictionary = new();
    private readonly StandardMaterialAtlas _atlas = new();

    private readonly Dictionary<Guid, IReadOnlyDictionary<Vector3I, Guid>> _voxelsByElementDictionary = new();
    private readonly Dictionary<Guid, SymmetryValues> _symmetryByElementDictionary = new();
    private readonly Dictionary<Guid, Material> _materialsByIdDictionary = new();

    private readonly Dictionary<Guid, VoxelMaterialDescription>
        _materialDescriptionsByIdDictionary = new();

    private ViewportEffects _effects = ViewportEffects.Full;

    // One marker per cell of the brush's orbit, pooled: hidden rather than freed
    // when a smaller orbit needs fewer, because the count changes every time the
    // pointer crosses a mirror plane.
    private readonly List<MeshInstance3D> _placePreviewsList = new();
    private readonly List<MeshInstance3D> _eraseWireframesList = new();

    // One laser contour per enabled mirror plane of the selected element, a
    // child of its node like the selection box, so the engine carries the
    // element's transform. Materials are lazy and shared across rebuilds.
    private readonly MeshInstance3D?[] _laserInstances = new MeshInstance3D?[3];
    private readonly Material?[] _laserMaterialsByAxis = new Material?[3];

    // The wall half of the laser: world-space children of this node, because
    // the trace lives on the region's walls, not on the element. Which walls
    // carry it follows the camera, so the set is remembered and compared per
    // frame — geometry is rebuilt when it changes, never merely because a
    // frame passed.
    private readonly MeshInstance3D?[] _wallLaserInstances = new MeshInstance3D?[3];
    private int _litWallsMask;
    private readonly ViewportSurface _surface;

    // The referee between camera and brush. While the camera is being driven —
    // any gesture, plus a short cooldown after — the brush yields and shows
    // nothing, so nothing is placed mid-turn and a held Shift is the camera's to
    // slide with rather than the brush's to erase with. Read here, never off the
    // focus marker, which is only its picture. The transform handles yield to it
    // on exactly the same terms.
    private readonly CameraActivity _cameraActivity = new();

    private readonly TransformGizmo _gizmo = new();

    // Whether the selected element's bounds are drawn. Not a boolean anybody sets:
    // the transform tools and an inactive viewport each raise their own reason, and
    // the box is on while either stands. See SelectionBoundsSwitch.
    private readonly SelectionBoundsSwitch _selectionBounds = new();

    // Whose pointer and keys the tools read: the user's, or — while an agent holds
    // the editor — the agent's. Polled state is the third channel control has to
    // be exclusive on, beside commands and events.
    private readonly EditorInputState _input;

    // Whether a window point is claimed by shell chrome laid over the viewport.
    // See SetChromeClaims.
    private Func<Vector2, bool>? _chromeClaims;

    // Navigation furniture, like the focus marker: it shows where the camera is
    // looking and asks it to look elsewhere, and none of that is model state.
    private readonly ViewCube _viewCube;
    private readonly MotionTiming _timing;

    // The drag's own numbers, in a corner of the viewport. A Control, so the shell
    // places it, and this only ever hands it a line of text.
    private readonly DragReadout _dragReadout;

    private Node3D? _sceneRoot;
    private SceneBox? _box;
    private readonly DirectionalLight3D _keyLight;

    private OrbitCamera? _camera;
    private Camera3D? _camera3D;
    private CanvasLayer? _chrome;

    /// <summary>
    /// The look, shared with the chrome. The scene takes two colours from it —
    /// the void and the room — and nothing else: what a model is measured by
    /// stays where it is. See <see cref="EditorPalette.SceneBackground"/>.
    /// </summary>
    private readonly EditorSkin _skin;

    private readonly Godot.Environment _environment;

    /// <summary>
    /// The window's own backdrop, behind the 3D. A canvas rather than a colour
    /// or a sky, because it must not turn with the camera.
    /// </summary>
    private readonly TextureRect _void = new() { Name = "Void" };

    private readonly GradientTexture2D _voidGradient = new();

    /// <summary>
    /// A border per selected element. Keyed by element rather than held as one
    /// node, because a selection is a set: each border hangs beside its own
    /// element's node and is rebuilt, carried and detached on its own.
    /// </summary>
    private readonly Dictionary<Guid, SelectionBorder> _bordersByElementDictionary = new();

    private Vector3I _size;
    private Guid? _pickTarget;

    /// <summary>
    /// Every element wearing a border. The order is the selection's, and it is kept
    /// so that a rebuild puts the borders back in the order they were asked for.
    /// </summary>
    private readonly List<Guid> _highlightedElementsList = new();

    /// <summary>
    /// The one the editor speaks for: where the handles sit and whose mirror planes
    /// the lasers draw. Always one of <see cref="_highlightedElementsList"/>, or
    /// null when nothing in this scene is selected.
    /// </summary>
    private Guid? _highlightTarget;
    private Guid _brushMaterialId;
    private ToolKind _tool;
    // Only stands in until a brush material has arrived; see GetPlacePreviewMaterial.
    private Material? _placePreviewMaterial;
    private Material? _eraseWireframeMaterial;
    private Material? _samplePreviewMaterial;
    private Node3D? _placePreviewHost;

    private readonly List<MeshInstance3D> _samplePreviewsList = new();
    private Material? _selectionBoxMaterial;
    private Material? _dragGhostMaterial;
    private bool _previewWasOffered;

    // The ghost of the element where the drag has pulled it, and whose cells it was
    // built from — so the box is rebuilt when the element changes and merely moved
    // on the frames only the transform did.
    private MeshInstance3D? _dragGhost;
    private Guid _dragGhostElement;

    /// <summary>The box the ghost was built around, kept so a scale change need not re-walk the cells.</summary>
    private VoxelExtent _dragGhostExtent;

    /// <summary>The scale baked into the ghost's corners; infinity until one has been.</summary>
    private Vector3 _dragGhostScale = Vector3.Inf;

    // The extrude gesture in flight, if any. The press grabs a face, the pull is
    // measured against an axis captured in world space at that press — the
    // gizmo's own rule, for the gizmo's own reason: nothing may move under a
    // drag — and the release answers with a layer count through the ordinary
    // Picked event.
    private ExtrudeDragState? _extrudeDrag;
    private Vector3 _extrudeAxisOrigin;
    private Vector3 _extrudeAxisDirection;

    // The brush stroke in flight, if any: opened by the press, fed by the hover's
    // proposal every frame the button is held, closed by the release. Its written
    // cells are what TryPick reads the target's surface through; see BrushStroke.
    private BrushStroke? _brushStroke;

    // What the cube was last turned to, so it is repainted on the frames the camera
    // moved and on no others. Started off the pose so the first frame syncs it.
    private float _cubeYawDegrees = float.NaN;
    private float _cubePitchDegrees = float.NaN;

    // Which zone the turn in flight is heading for, so a second press on the same
    // one reads as "stop" rather than as "start again from here".
    private Vector3I _animationZone;

    /// <summary>
    /// The surface is built outside and handed in because it is a
    /// <see cref="Control"/>, and where a Control sits on screen is the shell's
    /// business — it is the only thing that knows what else is on screen. What
    /// arrives through it is entirely this view's business, which is why the
    /// wiring is here.
    /// </summary>
    public SceneView(
        ViewportSurface surface,
        EditorInputState input,
        ViewCube viewCube,
        DragReadout dragReadout,
        MotionTiming timing,
        EditorSkin skin)
    {
        _surface = surface;
        _input = input;
        _viewCube = viewCube;
        _dragReadout = dragReadout;
        _timing = timing;
        _skin = skin;

        _environment = new Godot.Environment
        {
            // The background is a 2D canvas, not a colour and not the sky: the
            // void behind the room is a gradient on the *screen*, which does not
            // turn as the camera does. BackgroundCanvasMaxLayer is what picks it
            // out — every layer at or below it is drawn as the 3D's backdrop,
            // and the chrome's own layer is above, so it still draws in front.
            BackgroundMode = Godot.Environment.BGMode.Canvas,
            BackgroundCanvasMaxLayer = VoidLayer,

            // Not the background: the ambient is a colour of its own, which is
            // the fact that lets the void follow the theme without a single
            // voxel changing shade. Do not make this AmbientSource.Bg — a light
            // theme would then light the model.
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.55f, 0.6f),
            AmbientLightEnergy = 0.35f,

            // A sky nothing ever sees: the background is the canvas above, and
            // this is here only so a reflective surface has something to
            // reflect. Without it a metal voxel reflects one constant and reads
            // as dark paint — the shading model would be right and the material
            // would still look wrong. It is the same in both themes, which is
            // what keeps a theme from reaching any part of a model.
            Sky = CreateReflectionSky(),
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky
        };

        AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = _environment });
        AddChild(CreateVoid());

        // The scene's two scenery colours follow the theme, and are re-applied
        // when it is repainted. Pushed rather than looked up: a Node3D has no
        // theme, and a shader looks nothing up either.
        _skin.Changed += ApplySceneColors;
        ApplySceneColors();

        // One real light for everything: the box and the voxels share the same
        // Lambert model, so faking per-axis shading would only hide bugs.
        //
        // Aimed and weighted like Goxel's, because that is the look this editor is
        // being held to. Its default light is pitch 20 / yaw 120 about a Z-up
        // world, which carried into Godot's Y-up frame is 70 degrees above the
        // horizon from the same quarter — high enough that shadows stay tucked under
        // what casts them instead of striping the model. Its shadow is deliberately
        // faint (strength 0.3, and a fully shadowed pixel still keeps three quarters
        // of its colour), which ShadowOpacity says here: a shadowed face gives up
        // most of its direct light, not all of it. The soft creases where surfaces
        // meet are not this light's doing at all — see VoxelAmbientOcclusion.
        // How far it carries its shadows is not set here: it comes from the room,
        // in SetRoot, the way the zoom limit does. The fade fraction is pinned to
        // the constant that budget is computed against, so the two cannot drift.
        _keyLight = new DirectionalLight3D
        {
            Name = "KeyLight",
            RotationDegrees = new Vector3(-70f, 120f, 0f),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
            ShadowOpacity = 0.3f,
            DirectionalShadowFadeStart = CameraFraming.ShadowFadeStart,
            LightCullMask = RenderLayers.Voxels | RenderLayers.SceneBox
        };

        AddChild(_keyLight);

        AddChild(new DirectionalLight3D
        {
            Name = "FillLight",
            RotationDegrees = new Vector3(-15f, 55f, 0f),
            LightEnergy = 0.25f,
            ShadowEnabled = false,
            LightCullMask = RenderLayers.Voxels | RenderLayers.SceneBox
        });

        // Not under the element it acts on: the handles wear the frame the
        // element's values are written in and a size measured against the camera,
        // neither of which is anything the element's own node could give them.
        AddChild(_preview);
        AddChild(_gizmo);

        _gizmo.DragBegan += () => GizmoDragBegan?.Invoke();
        _gizmo.Dragged += delta => GizmoDragged?.Invoke(delta);
        _gizmo.DragEnded += () => GizmoDragEnded?.Invoke();
        _gizmo.DragCancelled += () => GizmoDragCancelled?.Invoke();

        // The viewport's claim on the window and on the keyboard, and the only
        // way input reaches the scene: hovering a panel is not hovering the
        // scene, typing in one is not turning the camera, and the engine answers
        // both — by hit test and by focus.
        _surface.InputHandler = OnSurfaceInput;
        _surface.KeyboardLost += OnKeyboardLost;

        _viewCube.ZonePressed += OnViewCubeZonePressed;
    }

    /// <summary>
    /// A face, edge or corner of the navigation cube was pressed. It asks for a
    /// pose through the very setter an aim command and a handover restore use, so
    /// the turn obeys the same clamps and leaves the same trace as any other camera
    /// move — the cube is a way of asking, not a second camera.
    /// </summary>
    /// <remarks>
    /// A press is always an interrupt first: whatever turn was running is given up
    /// where it stands, and only then is it asked whether a new one should start.
    /// Pressing the zone already being turned to therefore stops the camera rather
    /// than restarting the same journey, which is the reading of a repeat press
    /// that leaves the user in control of it.
    /// <para>
    /// Refused outright while a transform handle is held, because a gesture in
    /// flight owns the viewport: the drag's axis was captured in world space at the
    /// press precisely so that nothing moves under it, and turning the camera
    /// through one would be doing exactly that.
    /// </para>
    /// </remarks>
    private void OnViewCubeZonePressed(Vector3I zone)
    {
        if (_camera == null || _gizmo.IsDragging)
            return;

        bool wasAnimating = _camera.IsAnimating;
        Vector3I previousZone = _animationZone;

        _camera.CancelAnimation();

        if (wasAnimating && previousZone == zone)
            return;

        _animationZone = zone;
        _camera.AnimateTo(ViewCubeGeometry.GetPose(_camera.GetPose(), zone));
    }

    /// <summary>
    /// The chrome layer to hide for one frame when capturing a clean shot. Handed
    /// in after construction because the shell that is this layer is built second,
    /// around the scene view it is given. Hiding it — rather than rendering the 3D
    /// into a SubViewport — is deliberate: the whole app assumes the chrome sits on
    /// a CanvasLayer over a main-window 3D layer (input hit-testing, the viewport
    /// focus ring, window-space picking all read that layout), and a one-frame hide
    /// leaves every bit of it untouched.
    /// </summary>
    public void SetChrome(CanvasLayer chrome) => _chrome = chrome;

    /// <summary>
    /// Lets the shell say a window point belongs to chrome laid over the
    /// viewport — a rail's temporary panel. The polled affordances ask it beside
    /// the view cube's own exception, so the brush does not propose cells behind
    /// an open overlay. Clicks never needed this; the GUI hit test routes them.
    /// </summary>
    public void SetChromeClaims(Func<Vector2, bool> claims) => _chromeClaims = claims;

    /// <summary>
    /// Drops the document's contents and nothing else. The scene root, the box
    /// and the camera are the viewport's own furniture — created once and
    /// reconfigured by <see cref="SetRoot"/>, because a view that freed them here
    /// would be reaching for them again a line later.
    /// </summary>
    /// <remarks>
    /// This is the whole of "the open document is gone", and everything derived
    /// from one belongs in it. The rule the symmetry lasers cost us: a node built
    /// from the document either hangs under the scene root and dies with it, or is
    /// named here — nothing may be left for whoever pushes the next value to tidy
    /// up, because on a fresh document nobody pushes a value for what the old one
    /// had. What deliberately stays is the pooled chrome — the brush's markers, the
    /// preview boxes — which belongs to no document and is hidden rather than freed.
    /// </remarks>
    public void Clear()
    {
        // A pull whose element is going away with the document has nothing left
        // to answer with, and a stroke's cells have already gone through the model
        // — closing it is what lets the presenter close the record.
        CancelExtrudeDrag();
        CompleteStroke();

        // Before the elements go: the box lives under one of them, and a freed
        // parent takes its children with it. The drag ghost hangs the same way,
        // and the lasers — both halves, see RebuildLasers.
        SetSelectionHighlight(Array.Empty<Guid>(), null, Vector3.Zero);
        SetDragPreview(null);

        // Nothing to resolve a click against until the next document says so. The
        // brush's material goes the same way: it names a material of the project
        // being closed.
        SetPickTarget(null);
        _brushMaterialId = Guid.Empty;
        _previewWasOffered = false;

        if (_sceneRoot != null)
        {
            foreach (Node child in _sceneRoot.GetChildren())
                child.QueueFree();
        }

        _nodesByElementDictionary.Clear();
        _elementsByNodeDictionary.Clear();
        _meshesByElementDictionary.Clear();
        _voxelsByElementDictionary.Clear();
        _symmetryByElementDictionary.Clear();

        // The gizmos are freed with their elements above, so only the bookkeeping
        // goes here — but it must go, or the next document's first aspect change
        // would rebuild marks for cameras that no longer exist.
        _camerasByElementDictionary.Clear();
        _cameraGizmosByElementDictionary.Clear();

        // Same terms as the gizmos: the nodes die with their elements above, the
        // bookkeeping does not, and a table keyed by an element id that survived a
        // document would draw the next one's element with the last one's picture.
        _referenceImagesByElementDictionary.Clear();
        _referenceModelsByElementDictionary.Clear();
        _referenceNodesByElementDictionary.Clear();

        // The palette is the project's, not the viewport's: keeping it would leave
        // the next document's meshes able to resolve a material nobody can name.
        // Every table keyed by a material id goes, not merely the one holding the
        // materials themselves — the opacity decides which faces exist and the
        // layer decides which picture is on them, so either one surviving a
        // document would draw the next one's cells with the last one's answer.
        _materialsByIdDictionary.Clear();
        _materialDescriptionsByIdDictionary.Clear();
        _opacityByMaterialDictionary.Clear();
        _atlasByMaterialDictionary.Clear();

        HideBrushPreview();
    }

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

    public void FocusOn(Guid targetId)
    {
        if (_camera == null || !_nodesByElementDictionary.TryGetValue(targetId, out Node3D? node))
            return;

        // The scene is a room rather than an object: what frames it is the
        // buildable region, which nothing under it knows the shape of.
        _camera.FocusOn(ReferenceEquals(node, _sceneRoot)
            ? new Aabb(Vector3.Zero, new Vector3(_size.X, _size.Y, _size.Z))
            : CalculateWorldBounds(node));
    }

    public CameraPose GetCameraPose() => _camera?.GetPose() ?? default;

    public CameraView GetCameraView() => _camera?.GetView() ?? default;

    public void SetCameraPose(CameraPose pose) => _camera?.SetPose(pose);

    /// <summary>
    /// An agent has the editor, or has given it back. Two things follow: the
    /// user's orbit marker goes away, so a captured frame is the scene alone — and
    /// the tools start reading the agent's pointer and keys instead of the
    /// machine's, which is what stops a bystander's Shift from changing what the
    /// brush is about to do.
    /// </summary>
    public void SetAgentControlled(bool controlled)
    {
        // Control changing hands breaks a gesture in flight, the shell's own rule
        // for its drags: the party whose button is down is no longer the party
        // being read, so the stroke closes on what it has rather than reading a
        // stranger's pointer as its own.
        CompleteStroke();

        _camera?.SetMarkerSuppressed(controlled);
        _input.SetAgentDriving(controlled);
    }

    /// <summary>
    /// Reads a frame off the viewport, then downscales and encodes it per the
    /// options. Only the read has to happen on the exact frame — and, for a clean
    /// shot, with the chrome hidden; the scaling and encoding work on the captured
    /// image afterwards, with the chrome already back.
    /// </summary>
    /// <summary>
    /// The stage a material preview is rendered on. Built with the view, because a
    /// viewport added inside the call that photographs it has not been through a
    /// frame and comes back blank.
    /// </summary>
    private readonly MaterialPreviewStage _preview = new() { Name = "MaterialPreview" };

    public FrameCapture CapturePreview(
        string name,
        Color color,
        MaterialAppearance appearance,
        MaterialPreviewTurn turn,
        int size,
        FrameFormat format,
        float quality)
    {
        // Turn first: setting the material re-frames the camera from the block's
        // own size, and both have to be settled before the one forced draw.
        _preview.Turn(turn);
        _preview.SetMaterial(name, color, appearance);

        Image image = _preview.Capture(size);
        byte[] bytes = Encode(image, format, quality);

        // A preview has no window to be a fraction of, so it reports a mapping of
        // one to one over its own square rather than pretending to be a frame.
        var pixels = new Vector2I(image.GetWidth(), image.GetHeight());

        return new FrameCapture(bytes, pixels.X, pixels.Y, pixels, Vector2.Zero, Vector2.One);
    }

    public FrameCapture CaptureFrame(FrameCaptureOptions options)
    {
        Image image = ReadViewport(options.IncludeChrome);

        var fullSize = new Vector2I(image.GetWidth(), image.GetHeight());

        // Crop first, then scale: a region is a window on the full-size frame, and
        // the scale then shrinks whatever it framed.
        Rect2I crop = ClampRegion(fullSize, options.Region);

        if (crop.Position != Vector2I.Zero || crop.Size != fullSize)
            image = image.GetRegion(crop);

        Downscale(image, options.Scale);

        byte[] bytes = Encode(image, options.Format, options.Quality);
        var finalSize = new Vector2I(image.GetWidth(), image.GetHeight());
        Vector2I windowSize = GetWindow().Size;

        (Vector2 origin, Vector2 scale) =
            FrameCoordinates.Resolve(fullSize, windowSize, crop, finalSize);

        return new FrameCapture(bytes, finalSize.X, finalSize.Y, windowSize, origin, scale);
    }

    /// <summary>
    /// Forces one frame to draw so the viewport texture reflects this instant
    /// rather than the last one, and reads it back. The force-draw is what lets
    /// this be one synchronous call an agent command can return from, instead of
    /// waiting a frame.
    /// </summary>
    /// <remarks>
    /// A clean shot hides the chrome for the duration. Hiding it — rather than
    /// rendering the 3D into a SubViewport — is deliberate: the whole app assumes
    /// the chrome sits on a CanvasLayer over a main-window 3D layer, and a
    /// one-frame hide leaves every bit of that untouched.
    /// </remarks>
    private Image ReadViewport(bool includeChrome)
    {
        bool chromeWasVisible = _chrome is { Visible: true };

        // Hiding the chrome releases whichever control held the keyboard, and
        // showing it back does not return it — so without this a clean shot
        // quietly took the focus with it. That was the mechanism behind "a
        // brush click straight after a capture is swallowed": no keyboard, no
        // preview offered, and the click refused to paint blind.
        Control? focusOwner = GetViewport().GuiGetFocusOwner();

        if (_chrome != null && !includeChrome)
            _chrome.Visible = false;

        try
        {
            RenderingServer.ForceDraw();

            return GetViewport().GetTexture().GetImage();
        }
        finally
        {
            if (_chrome != null)
                _chrome.Visible = chromeWasVisible;

            if (focusOwner != null
                && IsInstanceValid(focusOwner)
                && focusOwner.IsVisibleInTree()
                && !focusOwner.HasFocus())
            {
                focusOwner.GrabFocus();
            }
        }
    }

    /// <summary>
    /// The requested region brought inside the frame — this is the one place the
    /// frame's real pixel size is known, so it is where an out-of-bounds region is
    /// clamped rather than refused. A degenerate rect collapses to a single pixel
    /// instead of throwing. No region at all is the whole frame.
    /// </summary>
    private static Rect2I ClampRegion(Vector2I size, Rect2I? region)
    {
        if (region is not { } rect)
            return new Rect2I(Vector2I.Zero, size);

        int x = Mathf.Clamp(rect.Position.X, 0, Mathf.Max(0, size.X - 1));
        int y = Mathf.Clamp(rect.Position.Y, 0, Mathf.Max(0, size.Y - 1));

        return new Rect2I(
            x,
            y,
            Mathf.Clamp(rect.Size.X, 1, size.X - x),
            Mathf.Clamp(rect.Size.Y, 1, size.Y - y));
    }

    private static void Downscale(Image image, float scale)
    {
        if (scale >= 1f)
            return;

        int width = Mathf.Max(1, Mathf.RoundToInt(image.GetWidth() * scale));
        int height = Mathf.Max(1, Mathf.RoundToInt(image.GetHeight() * scale));

        // Lanczos over bilinear: a downscaled screenshot is judged by its edges,
        // and thin voxel seams alias badly under a cheaper filter.
        image.Resize(width, height, Image.Interpolation.Lanczos);
    }

    private static byte[] Encode(Image image, FrameFormat format, float quality) => format switch
    {
        FrameFormat.Jpeg => image.SaveJpgToBuffer(quality),
        FrameFormat.Webp => image.SaveWebpToBuffer(lossy: true, quality),
        _ => image.SavePngToBuffer()
    };

    public void SetPickTarget(Guid? elementId)
    {
        // A stroke writes to the element it began on; the target changing under it
        // — the element deleted, the selection moved by an undo — closes it.
        if (elementId != _pickTarget)
            CompleteStroke();

        _pickTarget = elementId;

        if (elementId == null)
            HideBrushPreview();
    }

    public void SetBrushMaterial(Guid materialId)
    {
        _brushMaterialId = materialId;
    }

    public void SetSelectionHighlight(
        IReadOnlyList<Guid> elementIds, Guid? activeId, Vector3 pivotPoint)
    {
        _highlightedElementsList.Clear();

        for (var index = 0; index < elementIds.Count; index++)
            _highlightedElementsList.Add(elementIds[index]);

        _highlightTarget = activeId;

        RebuildSelectionBoxes();
        RebuildLasers();

        _gizmo.SetTarget(
            activeId is { } id && _nodesByElementDictionary.TryGetValue(id, out Node3D? node)
                ? node
                : null,
            pivotPoint);
    }

    /// <summary>
    /// The drag in flight, as the ghost of where it is pulling the element and the
    /// line of numbers that says how far past the applied value that is. Null clears
    /// both.
    /// </summary>
    /// <remarks>
    /// The ghost is a child of the element's <em>parent</em> wearing the transform
    /// the drag asked for — which is exactly how the border is placed now, so the two
    /// boxes are comparable by construction. It is the same mesh in a fainter grey:
    /// what it says is "the box you are watching, where your hand actually is", and a
    /// second shape to learn would say less.
    /// <para>
    /// The scale is baked into the corners rather than worn by the node, for the
    /// reason the border's is — a zero column is a transform the renderer drops
    /// silently, and a scale drag passes through zero on its way to negative. The
    /// extent is measured once and kept, so a scale changing every frame costs the
    /// twenty-four vertices and not a walk over every filled cell.
    /// </para>
    /// </remarks>
    public void SetDragPreview(DragPreview? preview)
    {
        if (preview is not { Pending: true } pending
            || !_nodesByElementDictionary.TryGetValue(pending.ElementId, out Node3D? node)
            || node.GetParent() is not Node3D parent
            || !_voxelsByElementDictionary.TryGetValue(
                pending.ElementId, out IReadOnlyDictionary<Vector3I, Guid>? voxels))
        {
            DetachDragGhost();
            _dragReadout.SetText(preview?.Readout ?? string.Empty);

            return;
        }

        if (_dragGhost == null || _dragGhostElement != pending.ElementId || _dragGhost.GetParent() != parent)
        {
            DetachDragGhost();

            // Measured where the ghost is created rather than on every frame of the
            // drag: the walk is over every filled cell, and the cells do not move
            // while the element they belong to is being pulled about.
            _dragGhostExtent = VoxelExtent.Measure(voxels.Keys);

            if (_dragGhostExtent.IsEmpty)
            {
                _dragReadout.SetText(pending.Readout);

                return;
            }

            _dragGhost = new MeshInstance3D
            {
                Name = "DragGhost",
                MaterialOverride = _dragGhostMaterial ??= CreateDragGhostMaterial(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Layers = RenderLayers.Voxels
            };

            _dragGhostElement = pending.ElementId;
            _dragGhostScale = Vector3.Inf;
            parent.AddChild(_dragGhost);
        }

        TransformValues values = pending.Values;

        // The same composition every element node gets — position names the pivot
        // — with one twist: the ghost's node carries no scale (the corners do), so
        // the scale multiplies the pivot by hand before the rotation turns it.
        _dragGhost.RotationDegrees = values.RotationDegrees;
        _dragGhost.Position =
            values.Position - _dragGhost.Basis * (values.Scale * pending.PivotPoint);

        if (!values.Scale.IsEqualApprox(_dragGhostScale))
        {
            _dragGhostScale = values.Scale;
            _dragGhost.Mesh = SelectionBox.Build(
                _dragGhostExtent.Corner * _dragGhostScale,
                _dragGhostExtent.FarCorner * _dragGhostScale);
        }

        _dragReadout.SetText(pending.Readout);
    }

    /// <summary>
    /// Out of the tree before it is freed, like the selection box and for the same
    /// reason: the element it hangs beside may be on its way out with its parent.
    /// </summary>
    private void DetachDragGhost()
    {
        if (_dragGhost == null)
            return;

        _dragGhost.GetParent()?.RemoveChild(_dragGhost);
        _dragGhost.QueueFree();
        _dragGhost = null;
        _dragGhostElement = Guid.Empty;
    }

    /// <summary>
    /// Which tool is active is decided a layer up and pushed down here, so the
    /// viewport keeps no opinion of its own about what a key meant. Everything the
    /// brush does hangs off this: outside the tools whose click aims at cells —
    /// <c>IsCellTool</c> names them — it offers nothing, and so — through
    /// <c>_previewWasOffered</c> — a click does nothing.
    /// </summary>
    public void SetTool(ToolKind tool)
    {
        // Changing tools mid-pull abandons the gesture, the gizmo's own rule:
        // the face being held is not something the new tool has a grip on. A
        // stroke closes rather than being abandoned — its cells are already in
        // the model — which is the same sentence for a gesture that acts as it
        // goes.
        if (tool != _tool)
        {
            CancelExtrudeDrag();
            CompleteStroke();
        }

        _tool = tool;
        _gizmo.SetTool(tool);

        // Every tool change lowers the tool's claim on the bounds box; the tools
        // that want it raise it again in the same call. Nothing has to remember
        // what the tool being left behind had asked for.
        if (_selectionBounds.Set(
                SelectionBoundsReason.TransformTool, SelectionBoundsSwitch.RaisedByTool(tool)))
        {
            RebuildSelectionBoxes();
        }

        if (!IsCellTool())
        {
            _previewWasOffered = false;
            HideBrushPreview();
        }
    }

    /// <summary>
    /// The surface is the focusable widget, not this node — the keyboard belongs
    /// to whichever panel holds it, and the viewport's panel is the surface.
    /// </summary>
    public void TakeKeyboard() => _surface.GrabFocus();

    /// <summary>
    /// A border for every selected element. Rebuilt whole, because the switch that
    /// decides whether any are drawn at all is one answer for the lot.
    /// </summary>
    private void RebuildSelectionBoxes()
    {
        // Everything comes down before anything is decided — the standing rule, and
        // it bites harder now that there are several: a selection that shrank would
        // otherwise leave the borders of the elements no longer in it standing.
        DetachSelectionBoxes();

        if (!_selectionBounds.IsOn)
            return;

        for (var index = 0; index < _highlightedElementsList.Count; index++)
            AttachSelectionBox(_highlightedElementsList[index]);
    }

    /// <summary>
    /// One element's border, put back. What a change to that element's own cells
    /// needs — the box is sized from them — without the rest of the selection
    /// paying for a walk over cells that did not move.
    /// </summary>
    private void RebuildSelectionBox(Guid id)
    {
        DetachSelectionBox(id);

        if (_selectionBounds.IsOn && _highlightedElementsList.Contains(id))
            AttachSelectionBox(id);
    }

    /// <summary>
    /// The box around one selected element's own voxels — a child of the element's
    /// <em>parent</em>, wearing the element's position and rotation, with its scale
    /// baked into the corners. The border still hugs the object rather than being an
    /// axis-aligned box around it; it is composed as <c>T · R · (S · v)</c>, which is
    /// the element's own frame written out rather than approximated.
    /// </summary>
    /// <remarks>
    /// Drawn only while something is asking for it — see
    /// <see cref="SelectionBoundsSwitch"/>. Under a cell tool in an active viewport
    /// nobody is, and the selection is then said by the project tree's rows alone.
    /// <para>
    /// It used to hang under the element and let the engine apply all three, which
    /// was free and worked until a scale reached exactly zero. **Godot drops an
    /// instance whose transform has a zero column** — the node stays visible and its
    /// global transform reads back correctly, and nothing is drawn and nothing is
    /// logged. Measured either side of it: at a scale of 0.01 the border is there, at
    /// 0 it is gone. A flattened model is a legal thing to have, and it is exactly
    /// when a border is most wanted, so the flatness goes into the geometry where the
    /// renderer can cope with it and the node keeps an invertible frame.
    /// </para>
    /// <para>
    /// A negative scale mirrors the corners past each other, which the edge walk does
    /// not mind — it builds a box from two corners in either order.
    /// </para>
    /// <para>
    /// An element with no voxels of its own — a group, a prefab instance, or a
    /// model not yet painted in — is measured by its <em>subtree</em> instead:
    /// each descendant's cells carried into the container's frame through the
    /// transforms between them, unioned. That box is axis-aligned in the
    /// container's frame rather than hugging a rotated child, which is the honest
    /// shape for "everything this row stands for" — and without it a selected
    /// instance showed nothing at all, which read as selection being broken.
    /// A container with nothing underneath still gets no box: there is nothing to
    /// draw one around, and the active one still gets the gizmo.
    /// </para>
    /// </remarks>
    private void AttachSelectionBox(Guid id)
    {
        if (!_nodesByElementDictionary.TryGetValue(id, out Node3D? node)
            || node.GetParent() is not Node3D parent)
        {
            return;
        }

        if (!TryMeasureLocalBounds(id, node, out Vector3 corner, out Vector3 farCorner))
            return;

        Vector3 scale = node.Scale;

        var box = new MeshInstance3D
        {
            Name = "SelectionBox",
            Mesh = SelectionBox.Build(corner * scale, farCorner * scale),
            MaterialOverride = _selectionBoxMaterial ??= CreateSelectionBoxMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = RenderLayers.Voxels,
            Position = node.Position,
            RotationDegrees = node.RotationDegrees
        };

        parent.AddChild(box);
        _bordersByElementDictionary[id] = new SelectionBorder(box, scale);
    }

    /// <summary>
    /// The box a border is built around, in the element's own unscaled frame. An
    /// element with cells answers with exactly those — the verified behaviour, a
    /// box that hugs the model and whose dashes count its cells — and only an
    /// element with none is measured by its subtree, so growing a leaf into a
    /// parent never changes what its border says.
    /// </summary>
    private bool TryMeasureLocalBounds(
        Guid id,
        Node3D node,
        out Vector3 corner,
        out Vector3 farCorner)
    {
        corner = default;
        farCorner = default;

        if (_voxelsByElementDictionary.TryGetValue(id, out IReadOnlyDictionary<Vector3I, Guid>? voxels))
        {
            VoxelExtent extent = VoxelExtent.Measure(voxels.Keys);

            if (!extent.IsEmpty)
            {
                corner = extent.Corner;
                farCorner = extent.FarCorner;

                return true;
            }
        }

        Aabb? bounds = null;

        foreach (Node child in node.GetChildren())
        {
            if (child is Node3D nested && _elementsByNodeDictionary.ContainsKey(nested))
                AppendLocalBounds(nested, Transform3D.Identity, ref bounds);
        }

        if (bounds is not { } measured)
            return false;

        corner = measured.Position;
        farCorner = measured.End;

        return true;
    }

    /// <summary>
    /// The subtree half of the walk above: <paramref name="fromContainer"/> is the
    /// product of the local transforms between the container and this node's
    /// parent, so a child's cells land in the container's frame by their eight
    /// corners — the same arithmetic the framing walk does in world space.
    /// </summary>
    private void AppendLocalBounds(Node3D node, Transform3D fromContainer, ref Aabb? bounds)
    {
        Transform3D localFromChild = fromContainer * node.Transform;

        if (_elementsByNodeDictionary.TryGetValue(node, out Guid id)
            && _voxelsByElementDictionary.TryGetValue(id, out IReadOnlyDictionary<Vector3I, Guid>? voxels)
            && voxels.Count > 0)
        {
            Aabb childBounds = CalculateVoxelBounds(localFromChild, voxels);
            bounds = bounds?.Merge(childBounds) ?? childBounds;
        }

        foreach (Node child in node.GetChildren())
        {
            if (child is Node3D nested && _elementsByNodeDictionary.ContainsKey(nested))
                AppendLocalBounds(nested, localFromChild, ref bounds);
        }
    }

    /// <summary>
    /// Keeps a border on its element as it is moved. The node has just been
    /// placed, so its origin and rotation are simply copied — the pivot arithmetic
    /// already happened once and must not happen twice. A changed scale is baked
    /// into the corners, so that one needs the mesh built again — which is why the
    /// two are separated rather than the whole thing being rebuilt on every frame
    /// of a drag, where the extent walk would be paid over every filled cell.
    /// </summary>
    private void SyncSelectionBox(Guid id, Node3D node)
    {
        if (!_bordersByElementDictionary.TryGetValue(id, out SelectionBorder? border))
            return;

        if (!node.Scale.IsEqualApprox(border.Scale))
        {
            RebuildSelectionBox(id);

            return;
        }

        border.Node.Position = node.Position;
        border.Node.RotationDegrees = node.RotationDegrees;
    }

    private void DetachSelectionBoxes()
    {
        foreach (KeyValuePair<Guid, SelectionBorder> entry in _bordersByElementDictionary)
            Detach(entry.Value);

        _bordersByElementDictionary.Clear();
    }

    /// <summary>
    /// Taken out of the tree before it is freed, because the element it hangs
    /// under may be on its way out too and a freed parent frees its children —
    /// leaving this holding a reference to something that no longer exists.
    /// </summary>
    private void DetachSelectionBox(Guid id)
    {
        if (_bordersByElementDictionary.Remove(id, out SelectionBorder? border))
            Detach(border);
    }

    private static void Detach(SelectionBorder border)
    {
        border.Node.GetParent()?.RemoveChild(border.Node);
        border.Node.QueueFree();
    }

    /// <summary>
    /// The laser contour of each enabled mirror plane, on the selected element
    /// alone: a mirror means nothing actual on an element the brush cannot
    /// reach. Rebuilt whole on any of its three inputs changing — selection,
    /// cells, slots — and never per frame; the contour is camera-independent
    /// because it deliberately does not sort against the scene's depth.
    /// </summary>
    /// <remarks>
    /// <b>Both halves come down before anything is decided.</b> The wall half used
    /// to be torn down inside <see cref="RebuildWallLasers"/> alone, which the
    /// early return below never reaches — so a selection going away left the wall
    /// traces standing over nothing, and opening another document left the mirror
    /// planes of an element that was no longer in the scene drawn across the room.
    /// The class rule, and it is not about lasers: <b>a rebuild drops everything it
    /// owns before it decides whether to build anything</b>, or its teardown covers
    /// only the paths that go on to build.
    /// </remarks>
    private void RebuildLasers()
    {
        DetachLasers();
        DetachWallLasers();

        if (_highlightTarget is not { } id
            || !_nodesByElementDictionary.TryGetValue(id, out Node3D? node)
            || !_symmetryByElementDictionary.TryGetValue(id, out SymmetryValues symmetry)
            || !_voxelsByElementDictionary.TryGetValue(id, out IReadOnlyDictionary<Vector3I, Guid>? voxels))
        {
            return;
        }

        AttachLaser(node, voxels, 0, symmetry.X);
        AttachLaser(node, voxels, 1, symmetry.Y);
        AttachLaser(node, voxels, 2, symmetry.Z);

        RebuildWallLasers();
    }

    /// <summary>
    /// Whether the selected element has a mirror plane for the walls to carry.
    /// Asked instead of "are instances attached", because a degenerate scale
    /// can leave the slots on with nothing drawn — and the next transform
    /// change has to be able to bring the trace back.
    /// </summary>
    private bool ShouldTraceWalls =>
        _highlightTarget is { } id
        && _symmetryByElementDictionary.TryGetValue(id, out SymmetryValues symmetry)
        && (symmetry.X.Enabled || symmetry.Y.Enabled || symmetry.Z.Enabled);

    /// <summary>
    /// Rebuilds the wall traces from the current camera side of every wall,
    /// remembering the set so <see cref="SyncWallLasers"/> can tell when the
    /// camera carries a wall in or out of the backdrop.
    /// </summary>
    private void RebuildWallLasers()
    {
        DetachWallLasers();

        _litWallsMask = CalculateLitWallsMask();

        if (_camera3D == null
            || _highlightTarget is not { } id
            || !_nodesByElementDictionary.TryGetValue(id, out Node3D? node)
            || !_symmetryByElementDictionary.TryGetValue(id, out SymmetryValues symmetry))
        {
            return;
        }

        Transform3D world = node.GlobalTransform;
        var boxSize = new Vector3(_size.X, _size.Y, _size.Z);

        AttachWallLaser(world, boxSize, 0, symmetry.X);
        AttachWallLaser(world, boxSize, 1, symmetry.Y);
        AttachWallLaser(world, boxSize, 2, symmetry.Z);
    }

    private void AttachWallLaser(
        Transform3D world,
        Vector3 boxSize,
        int axis,
        SymmetryPlane plane)
    {
        if (!plane.Enabled
            || !SymmetryWallTrace.TryGetWorldPlane(
                world, axis, plane.PlaneDoubled, out Plane worldPlane))
        {
            return;
        }

        var pointsList = new List<Vector3>();
        Vector3 camera = _camera3D!.GlobalPosition;

        for (var wallAxis = 0; wallAxis < 3; wallAxis++)
        {
            for (var wallSide = 0; wallSide < 2; wallSide++)
            {
                if (!SymmetryWallTrace.IsWallVisible(wallAxis, wallSide, boxSize, camera))
                    continue;

                pointsList.AddRange(
                    SymmetryWallTrace.TraceWall(boxSize, wallAxis, wallSide, worldPlane));
            }
        }

        var instance = new MeshInstance3D
        {
            Name = $"SymmetryWallLaser{axis}",
            Mesh = CreateLineMesh(pointsList),
            MaterialOverride = _laserMaterialsByAxis[axis] ??= CreateLaserMaterial(axis),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = RenderLayers.Voxels
        };

        AddChild(instance);
        _wallLaserInstances[axis] = instance;
    }

    private void DetachWallLasers()
    {
        // Which walls carried a trace goes with the traces themselves, so the next
        // rebuild compares against nothing rather than against a camera side left
        // over from a document that is gone.
        _litWallsMask = 0;

        for (var axis = 0; axis < _wallLaserInstances.Length; axis++)
        {
            MeshInstance3D? instance = _wallLaserInstances[axis];

            if (instance == null)
                continue;

            instance.GetParent()?.RemoveChild(instance);
            instance.QueueFree();
            _wallLaserInstances[axis] = null;
        }
    }

    /// <summary>
    /// The per-frame half of the wall trace: six dot products against the
    /// camera, and a rebuild only when their answer changes — the same rule
    /// <c>cull_front</c> applies to the box itself, restated on the CPU.
    /// </summary>
    private void SyncWallLasers()
    {
        if (_camera3D == null || !ShouldTraceWalls)
            return;

        if (CalculateLitWallsMask() != _litWallsMask)
            RebuildWallLasers();
    }

    private int CalculateLitWallsMask()
    {
        if (_camera3D == null)
            return 0;

        Vector3 camera = _camera3D.GlobalPosition;
        var boxSize = new Vector3(_size.X, _size.Y, _size.Z);
        var mask = 0;

        for (var axis = 0; axis < 3; axis++)
        {
            if (SymmetryWallTrace.IsWallVisible(axis, 0, boxSize, camera))
                mask |= 1 << (axis * 2);

            if (SymmetryWallTrace.IsWallVisible(axis, 1, boxSize, camera))
                mask |= 1 << (axis * 2 + 1);
        }

        return mask;
    }

    private static ArrayMesh CreateLineMesh(IReadOnlyList<Vector3> points)
    {
        var mesh = new ArrayMesh();

        if (points.Count == 0)
            return mesh;

        var vertices = new Vector3[points.Count];

        for (var index = 0; index < points.Count; index++)
            vertices[index] = points[index];

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;

        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

        return mesh;
    }

    private void AttachLaser(
        Node3D node,
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        int axis,
        SymmetryPlane plane)
    {
        if (!plane.Enabled)
            return;

        var laser = new MeshInstance3D
        {
            Name = $"SymmetryLaser{axis}",
            Mesh = SymmetryLaser.Build(voxels, axis, plane.PlaneDoubled),
            MaterialOverride = _laserMaterialsByAxis[axis] ??= CreateLaserMaterial(axis),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = RenderLayers.Voxels
        };

        node.AddChild(laser);
        _laserInstances[axis] = laser;
    }

    private void DetachLasers()
    {
        for (var axis = 0; axis < _laserInstances.Length; axis++)
        {
            MeshInstance3D? laser = _laserInstances[axis];

            if (laser == null)
                continue;

            laser.GetParent()?.RemoveChild(laser);
            laser.QueueFree();
            _laserInstances[axis] = null;
        }
    }

    /// <summary>
    /// Editor chrome on the erase marker's terms: unshaded, out of the depth
    /// test and the depth write, high render priority — the line must read
    /// wherever it lands, and letting it show through the model is what a
    /// construction laser sighted from the far side would do anyway.
    /// </summary>
    private static StandardMaterial3D CreateLaserMaterial(int axis) => new()
    {
        AlbedoColor = EditorAxisColors.Get(axis),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = true,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        RenderPriority = 127
    };

    /// <summary>
    /// Editor chrome, not scene geometry: the border says "this is selected", so
    /// it must read whatever it is drawn over and whatever light happens to fall
    /// on the model. Unshaded and out of the depth test for that reason alone —
    /// the voxels themselves keep the one real lighting model, which is what the
    /// sacred rule protects.
    /// </summary>
    /// <summary>
    /// The ghost's line: solid, grey and nearly transparent. It wore the accent
    /// briefly and that was wrong twice over — the accent is the transform handles'
    /// colour, so the ghost was competing with the very handles being dragged, and a
    /// saturated box is too loud for something that is only a hint about where the
    /// hand has got to. Neutral and faint reads as "not yet", which is what it means.
    /// </summary>
    private static Material CreateDragGhostMaterial() =>
        SelectionBoxMaterial.CreateSolid(DragGhostColor);

    /// <summary>
    /// The border: white dash-dot, the draughtsman's centre line. See
    /// <see cref="SelectionBoxMaterial"/> for why the two differ in style and not
    /// only in brightness.
    /// </summary>
    private static Material CreateSelectionBoxMaterial() =>
        SelectionBoxMaterial.CreateDashDot(SelectionBorderColor);

    public void SetRoot(Guid sceneId, Vector3I size)
    {
        Clear();

        _size = size;
        var extent = new Vector3(size.X, size.Y, size.Z);

        // The buildable region belongs to the document, so opening one with a
        // different size resizes the box rather than keeping the first one's.
        _box ??= CreateBox();
        _box.SetSize(extent);

        // The shadows reach as far as the camera can, for the same reason the zoom
        // limit is derived rather than fixed: a constant carries a small scene and
        // loses a large one, and a lost shadow reads as a broken one.
        _keyLight.DirectionalShadowMaxDistance = CameraFraming.CalculateShadowDistance(extent);

        // And so is how far a camera gizmo reaches, for the fourth time in this
        // file: a mark sized for a 32-cell room is a speck in a 208-cell one, and
        // the ledger's rule is that any length measured against the room comes off
        // the room rather than being written down.
        CameraGizmoReach = Mathf.Max(
            MinimumCameraGizmoReach,
            Mathf.Min(extent.X, Mathf.Min(extent.Y, extent.Z)) * CameraGizmoFraction);

        _sceneRoot ??= CreateSceneRoot();
        Register(sceneId, _sceneRoot);

        _camera ??= CreateCamera();

        // And so does the far plane — the third of the same family, and the one
        // that was still a constant. Set here rather than at construction because
        // the room changes when a document is opened and the camera does not.
        _camera3D!.Far = CameraFraming.CalculateFarPlane(extent);

        // Before the pivot: the region is what the pivot is clamped against, so
        // setting it second would measure the new focus against the old room.
        _camera.SetRegion(extent);

        // Centre of the floor, not of the box: that is where work starts, and
        // it matches the camera distance the prototype was tuned around.
        _camera.Pivot = new Vector3(extent.X * 0.5f, 0f, extent.Z * 0.5f);
    }

    public void AddElement(Guid id, Guid parentId)
    {
        if (!_nodesByElementDictionary.TryGetValue(parentId, out Node3D? parent))
            throw new ArgumentException($"No scene node {parentId} to add under.", nameof(parentId));

        var node = new Node3D { Name = id.ToString("N") };
        parent.AddChild(node);

        Register(id, node);
    }

    public void RemoveElement(Guid id)
    {
        if (!_nodesByElementDictionary.TryGetValue(id, out Node3D? node))
            return;

        Forget(node);
        node.QueueFree();
    }

    public void SetElementTransform(Guid id, TransformValues values, Vector3 pivotPoint)
    {
        if (!_nodesByElementDictionary.TryGetValue(id, out Node3D? node))
            return;

        // Rotation and scale first: the origin is solved from the basis they
        // produce, so the node's own frame is the arithmetic and cannot drift
        // from it.
        node.RotationDegrees = values.RotationDegrees;
        node.Scale = values.Scale;
        node.Position = values.Position - node.Basis * pivotPoint;

        // The gizmo hangs under the element and so takes its place and its aim for
        // free, but must not take its scale — a camera has no size of its own.
        ApplyCameraGizmoScale(id, node);

        // The border does not hang under the element any more, so it does not come
        // along for free; see AttachSelectionBox for why it cannot. A no-op unless
        // this element is one of the selected ones.
        SyncSelectionBox(id, node);

        // Any transform can be an ancestor's, and an ancestor moves the plane
        // in world space just as surely — so the wall trace follows every one
        // of them rather than guessing at lineage. The contour needs nothing:
        // it hangs under the element and rides along.
        if (ShouldTraceWalls)
            RebuildWallLasers();
    }

    /// <summary>
    /// One line, and the subtree comes with it: Godot draws nothing under a node
    /// that is not visible, which is the same composition rule the exporter walks
    /// by hand. Nothing else here reads the flag — the border and the handles hang
    /// in the parent's frame, so a hidden element can still be picked out in the
    /// hierarchy and moved.
    /// </summary>
    public void SetElementVisible(Guid id, bool visible)
    {
        if (_nodesByElementDictionary.TryGetValue(id, out Node3D? node))
            node.Visible = visible;
    }

    public void SetElementVoxels(
        Guid id,
        IReadOnlyDictionary<Vector3I, Guid> voxels,
        CellRegion? region)
    {
        if (!_nodesByElementDictionary.TryGetValue(id, out Node3D? node))
            return;

        _voxelsByElementDictionary[id] = voxels;

        if (!_meshesByElementDictionary.TryGetValue(id, out VoxelChunkMeshes? meshes))
        {
            meshes = new VoxelChunkMeshes(node);
            _meshesByElementDictionary[id] = meshes;
        }

        // Only the chunks the edit reached: see VoxelChunkMeshes for what a whole
        // rebuild used to cost. A null region is "everything", which is what a
        // fresh document and a change of material both are.
        meshes.Apply(
            voxels,
            _materialsByIdDictionary,
            _opacityByMaterialDictionary,
            region,
            _atlasByMaterialDictionary,
            _atlas.Material);

        // The box is sized from the very cells that just changed, so painting one
        // on at the edge grows it in the same frame the voxel appears — and the
        // laser contour is traced over those same cells.
        RebuildSelectionBox(id);

        // A selected container's box is sized from its subtree, so cells changing
        // in a descendant have to re-measure the ancestor's border too — painting
        // into a model while its group is also selected, most plainly.
        foreach (Guid highlighted in _highlightedElementsList)
        {
            if (highlighted != id && IsAncestorOf(highlighted, node))
                RebuildSelectionBox(highlighted);
        }

        if (id == _highlightTarget)
            RebuildLasers();
    }

    /// <summary>Whether the element is an ancestor of <paramref name="node"/> in the mirror tree.</summary>
    private bool IsAncestorOf(Guid ancestorId, Node3D node)
    {
        if (!_nodesByElementDictionary.TryGetValue(ancestorId, out Node3D? ancestor))
            return false;

        for (Node? walk = node.GetParent(); walk != null; walk = walk.GetParent())
        {
            if (ReferenceEquals(walk, ancestor))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Consulted per frame by the brush preview — which re-derives itself from
    /// the pointer anyway — and rebuilt into the laser contour at once when the
    /// slots belong to the selected element.
    /// </summary>
    public void SetElementSymmetry(Guid id, SymmetryValues values)
    {
        _symmetryByElementDictionary[id] = values;

        if (id == _highlightTarget)
            RebuildLasers();
    }

    /// <summary>
    /// Draws, redraws or takes away an element's camera gizmo. A camera holds no
    /// geometry, so without this it is a row in the tree and nothing on screen —
    /// and a shot nobody can see is a shot nobody can aim.
    /// </summary>
    /// <remarks>
    /// The gizmo hangs under the element's own node, which is what makes it follow
    /// an ancestor for free: the engine composes the world transform, and a mark
    /// parented to the scene root instead would go stale the moment a group holding
    /// the camera moved. What it must <em>not</em> take from that node is the
    /// scale — a camera has no size of its own and the render ignores it — so the
    /// gizmo carries the inverse, re-applied whenever the transform changes.
    /// </remarks>
    public void SetElementCamera(Guid id, CameraLens? lens, bool active)
    {
        if (lens is not { } wanted)
        {
            _camerasByElementDictionary.Remove(id);
            DetachCameraGizmo(id);

            return;
        }

        _camerasByElementDictionary[id] = (wanted, active);

        RebuildCameraGizmo(id);
    }

    public void SetElementReference(
        Guid id,
        ReferenceImageSettings? picture,
        ReferenceModelSettings? model)
    {
        Store(_referenceImagesByElementDictionary, id, picture);
        Store(_referenceModelsByElementDictionary, id, model);

        RebuildReference(id);

        static void Store<T>(Dictionary<Guid, T> table, Guid id, T? value) where T : struct
        {
            if (value is { } held)
                table[id] = held;
            else
                table.Remove(id);
        }
    }

    public void SetReferencesVisible(bool visible)
    {
        if (_referencesVisible == visible)
            return;

        _referencesVisible = visible;

        foreach (ReferenceNode node in _referenceNodesByElementDictionary.Values)
            node.Visible = visible;
    }

    /// <summary>
    /// Rebuilds one reference from what is stored about it, or removes it where the
    /// element has gone. Everything that could change the drawing comes through
    /// here, so there is one place a reference is made.
    /// </summary>
    private void RebuildReference(Guid id)
    {
        bool isPicture = _referenceImagesByElementDictionary.TryGetValue(
            id,
            out ReferenceImageSettings picture);

        bool isModel = _referenceModelsByElementDictionary.TryGetValue(
            id,
            out ReferenceModelSettings model);

        // Taken down here and nowhere else: the element is not a reference at all,
        // which is a different thing from "it is not a picture".
        if (!isPicture && !isModel)
        {
            DetachReference(id);

            return;
        }

        if (!_nodesByElementDictionary.TryGetValue(id, out Node3D? parent))
            return;

        if (!_referenceNodesByElementDictionary.TryGetValue(id, out ReferenceNode? node))
        {
            node = new ReferenceNode { Visible = _referencesVisible };

            parent.AddChild(node);
            _referenceNodesByElementDictionary[id] = node;
        }

        if (isPicture)
            node.Apply(picture);
        else
            node.Apply(model);
    }

    private void DetachReference(Guid id)
    {
        if (!_referenceNodesByElementDictionary.Remove(id, out ReferenceNode? node))
            return;

        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }

    /// <summary>
    /// The shape of the picture that would be rendered. Pushed rather than assumed,
    /// because a frustum drawn square is a promise the render does not keep — what
    /// the wireframe encloses has to be what the picture will contain.
    /// </summary>
    public void SetCameraAspect(float aspect)
    {
        aspect = Mathf.Max(aspect, 0.01f);

        if (Mathf.IsEqualApprox(_cameraAspect, aspect))
            return;

        _cameraAspect = aspect;

        foreach (Guid id in new List<Guid>(_camerasByElementDictionary.Keys))
            RebuildCameraGizmo(id);
    }

    /// <summary>
    /// Rebuilds one camera's gizmo from what is stored about it, or removes it
    /// where the element has gone. Everything that could change the drawing —
    /// the lens, which camera renders, the aspect, the room's size — comes through
    /// here, so there is one place the mark is made.
    /// </summary>
    private void RebuildCameraGizmo(Guid id)
    {
        DetachCameraGizmo(id);

        if (!_camerasByElementDictionary.TryGetValue(id, out (CameraLens Lens, bool Active) entry)
            || !_nodesByElementDictionary.TryGetValue(id, out Node3D? node))
        {
            return;
        }

        var gizmo = new MeshInstance3D
        {
            Name = "CameraGizmo",
            Mesh = CameraFrustum.Build(entry.Lens, _cameraAspect, CameraGizmoReach),
            MaterialOverride = entry.Active
                ? _activeCameraMaterial ??= CreateCameraMaterial(_skin.Palette.Accent)
                : _idleCameraMaterial ??= CreateCameraMaterial(_skin.Palette.IconReceded),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = RenderLayers.Voxels
        };

        node.AddChild(gizmo);
        _cameraGizmosByElementDictionary[id] = gizmo;

        ApplyCameraGizmoScale(id, node);
    }

    /// <summary>
    /// The inverse of whatever the element is scaled by, so the drawing says what
    /// the renderer does: a camera ignores scale. An axis scaled to nothing has no
    /// inverse and is left alone — Godot drops an instance whose basis has a zero
    /// column anyway, which is the same answer the render gives such a camera.
    /// </summary>
    private void ApplyCameraGizmoScale(Guid id, Node3D node)
    {
        if (!_cameraGizmosByElementDictionary.TryGetValue(id, out MeshInstance3D? gizmo))
            return;

        Vector3 scale = node.Scale;

        gizmo.Scale = new Vector3(Invert(scale.X), Invert(scale.Y), Invert(scale.Z));

        static float Invert(float value) => Mathf.IsZeroApprox(value) ? 1f : 1f / value;
    }

    private void DetachCameraGizmo(Guid id)
    {
        if (!_cameraGizmosByElementDictionary.Remove(id, out MeshInstance3D? gizmo))
            return;

        gizmo.GetParent()?.RemoveChild(gizmo);
        gizmo.QueueFree();
    }

    /// <summary>
    /// Lines over whatever they are drawn across, the selection border's own
    /// terms: a camera gizmo is a mark about the scene rather than a thing in it,
    /// and one hidden behind the model it is pointed at would be useless exactly
    /// when it is needed.
    /// </summary>
    private static StandardMaterial3D CreateCameraMaterial(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = true,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        RenderPriority = 126
    };

    public void SetViewportEffects(ViewportEffects effects)
    {
        if (_effects == effects)
            return;

        // What a material is built from: the creases it bakes into its vertex
        // colours and the relief it lays over itself. Shadows are deliberately
        // not in it — they are one light's business, and the canon says so as a
        // test precisely so that nobody folds a third switch in here and rebuilds
        // every material for a setting that touches one boolean.
        bool rebuild = _effects.Occlusion != effects.Occlusion
            || _effects.Factures != effects.Factures;

        _effects = effects;

        // The key light stops casting; the fill never cast anything, and the room
        // is furniture that neither casts nor receives.
        _keyLight.ShadowEnabled = effects.Shadows;

        if (!rebuild)
            return;

        _atlas.SetEffects(effects);

        // A project material's look is derived from the appearance *and* these, so
        // every one of them is built again — from what the document said, never
        // read back off the material standing there.
        foreach (VoxelMaterialDescription description in _materialDescriptionsByIdDictionary.Values)
            ApplyMaterial(description);

        RebuildAllMeshes();
    }

    public void SetMaterial(Guid id, string name, Color color, MaterialAppearance appearance)
    {
        var description = new VoxelMaterialDescription(id, name, color, appearance);

        _materialDescriptionsByIdDictionary[id] = description;
        ApplyMaterial(description);

        RebuildAllMeshes();
    }

    /// <summary>
    /// Writes one material's description into everything the viewport draws it
    /// with. Shared by a document's push and by a change of viewport effects, so
    /// the two cannot come to build a material differently.
    /// </summary>
    private void ApplyMaterial(VoxelMaterialDescription description)
    {
        (Guid id, string name, Color color, MaterialAppearance appearance) = description;

        float opacity = appearance.OpacityOf(color.A);

        // A shipped material is drawn by the one shader every shipped material is
        // drawn by, reading the layer this fills on first use.
        //
        // Opaque ones only, and that is not a caution: the atlas is a single
        // surface appended to the opaque mesh, so a see-through material cannot
        // be in it — Godot's alpha pass sorts instances, and one shared instance
        // is the thing per-material sorting exists to avoid. Without this the
        // material fell between the two: the atlas branch refused it for needing
        // its own instance, and the branch below found no material to give it,
        // because taking a layer is what throws the material away.
        int layer = appearance.Finish == MaterialFinish.Standard
            && !VoxelMeshSet.NeedsOwnInstance(opacity)
                ? _atlas.LayerOf(name)
                : -1;

        if (layer >= 0)
        {
            // How it tiles is the material's own, not the library's: the pack's
            // tile is one unit, so the dial is cells to the tile — the very
            // product the tracer and the preview form for the same material
            // (FinishPeriod.GetCells), asked here so all three tile it alike.
            _atlasByMaterialDictionary[id] = AtlasPlacement.For(layer, appearance);

            // And then no material of its own is built at all. Building one would
            // load the very maps the atlas has just taken a copy of — the same
            // pixels twice, once per material, which is most of what the atlas
            // exists to stop.
            _materialsByIdDictionary.Remove(id);
        }
        else
        {
            _atlasByMaterialDictionary.Remove(id);
            _materialsByIdDictionary[id] = VoxelMaterialFactory.Create(name, color, appearance, _effects);
        }

        // Kept from what the document said rather than read back off the material
        // that was just built: whether a surface is see-through is the material's
        // own declaration, and an alpha sitting unused in an opaque colour would
        // otherwise be mistaken for one.
        _opacityByMaterialDictionary[id] = opacity;
    }

    public void RemoveMaterial(Guid id)
    {
        // Every table keyed by a material id, the descriptions included. Leaving
        // that one out was not inert: a change of viewport effects re-applies
        // every description it holds, so a deleted material put its own entries
        // back into the others on the next toggle.
        _materialDescriptionsByIdDictionary.Remove(id);
        _opacityByMaterialDictionary.Remove(id);
        _atlasByMaterialDictionary.Remove(id);

        if (_materialsByIdDictionary.Remove(id))
            RebuildAllMeshes();
    }

    /// <summary>
    /// Standard PBR, not a bespoke shader: voxels must answer to the same lights
    /// as everything else, and alpha below one is genuine transparency rather
    /// than a look faked with unshaded colours.
    /// </summary>
    /// <remarks>
    /// The vertex colour is the mesher's ambient occlusion
    /// (<see cref="VoxelAmbientOcclusion"/>) and nothing else, so it multiplies the
    /// albedo — darkening the ambient term as much as the direct one, which is what
    /// occlusion means and what Goxel does with it. The mesher has already weakened
    /// it by the material's opacity, so what arrives here is occlusion a pane can
    /// wear without looking dirty. <c>VertexColorIsSrgb</c> is
    /// stated rather than left at its default because it is load-bearing: converted
    /// as sRGB a shade of 0.6 would land near 0.32 and turn a 40% darkening into a
    /// 68% one.
    /// <para>
    /// The place-preview mesh shares this material and carries no colours; Godot
    /// hands an absent vertex colour through as white, so it is unshaded by this.
    /// </para>
    /// </remarks>
    private static Material CreateMaterial(Color color) => new StandardMaterial3D
    {
        AlbedoColor = color,
        Transparency = color.A < 1f
            ? BaseMaterial3D.TransparencyEnum.Alpha
            : BaseMaterial3D.TransparencyEnum.Disabled,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        Roughness = 1f,
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = false
    };

    /// <summary>
    /// A plain gradient, keyed to the viewport's own background so a reflection
    /// looks like the room the model is standing in rather than an outdoor scene
    /// borrowed from somewhere else.
    /// </summary>
    internal static Sky CreateReflectionSky() => new()
    {
        SkyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.58f, 0.62f, 0.72f),
            SkyHorizonColor = new Color(0.82f, 0.84f, 0.88f),
            GroundHorizonColor = new Color(0.42f, 0.42f, 0.46f),
            GroundBottomColor = new Color(0.22f, 0.22f, 0.25f),
            SunAngleMax = 1f,
            EnergyMultiplier = 1f
        }
    };

    /// <summary>
    /// The layer the backdrop is drawn on, and what
    /// <see cref="Godot.Environment.BackgroundCanvasMaxLayer"/> is set to. Well
    /// below the shell's, which is where the chrome lives and has to stay in
    /// front.
    /// </summary>
    private const int VoidLayer = -100;

    private CanvasLayer CreateVoid()
    {
        _voidGradient.Fill = GradientTexture2D.FillEnum.Linear;

        // Top to bottom of the screen, which is the whole point: the gradient is
        // the window's, not the world's, so it does not turn with the camera.
        _voidGradient.FillFrom = new Vector2(0f, 0f);
        _voidGradient.FillTo = new Vector2(0f, 1f);

        _void.Texture = _voidGradient;
        _void.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _void.StretchMode = TextureRect.StretchModeEnum.Scale;
        _void.MouseFilter = Control.MouseFilterEnum.Ignore;
        _void.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var layer = new CanvasLayer { Name = "VoidLayer", Layer = VoidLayer };
        layer.AddChild(_void);

        return layer;
    }

    private Node3D CreateSceneRoot()
    {
        var root = new Node3D { Name = "Scene" };
        AddChild(root);

        return root;
    }

    private SceneBox CreateBox()
    {
        var box = new SceneBox { Name = "SceneBox" };
        box.SetRoomColor(_skin.Palette.SceneRoom);
        AddChild(box);

        return box;
    }

    /// <summary>
    /// The void beyond the room and the room's own walls, from the current
    /// theme. Nothing else in the scene follows it — see
    /// <see cref="EditorPalette.SceneBackground"/> for where that line is and
    /// why it is where it is.
    /// </summary>
    private void ApplySceneColors()
    {
        EditorPalette palette = _skin.Palette;

        _voidGradient.Gradient = new Gradient
        {
            Offsets = new[] { 0f, 1f },
            Colors = new[] { palette.VoidHigh, palette.VoidLow }
        };

        _box?.SetRoomColor(palette.SceneRoom);

        // The accent is the user's, so a camera gizmo drawn in it has to follow a
        // repaint like every other colour read once and held — the rule the
        // control banner's own fields were fixed under.
        if (_activeCameraMaterial != null)
            _activeCameraMaterial.AlbedoColor = palette.Accent;

        if (_idleCameraMaterial != null)
            _idleCameraMaterial.AlbedoColor = palette.IconReceded;
    }

    private OrbitCamera CreateCamera()
    {
        var orbit = new OrbitCamera
        {
            Name = "OrbitCamera",
            Activity = _cameraActivity,
            InputState = _input,
            Timing = _timing
        };

        // No Far here on purpose: it is the room's answer, not the camera's, and
        // SetRoot writes it from the region every time one is opened. A number
        // parked here would read as the budget while being wrong for every scene
        // but the one it was picked for — which is the bug this replaced.
        _camera3D = new Camera3D
        {
            Name = "Camera3D",
            Current = true,
            Fov = 50f,
            Near = 0.05f
        };

        orbit.AddChild(_camera3D);
        orbit.FocusRequested += OnFocusRequested;
        AddChild(orbit);

        return orbit;
    }

    /// <summary>
    /// Passed straight out. The camera reports that framing was asked for; which
    /// object deserves it is a question about the selection, and that is answered
    /// a layer up.
    /// </summary>
    private void OnFocusRequested() => FocusRequested?.Invoke();

    /// <summary>
    /// What an element occupies in world space: the voxels of its whole subtree,
    /// since a grouping element holds none of its own and framing one has to mean
    /// framing what is under it. An element with nothing under it falls back to
    /// its origin — a point, which the framing's minimum radius keeps the camera
    /// from landing on top of.
    /// </summary>
    private Aabb CalculateWorldBounds(Node3D node)
    {
        Aabb? bounds = null;
        AppendWorldBounds(node, ref bounds);

        return bounds ?? new Aabb(node.GlobalPosition, Vector3.Zero);
    }

    private void AppendWorldBounds(Node3D node, ref Aabb? bounds)
    {
        if (_elementsByNodeDictionary.TryGetValue(node, out Guid id)
            && _voxelsByElementDictionary.TryGetValue(id, out IReadOnlyDictionary<Vector3I, Guid>? voxels)
            && voxels.Count > 0)
        {
            Aabb voxelBounds = CalculateVoxelBounds(node.GlobalTransform, voxels);
            bounds = bounds?.Merge(voxelBounds) ?? voxelBounds;
        }

        // A reference holds no cells and is drawn all the same, so a walk that
        // measured voxels alone framed one as a point — the camera diving to its
        // own origin, which reads as focus being broken rather than as a box of
        // nothing.
        if (_elementsByNodeDictionary.TryGetValue(node, out Guid drawn)
            && _referenceNodesByElementDictionary.TryGetValue(drawn, out ReferenceNode? reference)
            && TryMeasureDrawn(reference, out Aabb referenceBounds))
        {
            bounds = bounds?.Merge(referenceBounds) ?? referenceBounds;
        }

        foreach (Node child in node.GetChildren())
        {
            if (child is Node3D nested && _elementsByNodeDictionary.ContainsKey(nested))
                AppendWorldBounds(nested, ref bounds);
        }
    }

    /// <summary>
    /// The world box of everything drawn under a node, whatever it happens to be:
    /// a plane's quad, or the tree of meshes a model file arrived as. Asked of the
    /// instances rather than of the settings, so a model nobody could load measures
    /// as nothing rather than as a guess.
    /// </summary>
    private static bool TryMeasureDrawn(Node node, out Aabb bounds)
    {
        Aabb? found = null;

        Append(node);

        bounds = found ?? default;

        return found != null;

        void Append(Node current)
        {
            if (current is VisualInstance3D instance && instance.Visible)
            {
                Aabb local = instance.GetAabb();

                // By its eight corners, the voxel walk's own rule: a rotated box's
                // world extent is not its local one turned.
                Transform3D world = instance.GlobalTransform;
                var measured = new Aabb(world * local.Position, Vector3.Zero);

                for (var index = 1; index < 8; index++)
                {
                    measured = measured.Expand(world * new Vector3(
                        local.Position.X + ((index & 1) == 0 ? 0f : local.Size.X),
                        local.Position.Y + ((index & 2) == 0 ? 0f : local.Size.Y),
                        local.Position.Z + ((index & 4) == 0 ? 0f : local.Size.Z)));
                }

                found = found?.Merge(measured) ?? measured;
            }

            foreach (Node child in current.GetChildren())
                Append(child);
        }
    }

    /// <summary>
    /// The cells' own box, put into world space by its eight corners rather than
    /// by its extents: a rotated element's world box is not its local one turned,
    /// and taking the shortcut would frame it too tightly.
    /// </summary>
    private static Aabb CalculateVoxelBounds(
        Transform3D worldFromLocal,
        IReadOnlyDictionary<Vector3I, Guid> voxels)
    {
        VoxelExtent extent = VoxelExtent.Measure(voxels.Keys);

        if (extent.IsEmpty)
            return new Aabb(worldFromLocal.Origin, Vector3.Zero);

        Vector3 corner = extent.Corner;
        Vector3 far = extent.FarCorner;
        var world = new Aabb(worldFromLocal * corner, Vector3.Zero);

        for (var index = 1; index < 8; index++)
        {
            world = world.Expand(worldFromLocal * new Vector3(
                (index & 1) == 0 ? corner.X : far.X,
                (index & 2) == 0 ? corner.Y : far.Y,
                (index & 4) == 0 ? corner.Z : far.Z));
        }

        return world;
    }

    /// <summary>
    /// The camera claims the middle button, the right button and Alt+left for
    /// navigation, so the tools get the plain left button and Shift+left. The
    /// order here is a rule, not an accident of the tree: the camera has first
    /// refusal, then the tool keys, and what is left goes to whichever tool is
    /// active — which is why the gizmo and the brush can share the same button
    /// without arbitrating between themselves.
    /// </summary>
    /// <remarks>
    /// Except while a handle is held, which reverses it. The camera and a tool must
    /// never act at once, and for the brush one gate is enough — it proposes
    /// nothing while the camera is being driven, so a click places nothing. A drag
    /// is not an instant: it is already in flight when the arrow key arrives, and a
    /// gate that only refuses to *start* one lets the camera turn under a drag that
    /// has begun. The pointer ray is then measured against a camera that is moving,
    /// and the element slides for reasons the user did not ask for.
    /// <para>
    /// So a gesture in flight owns the viewport until it ends: the camera is not
    /// offered the event, no key reaches anything but the drag, and the event is
    /// claimed either way so nothing downstream — the shell's own shortcuts
    /// included — acts in the middle of it. The gizmo's per-frame check that the
    /// button is still down is what guarantees this cannot outlive the gesture.
    /// </para>
    /// </remarks>
    private bool OnSurfaceInput(InputEvent @event)
    {
        // An extrude drag owns the viewport on the terms a handle drag does:
        // the camera is not offered the event, no key reaches anything but the
        // gesture, and the event is claimed either way.
        if (_extrudeDrag != null)
        {
            HandleExtrudeDragInput(@event);

            return true;
        }

        // A brush stroke owns it on the same terms. Its motion is polled rather
        // than handled here, so the only events it answers are the two that end
        // it; everything else is swallowed for the length of the gesture.
        if (_brushStroke != null)
        {
            HandleStrokeInput(@event);

            return true;
        }

        if (_gizmo.IsDragging)
        {
            TryGizmo(@event);

            return true;
        }

        return (_camera != null && _camera.HandleInput(@event))
            || TryGizmo(@event)
            || TryTool(@event)
            || TrySelect(@event)
            || TryBrush(@event);
    }

    /// <summary>
    /// The transform handles get the plain left button, the same one the brush
    /// uses — which is safe because only one tool is ever active. Starting a drag
    /// yields to the camera on the brush's own terms; once one has started, the
    /// question no longer arises, because the camera is no longer being offered
    /// anything to act on.
    /// </summary>
    private bool TryGizmo(InputEvent @event)
    {
        if (_camera3D == null)
            return false;

        if (!_gizmo.IsDragging && (IsNavigating() || _cameraActivity.IsActive))
            return false;

        return _gizmo.HandleInput(@event, _camera3D);
    }

    /// <summary>
    /// A tool key, reported rather than acted on. Claiming the event matters for
    /// the same reason the camera claims its arrows: an unclaimed key on a focused
    /// control is the engine's to read as something else.
    /// </summary>
    private bool TryTool(InputEvent @event)
    {
        if (@event is not InputEventKey key)
            return false;

        if (!ToolActions.TryResolve(key, out ToolKind tool))
            return false;

        // An echo is the OS repeating a key that already switched the tool.
        if (key.Pressed && !key.Echo)
            ToolRequested?.Invoke(tool);

        return true;
    }

    private void OnKeyboardLost()
    {
        _camera?.ReleaseKeys();
        KeyboardLost?.Invoke();
    }

    /// <summary>
    /// A selecting tool's click: every visible element under the pointer, reported
    /// nearest first, with a click that crossed nothing reported as an empty list
    /// rather than swallowed — "you clicked past everything" is an answer, and the
    /// presenter has something to do with it.
    /// </summary>
    /// <remarks>
    /// The cursor is not the only tool that selects — see
    /// <see cref="SelectingTools"/> — and the transform tools reach this only
    /// after the gizmo has refused the same press, which is what makes "a click on
    /// a handle grabs it, a click anywhere else picks what is under it" one
    /// ordering rather than two rules.
    /// <para>
    /// Deliberately without the brush's <c>_previewWasOffered</c> gate, which is
    /// what makes the first click into an inactive viewport an activation and
    /// nothing else. That rule exists so a click cannot <em>edit</em> blind;
    /// selecting edits nothing and leaves no record, and a user who clicks a model
    /// while the inspector holds the keyboard means to select it. The camera keeps
    /// its refusal, though: it and a tool must never act at once.
    /// </para>
    /// </remarks>
    private bool TrySelect(InputEvent @event)
    {
        if (!SelectingTools.IsSelectingTool(_tool))
            return false;

        if (@event is not InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
                AltPressed: false
            } click)
        {
            return false;
        }

        if (IsNavigating() || _cameraActivity.IsActive)
            return false;

        // Read off this event rather than polled: by the time a presenter answers,
        // the modifier may well be up.
        ElementPicked?.Invoke(new ElementPick(
            CollectPickedElements(click.Position),
            click.Position,
            SelectionModifiers.Resolve(click)));

        return true;
    }

    /// <summary>
    /// Every element the ray crosses, nearest first — the cross-element pick the
    /// cell tools still do not have, and the reason this is a separate walk rather
    /// than <see cref="TryPick"/> widened: that one answers in the target's cells
    /// and needs a target, this one has none and answers in ids.
    /// </summary>
    /// <remarks>
    /// <b>Only what is on screen is offered</b>, which the engine answers for free:
    /// visibility composes down the node tree exactly as it composes down the
    /// document's, so an element under a hidden group is out without this having to
    /// walk anything. A degenerate transform is refused for the reason
    /// <see cref="TryPick"/> refuses one — the ray is expressed in the element's
    /// frame, and a basis with no inverse turns that into an infinity.
    /// <para>
    /// Distances are compared in <b>world</b> units. Each element's own ray is
    /// normalized in its own space, so a scaled element would otherwise report a
    /// distance in a different unit than its neighbour and sort against it wrongly.
    /// The tie-break on the id is not cosmetic either: the order has to be the same
    /// on the second click as on the first, or cycling would cycle at random.
    /// </para>
    /// </remarks>
    private IReadOnlyList<Guid> CollectPickedElements(Vector2 screenPosition)
    {
        if (_camera3D == null)
            return Array.Empty<Guid>();

        Vector3 worldOrigin = _camera3D.ProjectRayOrigin(screenPosition);
        Vector3 worldDirection = _camera3D.ProjectRayNormal(screenPosition);

        var hitsList = new List<(float Distance, Guid Id)>();

        foreach (KeyValuePair<Guid, IReadOnlyDictionary<Vector3I, Guid>> entry
            in _voxelsByElementDictionary)
        {
            if (!_nodesByElementDictionary.TryGetValue(entry.Key, out Node3D? node)
                || !node.IsVisibleInTree()
                || Mathf.IsZeroApprox(node.GlobalTransform.Basis.Determinant()))
            {
                continue;
            }

            Transform3D localFromWorld = node.GlobalTransform.AffineInverse();
            Vector3 origin = localFromWorld * worldOrigin;
            Vector3 direction = (localFromWorld.Basis * worldDirection).Normalized();

            if (!VoxelPicker.TryPickVoxels(origin, direction, entry.Value, out _, out _, out float local))
                continue;

            Vector3 worldPoint = node.GlobalTransform * (origin + direction * local);

            hitsList.Add((worldOrigin.DistanceTo(worldPoint), entry.Key));
        }

        hitsList.Sort(CompareHits);

        var idsList = new List<Guid>(hitsList.Count);

        foreach ((float _, Guid id) in hitsList)
            idsList.Add(id);

        return idsList;
    }

    private static int CompareHits((float Distance, Guid Id) left, (float Distance, Guid Id) right)
    {
        int byDistance = left.Distance.CompareTo(right.Distance);

        return byDistance != 0 ? byDistance : left.Id.CompareTo(right.Id);
    }

    /// <summary>
    /// A click may only place what the preview already offered. That one rule is
    /// also what makes the first click into an inactive viewport an activation
    /// and nothing else: there was no preview to act on, because the viewport was
    /// not the panel listening. The click still lands — it is what hands the
    /// viewport the keyboard — it just does not paint blind.
    /// </summary>
    private bool TryBrush(InputEvent @event)
    {
        if (!IsCellTool())
            return false;

        if (@event is not InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
                AltPressed: false
            } click || !_previewWasOffered || IsNavigating())
        {
            return false;
        }

        // Shift is the brush's erase modifier and the extruder's carve-inward one;
        // sampling and filling have no alternate mode.
        bool alternate =
            (_tool is ToolKind.Paint or ToolKind.Extrude) && click.ShiftPressed;

        if (!TryPick(click.Position, alternate, out ScenePick pick))
            return false;

        // The eyedropper reads a voxel's material, the extruder takes hold of a
        // voxel's face and the fill recolours a voxel's volume; the region's walls
        // have none of the three to offer.
        if (_tool is ToolKind.Eyedropper or ToolKind.Extrude or ToolKind.Fill
            && !pick.HitVoxel)
        {
            return false;
        }

        // Under the extruder the press opens a gesture rather than acting: the
        // layers are measured by the pull and reported once, at release. A click
        // is the degenerate gesture and keeps meaning one layer.
        if (_tool == ToolKind.Extrude)
            return TryBeginExtrudeDrag(pick, click.Position);

        // Under the brush the press is the first step of a stroke: the cell lands
        // at once, exactly as a click always has, and the button staying down is
        // what turns the click into a gesture.
        if (_tool == ToolKind.Paint)
            return TryBeginStroke(pick);

        Picked?.Invoke(pick);

        return true;
    }

    /// <summary>
    /// Opens the stroke from the press and lands its first cell. The gesture is
    /// announced before the cell, so that the record a layer up is open by the
    /// time the first edit reaches it. The marker goes away for the length of the
    /// stroke: the cells appearing under the pointer are the feedback, and a ghost
    /// drawn inside the cell just placed would fight it for depth.
    /// </summary>
    private bool TryBeginStroke(ScenePick pick)
    {
        if (_pickTarget is not { } targetId)
            return false;

        var stroke = new BrushStroke(pick.Alternate);
        Vector3I cell = pick.Alternate ? pick.Cell : pick.Cell + pick.Face;

        stroke.Record(CollectPreviewCells(targetId, cell, pick.Alternate));
        _brushStroke = stroke;
        _previewWasOffered = false;
        HideBrushPreview();

        StrokeBegan?.Invoke();
        Picked?.Invoke(pick);

        return true;
    }

    /// <summary>
    /// The stroke's own input loop: the release closes it, and everything else
    /// is swallowed for the length of the gesture. Motion is polled, not handled:
    /// the hover's proposal changes for reasons that are not motions, the same
    /// argument the preview itself makes. Escape is not here either — it reaches
    /// every scene gesture through <see cref="TryAnswerEscape"/>, because the
    /// shell answers the key before GUI routing and this loop never sees it.
    /// </summary>
    private void HandleStrokeInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            CompleteStroke();
    }

    /// <summary>
    /// Escape, offered to the gesture in flight before anything else the shell
    /// dismisses with the key. The shell answers Escape in <c>_Input</c>, ahead of
    /// GUI routing, so the key never reaches this node's own input loops while a
    /// hand is holding something in the scene — which is exactly when it is meant
    /// to; found by driving a stroke, which went on after Escape as if nothing had
    /// been pressed. A gesture owns the viewport while it is held, so it is the
    /// front-most thing there is and is asked first. Answers whether there was one
    /// to end. The stroke closes on what it landed (its cells are already the
    /// model's, and a batch cannot be discarded); the extrude pull and the handle
    /// drag are abandoned, which is what each of their own loops used to do with
    /// the key before the shell took it.
    /// </summary>
    public bool TryAnswerEscape()
    {
        if (_brushStroke != null)
        {
            CompleteStroke();

            return true;
        }

        if (_extrudeDrag != null)
        {
            CancelExtrudeDrag();

            return true;
        }

        if (_gizmo.IsDragging)
        {
            _gizmo.CancelDrag();

            return true;
        }

        return false;
    }

    /// <summary>
    /// One frame of a held stroke. The button is the authority on whether the
    /// stroke is still happening — the gizmo's own rule, for the gizmo's own
    /// reason: a release delivered to some other control would otherwise leave a
    /// stroke painting for good. Then the hover's proposal, read against the
    /// surface as it stood at the press, and written only when it is a cell the
    /// stroke has not written yet.
    /// </summary>
    private void UpdateStroke(bool viewportListening, bool cameraBusy)
    {
        if (_brushStroke is not { } stroke)
            return;

        // The camera and a tool never act at once. Nothing a hand does can drive
        // the camera under a held stroke — the stroke swallows every event — but
        // an agent's camera command can, and the pointer's ray then lands on a
        // cell nobody aimed at. So a camera moving under a stroke closes it,
        // which is "anything that starts a new action ends the one in flight"
        // read from the camera's side.
        if (!_input.IsButtonDown(MouseButton.Left) || cameraBusy)
        {
            CompleteStroke();

            return;
        }

        // The pointer over a panel, or off the scene, pauses the stroke rather
        // than ending it: the button is still down, and it comes back when the
        // pointer does.
        if (!viewportListening
            || !_input.TryGetPointer(out Vector2 pointer)
            || _pickTarget is not { } targetId
            || !TryProposeBrushCells(pointer, stroke.Alternate, targetId, out ScenePick pick)
            || !stroke.IsNew(ProposedCell(pick)))
        {
            return;
        }

        stroke.Record(CollectPreviewCells(targetId, ProposedCell(pick), stroke.Alternate));
        Picked?.Invoke(pick);
    }

    public void EndStroke() => CompleteStroke();

    /// <summary>
    /// Closes the stroke in flight, if any, and says so. Every way out of a
    /// stroke comes through here, and every one of them commits: the cells are
    /// already the model's.
    /// </summary>
    private void CompleteStroke()
    {
        if (_brushStroke == null)
            return;

        _brushStroke = null;
        StrokeEnded?.Invoke();
    }

    /// <summary>The cell a pick means to the brush: the one in front of the face, or the voxel itself when erasing.</summary>
    private static Vector3I ProposedCell(ScenePick pick) =>
        pick.Alternate ? pick.Cell : pick.Cell + pick.Face;

    /// <summary>
    /// Opens the extrude gesture from the press that grabbed a face. The axis it
    /// is measured against — the face's normal, through the face's centre — is
    /// captured in world space here and never re-derived, so nothing moves under
    /// the drag; the direction is deliberately unnormalized, one cell step long,
    /// which is what makes the projection come back in layers with a scaled
    /// element needing no correction anywhere.
    /// </summary>
    private bool TryBeginExtrudeDrag(ScenePick pick, Vector2 screenPosition)
    {
        if (_camera3D == null
            || _pickTarget is not { } targetId
            || !_nodesByElementDictionary.TryGetValue(targetId, out Node3D? target))
        {
            return false;
        }

        var normal = new Vector3(pick.Face.X, pick.Face.Y, pick.Face.Z);
        Vector3 faceCentre = new Vector3(pick.Cell.X, pick.Cell.Y, pick.Cell.Z)
            + Vector3.One * 0.5f
            + normal * 0.5f;

        _extrudeAxisOrigin = target.GlobalTransform * faceCentre;
        _extrudeAxisDirection = target.GlobalTransform.Basis * normal;

        // A ray parallel to the axis has no projection — a face clicked that
        // edge-on is barely clickable at all, so the press falls back to being
        // the click it would have been before the gesture existed.
        if (!GizmoMath.TryProjectOnAxis(
                _extrudeAxisOrigin,
                _extrudeAxisDirection,
                _camera3D.ProjectRayOrigin(screenPosition),
                _camera3D.ProjectRayNormal(screenPosition),
                out float baseOffset))
        {
            Picked?.Invoke(pick with
            {
                Layers = pick.Alternate ? -VoxelFacePatch.ClickLayers : VoxelFacePatch.ClickLayers
            });

            return true;
        }

        _extrudeDrag = new ExtrudeDragState(pick, baseOffset);

        return true;
    }

    /// <summary>
    /// The gesture's own input loop: motion pulls, release answers. Everything
    /// else is swallowed by the caller — a drag in flight owns the viewport. Escape
    /// walks away, and arrives through <see cref="TryAnswerEscape"/> rather than
    /// here: the shell answers the key before GUI routing.
    /// </summary>
    private void HandleExtrudeDragInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
                UpdateExtrudeDrag(motion.Position);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                CompleteExtrudeDrag();
                break;
        }
    }

    private void UpdateExtrudeDrag(Vector2 screenPosition)
    {
        if (_extrudeDrag == null || _camera3D == null)
            return;

        if (!GizmoMath.TryProjectOnAxis(
                _extrudeAxisOrigin,
                _extrudeAxisDirection,
                _camera3D.ProjectRayOrigin(screenPosition),
                _camera3D.ProjectRayNormal(screenPosition),
                out float offset))
        {
            return;
        }

        if (_extrudeDrag.Update(offset))
            RefreshExtrudeDragPreview();
    }

    /// <summary>
    /// Draws what the pull is pending — the layers a release right now would
    /// write, over the whole patch, in the markers the click already taught:
    /// solid cells outward, erase wireframes inward, nothing at zero. Rebuilt
    /// only when the count changes, which is what <see cref="ExtrudeDragState"/>'s
    /// answer is for.
    /// </summary>
    private void RefreshExtrudeDragPreview()
    {
        if (_extrudeDrag == null)
            return;

        int pending = _extrudeDrag.PendingLayers;

        _dragReadout.SetText(pending == 0 ? string.Empty : $"Extrude {pending:+0;-0}");

        if (pending == 0
            || _pickTarget is not { } targetId
            || !_nodesByElementDictionary.TryGetValue(targetId, out Node3D? target)
            || !_voxelsByElementDictionary.TryGetValue(
                targetId, out IReadOnlyDictionary<Vector3I, Guid>? voxels)
            || !VoxelFaces.TryGetFaceIndex(_extrudeDrag.Pick.Face, out int face)
            || !voxels.TryGetValue(_extrudeDrag.Pick.Cell, out Guid material))
        {
            HideBrushPreview();

            return;
        }

        SymmetryValues symmetry =
            _symmetryByElementDictionary.TryGetValue(targetId, out SymmetryValues stored)
                ? stored
                : default;

        IReadOnlyList<Vector3I> cells = VoxelFacePatch.CollectExtrusion(
            voxels,
            _extrudeDrag.Pick.Cell,
            face,
            pending,
            symmetry.X.Enabled ? symmetry.X.PlaneDoubled : null,
            symmetry.Y.Enabled ? symmetry.Y.PlaneDoubled : null,
            symmetry.Z.Enabled ? symmetry.Z.PlaneDoubled : null);

        if (pending > 0)
        {
            HideEraseWireframe();
            ShowPlacePreviews(target.GlobalTransform, cells, material);
        }
        else
        {
            HidePlacePreview();
            ShowEraseWireframes(target.GlobalTransform, cells);
        }
    }

    /// <summary>
    /// The release. What it resolves to is raised through the ordinary
    /// <see cref="Picked"/> — one event, carrying the measured layers — and a
    /// gesture that resolved to nothing raises nothing at all.
    /// </summary>
    private void CompleteExtrudeDrag()
    {
        if (_extrudeDrag == null)
            return;

        int layers = _extrudeDrag.ResolveLayers();
        ScenePick pick = _extrudeDrag.Pick with { Layers = layers };

        ClearExtrudeDrag();

        if (layers != 0)
            Picked?.Invoke(pick);
    }

    private void CancelExtrudeDrag() => ClearExtrudeDrag();

    private void ClearExtrudeDrag()
    {
        _extrudeDrag = null;
        _dragReadout.SetText(string.Empty);
        HideBrushPreview();
    }

    /// <summary>
    /// The preview has to track the current cell and mode the instant either
    /// changes — not just on the next mouse motion — because plenty of what
    /// changes them isn't a mouse motion at all: Shift going down or up under a
    /// still mouse, the camera turning (drag, wheel, arrow keys) with the cursor
    /// parked over the same pixel, a click that just edited the very cell being
    /// shown. Polling every frame instead of reacting to whichever input caused
    /// it is what makes all of those the same case rather than three.
    ///
    /// The mouse position is the window's, so the preview also has to know when
    /// the cursor is over a panel instead of over the scene — where a click would
    /// place nothing, and so nothing should be proposed. It asks about the
    /// keyboard for the same reason it asks about the pointer: a proposal made by
    /// a panel that is not the active one is a promise about a tool that is not
    /// listening.
    ///
    /// Whether a preview was actually offered is remembered, because that is what
    /// <see cref="TryBrush"/> requires before it will place anything. Input is
    /// processed before this runs, so on the frame a click activates the viewport
    /// the answer is still the one from before the click — which is exactly the
    /// answer that click should get.
    /// </summary>
    public override void _Process(double delta)
    {
        // Decayed every frame regardless of what else happens, so the window
        // still lapses on schedule when the pointer has wandered off the scene
        // or the viewport has lost the keyboard.
        _cameraActivity.Tick(delta);

        SyncViewCube();

        // The cube sits inside the surface's rect, so the pointer being on it is
        // the pointer being off the scene. Without this the brush would go on
        // proposing the cell behind the widget — a promise about a click that is
        // going to turn the camera instead.
        // Whether the viewport is the panel a tool would act in. Every affordance a
        // tool draws — the handles, the brush's cell, the erase and sample markers,
        // the mark on walls that cannot be painted against — hangs off this: with a
        // model selected from the hierarchy and the hierarchy still holding the
        // keyboard, what is drawn is its border and nothing else. Selecting a thing
        // is not the same as pointing at it, and only one of the two should put
        // pointers on the screen.
        bool viewportFocused = _surface.HasKeyboard;

        // The other party pulling the bounds switch. With the viewport inactive the
        // selection was made in the project tree, and the box is the only thing in
        // the scene saying which element that row stands for — so it is drawn
        // whatever the tool, and goes away again the moment the viewport is the
        // panel being worked in.
        if (_selectionBounds.Set(SelectionBoundsReason.ViewportInactive, !viewportFocused))
            RebuildSelectionBoxes();

        // The narrower question the brush asks, which is about the pointer rather
        // than the keyboard. The cube sits inside the surface's rect, so the pointer
        // being on it is the pointer being off the scene. Without this the brush
        // would go on proposing the cell behind the widget — a promise about a click
        // that is going to turn the camera instead.
        // A shell overlay over the strip is the same answer as the cube, asked
        // of whoever owns the chrome rather than hard-coded per widget.
        bool chromeClaims =
            _chromeClaims != null
            && _input.TryGetPointer(out Vector2 chromePoint)
            && _chromeClaims(chromePoint);

        bool viewportListening =
            viewportFocused && _surface.IsPointerInside && !_viewCube.IsPointerInside
            && !chromeClaims;

        bool cameraBusy = IsNavigating() || _cameraActivity.IsActive;

        // Before the brush's early return, because this is about the target rather
        // than the pointer: moving the mouse out of the viewport must not change
        // what the walls look like. Losing the keyboard does, though — the mark is
        // the brush saying it cannot place here, and with the brush not listening
        // there is nothing saying it. Derived every frame rather than cached: the
        // answer depends on every ancestor's transform, so any parent moving or a
        // reparent changes it, and a subscription would have to walk the subtree
        // to notice what these thirty floating-point operations notice for free.
        UpdateBoxPaintable(viewportFocused);

        // Same standing as the box's own backdrop rule: the camera decides which
        // walls are on screen, so the wall traces follow it here rather than
        // waiting for a data change that may never come.
        SyncWallLasers();

        // Before the brush's early return: the handles are placed and rescaled on
        // every frame whatever else is going on — a camera turning is exactly when
        // their size on screen has to be recomputed — and only what they highlight
        // yields while it is being driven.
        UpdateGizmo(viewportFocused, viewportListening && !cameraBusy);

        // An extrude drag owns the preview: what is pending is drawn from the
        // gesture's own state as it changes, and the hover proposal re-derived
        // here would clobber it with the cell under a pointer that is mid-pull.
        if (_extrudeDrag != null)
        {
            _previewWasOffered = false;

            return;
        }

        // A stroke in flight is the hover's proposal being written rather than
        // shown: the cells it lands are its own marker, and nothing else here is
        // asked until the button is up.
        if (_brushStroke != null)
        {
            _previewWasOffered = false;
            UpdateStroke(viewportListening, cameraBusy);

            return;
        }

        // A camera being driven suppresses the brush the same way a drag does:
        // no preview, and so — through _previewWasOffered — no paint. Whatever
        // the brush would have shown vanishes the instant a gesture starts and
        // returns only once the camera goes idle, in step with the marker's
        // fade. That is also what resolves Shift: while the camera is active it
        // owns a held Shift, and the erase preview comes back afterwards because
        // the brush reads the live key again once it is no longer suppressed.
        //
        // Another tool being active suppresses it in exactly the same way, and
        // that is the whole of the arbitration between them: a click aims at
        // cells only under the tools IsCellTool names, so outside those the
        // viewport proposes nothing and a click does nothing.
        if (!IsCellTool()
            || !viewportListening
            || cameraBusy
            || !_input.TryGetPointer(out Vector2 pointer))
        {
            _previewWasOffered = false;
            HideBrushPreview();
            return;
        }

        _previewWasOffered = _tool switch
        {
            ToolKind.Eyedropper => UpdateSamplePreview(pointer),
            ToolKind.Fill => UpdateFillPreview(pointer),
            ToolKind.Extrude => UpdateExtrudePreview(pointer, _input.IsKeyDown(Key.Shift)),
            _ => UpdateBrushPreview(pointer, _input.IsKeyDown(Key.Shift))
        };
    }

    /// <summary>The tools whose click aims at a cell — the brush family, not the gizmo's.</summary>
    private bool IsCellTool() =>
        _tool is ToolKind.Paint or ToolKind.Eyedropper or ToolKind.Extrude or ToolKind.Fill;

    /// <summary>
    /// Turns the cube to wherever the camera now is. Derived from the live pose
    /// every frame rather than pushed by whatever moved it, which is what makes a
    /// mouse drag, an arrow key, a wheel notch, an agent's <c>orbit</c>, a framing
    /// and a handover restore all one case instead of six — and the comparison is
    /// what keeps that from repainting a 3D widget on frames where nothing moved.
    /// </summary>
    private void SyncViewCube()
    {
        if (_camera == null)
            return;

        CameraPose pose = _camera.GetPose();

        if (pose.YawDegrees == _cubeYawDegrees && pose.PitchDegrees == _cubePitchDegrees)
            return;

        _cubeYawDegrees = pose.YawDegrees;
        _cubePitchDegrees = pose.PitchDegrees;

        _viewCube.SetOrientation(pose.YawDegrees, pose.PitchDegrees);
    }

    private void UpdateGizmo(bool allowHandles, bool allowHover)
    {
        if (_camera3D == null)
            return;

        // No pointer at all — an agent that has moved none — highlights nothing,
        // rather than lighting up whatever happens to sit at the origin.
        bool hasPointer = _input.TryGetPointer(out Vector2 pointer);

        _gizmo.Update(
            _camera3D,
            pointer,
            allowHandles,
            allowHover && hasPointer,
            _input.IsButtonDown(MouseButton.Left));
    }

    /// <summary>
    /// Asked of the camera rather than worked out from the raw input a second
    /// time. Re-deriving it here meant keeping a copy of the navigation keymap in
    /// the one class that must not own it, and a copy is a thing that drifts.
    /// </summary>
    private bool IsNavigating() => _camera?.IsNavigating == true;

    /// <summary>
    /// Says on the box itself whether its walls can be painted against, which they
    /// can only while the brush's target sits on the scene's own cell grid.
    /// </summary>
    /// <remarks>
    /// With another tool active, or nothing to paint, the question does not arise
    /// and the box is left alone: the mark means "this target cannot place here",
    /// not "nothing can".
    /// </remarks>
    private void UpdateBoxPaintable(bool viewportFocused)
    {
        if (_box == null)
            return;

        bool paintable = !viewportFocused
            || _tool != ToolKind.Paint
            || _pickTarget is not { } targetId
            || !_nodesByElementDictionary.TryGetValue(targetId, out Node3D? target)
            || GridAlignment.IsAligned(target.GlobalTransform);

        _box.SetPaintable(paintable);
    }

    /// <summary>
    /// Shows the cell a click would act on: the empty cell in front of the face,
    /// or the voxel itself when the modifier says clear. Returns whether it
    /// offered anything at all — the answer a click needs before it may act.
    /// </summary>
    private bool UpdateBrushPreview(Vector2 screenPosition, bool alternate)
    {
        // The other tools' markers, which the brush must never be seen wearing.
        // They go here rather than in SetTool, because the cell tools all aim at
        // cells and so no switch between them hides anything: with the pointer
        // parked on a voxel, the eyedropper's ring would still be round the cell
        // it read while the brush offered the one in front of it. A sample hands
        // the brush back the moment it succeeds, which turned that from a case
        // reached by pressing a key mid-hover into the ordinary one.
        HideSamplePreview();

        if (_pickTarget is not { } targetId
            || !_nodesByElementDictionary.TryGetValue(targetId, out Node3D? target)
            || !TryProposeBrushCells(screenPosition, alternate, targetId, out ScenePick pick))
        {
            HideBrushPreview();

            return false;
        }

        IReadOnlyList<Vector3I> cells =
            CollectPreviewCells(targetId, ProposedCell(pick), alternate);

        if (alternate)
        {
            HidePlacePreview();
            ShowEraseWireframes(target.GlobalTransform, cells);
        }
        else
        {
            HideEraseWireframe();
            ShowPlacePreviews(target.GlobalTransform, cells, _brushMaterialId);
        }

        return true;
    }

    /// <summary>
    /// The brush's proposal for a pointer, drawn or not: the pick under it, refused
    /// where the brush could not act — no target, or an erase aimed at the region's
    /// walls, which have nothing to clear. The preview draws what this answers and
    /// a held stroke writes it, so the two cannot come to propose different cells.
    /// </summary>
    private bool TryProposeBrushCells(
        Vector2 screenPosition,
        bool alternate,
        Guid targetId,
        out ScenePick pick)
    {
        if (!TryPick(screenPosition, alternate, out pick) || _pickTarget != targetId)
            return false;

        // Nothing to clear where the click would land.
        return !alternate || pick.HitVoxel;
    }

    /// <summary>
    /// The cells a click would act on: the picked cell's whole orbit under the
    /// element's mirror planes, filtered to what each marker can honestly show.
    /// A place ghost stands only in empty cells — inside an existing voxel it
    /// would fight the mesh for depth, and what a click does there is repaint,
    /// not appear — and an erase mark sits only on filled ones. The picked cell
    /// itself always qualifies: placing targets the empty cell in front of a
    /// face, erasing targets the voxel that was hit.
    /// </summary>
    private IReadOnlyList<Vector3I> CollectPreviewCells(
        Guid targetId,
        Vector3I cell,
        bool alternate)
    {
        SymmetryValues symmetry =
            _symmetryByElementDictionary.TryGetValue(targetId, out SymmetryValues stored)
                ? stored
                : default;

        IReadOnlyList<Vector3I> orbit = VoxelSymmetry.ExpandOrbit(
            cell,
            symmetry.X.Enabled ? symmetry.X.PlaneDoubled : null,
            symmetry.Y.Enabled ? symmetry.Y.PlaneDoubled : null,
            symmetry.Z.Enabled ? symmetry.Z.PlaneDoubled : null);

        if (orbit.Count == 1)
            return orbit;

        _voxelsByElementDictionary.TryGetValue(
            targetId, out IReadOnlyDictionary<Vector3I, Guid>? voxels);

        var cellsList = new List<Vector3I>(orbit.Count);

        foreach (Vector3I orbitCell in orbit)
        {
            bool filled = voxels != null && voxels.ContainsKey(orbitCell);

            if (alternate == filled)
                cellsList.Add(orbitCell);
        }

        return cellsList;
    }

    /// <summary>
    /// The cells a fill click would flood from:
    /// <see cref="VoxelFlood.CollectOrbitSeeds"/>'s answer over the element's own
    /// cells and mirror planes. The click and the <c>fill-connected</c> command
    /// ask the same function, so the marker cannot name a place they do not.
    /// </summary>
    private IReadOnlyList<Vector3I> CollectFillSeeds(Guid targetId, Vector3I cell)
    {
        if (!_voxelsByElementDictionary.TryGetValue(
                targetId, out IReadOnlyDictionary<Vector3I, Guid>? voxels))
        {
            return new[] { cell };
        }

        SymmetryValues symmetry =
            _symmetryByElementDictionary.TryGetValue(targetId, out SymmetryValues stored)
                ? stored
                : default;

        return VoxelFlood.CollectOrbitSeeds(
            voxels,
            cell,
            symmetry.X.Enabled ? symmetry.X.PlaneDoubled : null,
            symmetry.Y.Enabled ? symmetry.Y.PlaneDoubled : null,
            symmetry.Z.Enabled ? symmetry.Z.PlaneDoubled : null);
    }

    /// <summary>
    /// Shows the voxel the eyedropper would read. Only a voxel qualifies — the
    /// region's walls have no material to give — so over anything else nothing
    /// is offered, and through <c>_previewWasOffered</c> a click does nothing.
    /// The mirror orbit is deliberately absent: sampling reads one cell.
    /// </summary>
    private bool UpdateSamplePreview(Vector2 screenPosition)
    {
        if (!TryPick(screenPosition, alternate: false, out ScenePick pick)
            || !pick.HitVoxel
            || _pickTarget is not { } targetId
            || !_nodesByElementDictionary.TryGetValue(targetId, out Node3D? target))
        {
            HideBrushPreview();

            return false;
        }

        HidePlacePreview();
        HideEraseWireframe();
        ShowSamplePreviews(target.GlobalTransform, new[] { pick.Cell });

        return true;
    }

    /// <summary>
    /// Shows the cells a fill click would act on: the picked voxel and every image
    /// of it under the element's mirror planes that wears the same material, each
    /// of which seeds a volume of its own. The eyedropper's ring rather than the
    /// brush's solid ghost — a fill recolours a cell that is already there, and a
    /// ghost drawn inside it would fight the mesh for depth.
    /// </summary>
    /// <remarks>
    /// The seeds, not the volumes. Outlining every cell a fill would recolour is a
    /// marker per cell of a region that can be the whole model, rebuilt on every
    /// frame the pointer moves; what is marked instead is where the click acts,
    /// and the volume follows from it. What the marker must not do is promise
    /// <em>fewer</em> places than the click acts on, which is what showing the one
    /// cell did with a mirror plane on: the far side changed colour with nothing
    /// having shown it. Nor more of them, which is why the seeds are asked of the
    /// function the click asks rather than worked out here.
    /// </remarks>
    private bool UpdateFillPreview(Vector2 screenPosition)
    {
        if (!TryPick(screenPosition, alternate: false, out ScenePick pick)
            || !pick.HitVoxel
            || _pickTarget is not { } targetId
            || !_nodesByElementDictionary.TryGetValue(targetId, out Node3D? target))
        {
            HideBrushPreview();

            return false;
        }

        HidePlacePreview();
        HideEraseWireframe();

        // An image on empty space or on another material seeds nothing, so
        // marking it would promise a place the edit does not reach.
        ShowSamplePreviews(target.GlobalTransform, CollectFillSeeds(targetId, pick.Cell));

        return true;
    }

    /// <summary>
    /// Shows the layer an extrude click would write: the whole connected run of
    /// face the pointer is over, pushed one cell out — or, with the modifier held,
    /// carved one cell in. Same <see cref="VoxelFacePatch"/> answer the edit is
    /// built from, so the marker cannot promise a different set of cells than the
    /// click delivers.
    /// </summary>
    private bool UpdateExtrudePreview(Vector2 screenPosition, bool alternate)
    {
        // The other cell tools' markers; see UpdateBrushPreview for why they are
        // cleared here rather than on the tool change.
        HideSamplePreview();

        if (!TryPick(screenPosition, alternate, out ScenePick pick)
            || !pick.HitVoxel
            || _pickTarget is not { } targetId
            || !_nodesByElementDictionary.TryGetValue(targetId, out Node3D? target)
            || !_voxelsByElementDictionary.TryGetValue(
                targetId, out IReadOnlyDictionary<Vector3I, Guid>? voxels)
            || !VoxelFaces.TryGetFaceIndex(pick.Face, out int face))
        {
            HideBrushPreview();

            return false;
        }

        SymmetryValues symmetry =
            _symmetryByElementDictionary.TryGetValue(targetId, out SymmetryValues stored)
                ? stored
                : default;

        IReadOnlyList<Vector3I> cells = VoxelFacePatch.CollectExtrusion(
            voxels,
            pick.Cell,
            face,
            alternate ? -VoxelFacePatch.ClickLayers : VoxelFacePatch.ClickLayers,
            symmetry.X.Enabled ? symmetry.X.PlaneDoubled : null,
            symmetry.Y.Enabled ? symmetry.Y.PlaneDoubled : null,
            symmetry.Z.Enabled ? symmetry.Z.PlaneDoubled : null);

        if (cells.Count == 0)
        {
            HideBrushPreview();

            return false;
        }

        if (alternate)
        {
            HidePlacePreview();
            ShowEraseWireframes(target.GlobalTransform, cells);
        }
        else
        {
            HideEraseWireframe();

            // The seed cell's own material, not the brush's: an extrusion carries
            // the surface it grew out of.
            ShowPlacePreviews(target.GlobalTransform, cells, voxels[pick.Cell]);
        }

        return true;
    }

    private void HideBrushPreview()
    {
        HidePlacePreview();
        HideEraseWireframe();
        HideSamplePreview();
    }

    private void HideSamplePreview()
    {
        foreach (MeshInstance3D marker in _samplePreviewsList)
            marker.Visible = false;
    }

    private void HidePlacePreview()
    {
        if (_placePreviewHost is not null)
            _placePreviewHost.Visible = false;
    }

    private void HideEraseWireframe()
    {
        foreach (MeshInstance3D wireframe in _eraseWireframesList)
            wireframe.Visible = false;
    }

    /// <summary>
    /// The cells a click would fill, drawn as the voxels they would become: meshed
    /// by the scene's own mesher, in the element's own cell coordinates, under a
    /// node wearing the element's world transform.
    /// </summary>
    /// <remarks>
    /// The coordinates are the whole point, and getting them wrong is what shipped.
    /// A facture is mapped by local triplanar, which reads the <b>mesh vertex</b> —
    /// so a <c>BoxMesh</c> moved into place by its transform is forever at the
    /// pattern's origin, whatever cell it stands on, while the voxel a click leaves
    /// there is at that cell's own phase. The marker therefore showed one part of a
    /// plank and the click laid down another: photographed on PlankFloor, the ghost
    /// carried a joint across its top face and the voxel that replaced it was a
    /// clean board.
    /// <para>
    /// Building it with <see cref="VoxelMesher"/> rather than offsetting a box is
    /// what makes agreement structural instead of arithmetic kept in step: the
    /// marker is the same geometry, from the same function, wearing the same
    /// material.
    /// </para>
    /// <para>
    /// It <b>casts</b>, which no other marker does: it stands in empty space against
    /// the face it would attach to, and a box floating in space says nothing about
    /// where in space it is while its shadow does. The erase and sample markers are
    /// wireframes and the drag ghost is unshaded, so a shadow from either would be a
    /// line or a lie. A transparent brush material casts nothing here, exactly as
    /// its voxels do not.
    /// </para>
    /// <para>
    /// Its corners come out unoccluded, and by the occlusion rule itself rather than
    /// by the mesh having no colours to be shaded with: a cell with no filled
    /// neighbour earns full light at every corner. That is the honest answer — the
    /// cell is not in the model yet, and darkening it while its neighbours stayed
    /// bright would show half of a change that has not happened.
    /// </para>
    /// </remarks>
    private void ShowPlacePreviews(
        Transform3D targetTransform,
        IReadOnlyList<Vector3I> cells,
        Guid materialId)
    {
        if (_placePreviewHost is null)
        {
            _placePreviewHost = new Node3D { Name = "PlacePreview" };
            AddChild(_placePreviewHost);
        }

        var voxels = new Dictionary<Vector3I, Guid>(cells.Count);

        foreach (Vector3I cell in cells)
            voxels[cell] = materialId;

        // A shipped material has no material of its own to hand over: it is drawn
        // by the atlas's shader, and the marker is meshed the same way the model
        // is, which is the whole reason the two agree about what a cell will look
        // like once it lands.
        AtlasPlacement placement =
            _atlasByMaterialDictionary.TryGetValue(materialId, out AtlasPlacement found)
                ? found
                : AtlasPlacement.None;

        var materials = new Dictionary<Guid, Material>();
        var placements = new Dictionary<Guid, AtlasPlacement>();

        if (placement.HasLayer)
            placements[materialId] = placement;
        else
            materials[materialId] = GetPlacePreviewMaterial(materialId);

        // The marker wears the material's own opacity, so a ghost of a pane of
        // glass is a pane of glass.
        var opacities = new Dictionary<Guid, float>
        {
            [materialId] = _opacityByMaterialDictionary.TryGetValue(materialId, out float opacity)
                ? opacity
                : 1f
        };

        VoxelMeshInstances.Apply(
            _placePreviewHost,
            _placePreviewsList,
            VoxelMesher.Build(
                voxels,
                materials,
                opacities,
                only: null,
                placements,
                _atlas.Material),
            GeometryInstance3D.ShadowCastingSetting.On,
            RenderLayers.Voxels);

        _placePreviewHost.GlobalTransform = targetTransform;
        _placePreviewHost.Visible = true;
    }

    private void ShowEraseWireframes(Transform3D targetTransform, IReadOnlyList<Vector3I> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (_eraseWireframesList.Count <= i)
                _eraseWireframesList.Add(CreateEraseWireframe());

            MeshInstance3D wireframe = _eraseWireframesList[i];
            wireframe.GlobalTransform = BrushPreview.GetCellTransform(targetTransform, cells[i]);
            wireframe.Visible = true;

            RebuildEraseWireframe(wireframe);
        }

        for (int i = cells.Count; i < _eraseWireframesList.Count; i++)
            _eraseWireframesList[i].Visible = false;
    }

    /// <summary>
    /// Re-derives which of the cube's faces face the camera, in the cell's own
    /// (possibly rotated) frame, and rebuilds the marker's line mesh from them.
    /// </summary>
    private void RebuildEraseWireframe(MeshInstance3D wireframe)
    {
        if (_camera3D == null)
            return;

        Transform3D cellTransform = wireframe.GlobalTransform;
        Vector3 towardsCamera = _camera3D.GlobalPosition - cellTransform.Origin;
        Vector3 localViewDirection = cellTransform.Basis.Inverse() * towardsCamera;

        wireframe.Mesh = EraseWireframe.Build(localViewDirection);
    }

    /// <summary>
    /// Rings the cells a click would act on: one for the eyedropper, the whole
    /// mirror orbit for a fill. Grown as the orbit needs it and hidden rather than
    /// freed, the place marker's own arrangement.
    /// </summary>
    private void ShowSamplePreviews(
        Transform3D targetTransform,
        IReadOnlyList<Vector3I> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (_samplePreviewsList.Count <= i)
                _samplePreviewsList.Add(CreateSamplePreview());

            MeshInstance3D marker = _samplePreviewsList[i];

            marker.GlobalTransform = BrushPreview.GetCellTransform(targetTransform, cells[i]);
            marker.Visible = true;

            RebuildEraseWireframe(marker);
        }

        for (int i = cells.Count; i < _samplePreviewsList.Count; i++)
            _samplePreviewsList[i].Visible = false;
    }

    /// <summary>
    /// The erase marker's twin in the accent instead of the warning red: the
    /// same camera-facing cell wireframe, ringing a voxel about to be read
    /// rather than removed.
    /// </summary>
    private MeshInstance3D CreateSamplePreview()
    {
        var wireframe = new MeshInstance3D
        {
            Name = "SamplePreview",
            MaterialOverride = _samplePreviewMaterial ??= CreateSamplePreviewMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = RenderLayers.Voxels
        };

        AddChild(wireframe);

        return wireframe;
    }

    private MeshInstance3D CreateEraseWireframe()
    {
        var wireframe = new MeshInstance3D
        {
            Name = "EraseWireframe",
            MaterialOverride = _eraseWireframeMaterial ??= CreateEraseWireframeMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = RenderLayers.Voxels
        };

        AddChild(wireframe);

        return wireframe;
    }

    /// <summary>
    /// The very material a click would leave — the same instance the meshes use,
    /// not a see-through copy of its colour.
    /// </summary>
    /// <remarks>
    /// The preview's job is to say exactly what will appear, so a translucent
    /// stand-in was answering a slightly different question, and it lied outright
    /// about a material that is itself transparent. What keeps it from reading as
    /// an already-placed voxel is that it is a cell the model does not have: it
    /// stands in empty space, against the face it would attach to. Nothing is
    /// merged into the model's mesh — the marker is its own node and disappears
    /// with the pointer.
    /// </remarks>
    private Material GetPlacePreviewMaterial(Guid materialId)
    {
        if (_materialsByIdDictionary.TryGetValue(materialId, out Material? material))
            return material;

        // Only until a brush material has arrived; the meshes have the same gap.
        return _placePreviewMaterial ??= CreateMaterial(Colors.White);
    }

    /// <summary>
    /// Unshaded, and pulled out of both depth test and depth write: the marker
    /// must always read on top of the voxel it is about to clear, not fight it
    /// for the same depth as a coincident solid box used to. Alpha-sorted with a
    /// high render priority for the same reason — pure "never lose to the opaque
    /// pass" rather than a fudge tuned to today's scene.
    /// </summary>
    private static StandardMaterial3D CreateEraseWireframeMaterial() => new()
    {
        AlbedoColor = new Color(1f, 0.15f, 0.15f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = true,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        RenderPriority = 127
    };

    /// <summary>Same terms as the erase marker's; only the colour differs.</summary>
    private static StandardMaterial3D CreateSamplePreviewMaterial() => new()
    {
        AlbedoColor = new Color(0.44f, 0.73f, 0.98f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = true,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        RenderPriority = 127
    };

    private bool TryPick(Vector2 screenPosition, bool alternate, out ScenePick pick)
    {
        pick = default;

        if (_camera3D == null
            || _pickTarget is not { } targetId
            || !_nodesByElementDictionary.TryGetValue(targetId, out Node3D? target))
        {
            return false;
        }

        // A degenerate transform — a zero on any scale axis — has no inverse, and
        // everything below is expressed in the element's frame. Nothing can be
        // aimed at an element with no volume anyway; the point of refusing here is
        // that the alternative is an infinity turning into a cell coordinate.
        if (Mathf.IsZeroApprox(target.GlobalTransform.Basis.Determinant()))
            return false;

        // The region is a world-space room, the voxels are the element's own
        // cells, so each is tested in its own space and both results are then
        // compared in the element's — where the answer has to end up anyway.
        Transform3D localFromWorld = target.GlobalTransform.AffineInverse();
        Vector3 worldOrigin = _camera3D.ProjectRayOrigin(screenPosition);
        Vector3 worldDirection = _camera3D.ProjectRayNormal(screenPosition);
        Vector3 origin = localFromWorld * worldOrigin;
        Vector3 direction = (localFromWorld.Basis * worldDirection).Normalized();

        if (!_voxelsByElementDictionary.TryGetValue(
                targetId,
                out IReadOnlyDictionary<Vector3I, Guid>? voxels))
        {
            voxels = new Dictionary<Vector3I, Guid>();
        }

        var bestDistance = float.MaxValue;
        var found = false;
        Vector3I cell = default;
        Vector3I face = default;
        var hitVoxel = false;

        if (VoxelPicker.TryPickRegion(
                worldOrigin,
                worldDirection,
                _size,
                out Vector3 worldPoint,
                out Vector3 worldInward,
                out _))
        {
            Vector3 localPoint = localFromWorld * worldPoint;

            // A wall is always in the way — nothing may be reached through it —
            // but it only offers a cell to an element whose own grid coincides
            // with the scene's. Off the grid, flooring the wall point into the
            // element's cells answers with one that is neither flush with the wall
            // nor predictable, and that half-crosses it. So the wall keeps the
            // distance and gives up the cell: a click there does nothing.
            bestDistance = origin.DistanceTo(localPoint);

            if (GridAlignment.IsAligned(target.GlobalTransform))
            {
                Vector3I localFace = VoxelPicker.ToAxis(localFromWorld.Basis * worldInward);

                (cell, face) = VoxelPicker.ToRegionPick(localPoint, localFace);
                found = true;
            }
        }

        // Every other visible element is a surface on the same terms as a wall:
        // nothing may be reached through it, and it offers a cell only where its
        // own lattice and the target's coincide. Without it the ray ignored them
        // outright, so a click on a model standing in front of the one being
        // edited placed a cell in the empty space beyond it — and the preview said
        // so, which is how it was found.
        //
        // What it deliberately does not do is change which element is being
        // edited. A cell tool's click already means place, erase, sample, pull or
        // fill; having it also re-target is the complaint auto-select has earned
        // everywhere it exists, and the canon settles it for the selecting tools
        // in the same words.
        if (TryPickOtherElements(
                targetId,
                worldOrigin,
                worldDirection,
                out Vector3 otherPoint,
                out Vector3 otherOutward,
                out bool otherAligned))
        {
            Vector3 localPoint = localFromWorld * otherPoint;
            float otherDistance = origin.DistanceTo(localPoint);

            if (otherDistance < bestDistance)
            {
                bestDistance = otherDistance;

                // A nearer surface hides whatever the wall was offering, whether or
                // not it has a cell of its own to offer instead.
                found = false;

                if (otherAligned && GridAlignment.IsAligned(target.GlobalTransform))
                {
                    // The face the new cell goes against, which for a wall is its
                    // inward normal and here is the surface's outward one — the
                    // same sentence said from either side.
                    Vector3I localFace = VoxelPicker.ToAxis(localFromWorld.Basis * otherOutward);

                    (cell, face) = VoxelPicker.ToRegionPick(localPoint, localFace);
                    found = true;
                }
            }
        }

        // A stroke in flight reads the surface as it stood at its press: what it
        // has placed is read through, what it has erased is still there. Without
        // this the stroke's own track becomes the surface under the next frame's
        // ray, which piles cells towards the camera or digs through the model.
        IReadOnlySet<Vector3I>? hidden = _brushStroke is { Alternate: false } placing
            ? placing.Written
            : null;
        IReadOnlyCollection<Vector3I>? restored = _brushStroke is { Alternate: true } erasing
            ? erasing.Written
            : null;

        if (VoxelPicker.TryPickVoxels(
                origin,
                direction,
                voxels,
                hidden,
                restored,
                out Vector3I voxelCell,
                out Vector3I voxelFace,
                out float voxelDistance)
            && voxelDistance < bestDistance)
        {
            cell = voxelCell;
            face = voxelFace;
            hitVoxel = true;
            found = true;
        }

        if (!found)
            return false;

        pick = new ScenePick(cell, face, hitVoxel, alternate);

        return true;
    }

    /// <summary>
    /// The nearest visible element other than the one being edited, as the world
    /// point the ray met it at and the face it met — plus whether that element sits
    /// on the world lattice, since a surface off the grid can occlude without being
    /// able to say which cell of the target stands against it.
    /// </summary>
    private bool TryPickOtherElements(
        Guid targetId,
        Vector3 worldOrigin,
        Vector3 worldDirection,
        out Vector3 point,
        out Vector3 outward,
        out bool aligned)
    {
        point = Vector3.Zero;
        outward = Vector3.Zero;
        aligned = false;

        var nearest = float.MaxValue;

        foreach (KeyValuePair<Guid, IReadOnlyDictionary<Vector3I, Guid>> entry
            in _voxelsByElementDictionary)
        {
            if (entry.Key == targetId
                || !_nodesByElementDictionary.TryGetValue(entry.Key, out Node3D? node)
                || !node.IsVisibleInTree()
                || Mathf.IsZeroApprox(node.GlobalTransform.Basis.Determinant()))
            {
                continue;
            }

            Transform3D localFromWorld = node.GlobalTransform.AffineInverse();
            Vector3 localOrigin = localFromWorld * worldOrigin;
            Vector3 localDirection = (localFromWorld.Basis * worldDirection).Normalized();

            if (!VoxelPicker.TryPickVoxels(
                    localOrigin,
                    localDirection,
                    entry.Value,
                    out _,
                    out Vector3I localFace,
                    out float local))
            {
                continue;
            }

            Vector3 worldPoint = node.GlobalTransform * (localOrigin + localDirection * local);
            float distance = worldOrigin.DistanceTo(worldPoint);

            if (distance >= nearest)
                continue;

            nearest = distance;
            point = worldPoint;
            outward = (node.GlobalTransform.Basis * (Vector3)localFace).Normalized();
            aligned = GridAlignment.IsAligned(node.GlobalTransform);
        }

        return nearest < float.MaxValue;
    }

    /// <summary>A material's colour or appearance changes every mesh that references it.</summary>
    private void RebuildAllMeshes()
    {
        foreach (KeyValuePair<Guid, VoxelChunkMeshes> entry in _meshesByElementDictionary)
        {
            if (!_voxelsByElementDictionary.TryGetValue(
                entry.Key, out IReadOnlyDictionary<Vector3I, Guid>? voxels))
                continue;

            // No region: a material has changed, which can move any face of the
            // model, so every chunk is rebuilt.
            entry.Value.Apply(
                voxels,
                _materialsByIdDictionary,
                _opacityByMaterialDictionary,
                region: null,
                _atlasByMaterialDictionary,
                _atlas.Material);
        }
    }

    private void Register(Guid id, Node3D node)
    {
        _nodesByElementDictionary[id] = node;
        _elementsByNodeDictionary[node] = id;
    }

    /// <summary>
    /// Godot frees a node's children with it, so the lookups have to be cleaned
    /// up for the whole subtree before the node goes.
    /// </summary>
    private void Forget(Node3D node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Node3D nested)
                Forget(nested);
        }

        if (!_elementsByNodeDictionary.Remove(node, out Guid id))
            return;

        // Any element going takes the ghost with it, not only the one it was built
        // for: the ghost hangs under the dragged element's *parent*, so this node
        // could be the thing that frees it out from under this reference. A drag
        // surviving a deletion pushes a new preview on the next frame anyway.
        DetachDragGhost();

        // An element wearing a border is going: take the border out first, or this
        // is left pointing at something Godot has freed. Asked of every element
        // rather than only of the active one, which is what a selection of several
        // costs — the box that has to come down is not necessarily the one the
        // handles are on.
        DetachSelectionBox(id);
        _highlightedElementsList.Remove(id);

        // The lasers hang under the active element's own node and the handles sit
        // on it, so both go with it and with nothing else.
        if (id == _highlightTarget)
        {
            DetachLasers();
            DetachWallLasers();
            _gizmo.SetTarget(null, Vector3.Zero);
            _highlightTarget = null;
        }

        _nodesByElementDictionary.Remove(id);
        _meshesByElementDictionary.Remove(id);
        _voxelsByElementDictionary.Remove(id);
        _symmetryByElementDictionary.Remove(id);

        // The nodes themselves die with the element being freed; what must go here
        // is the bookkeeping, and every table keyed by this id rather than the one
        // holding the geometry. Left standing, a table naming a freed node is a
        // crash waiting for whatever walks it next — the whole-scene reference
        // switch walks every one of them, and the aspect push walks every camera.
        // This is <see cref="Clear"/>'s own rule for one element rather than for a
        // document.
        _camerasByElementDictionary.Remove(id);
        _cameraGizmosByElementDictionary.Remove(id);
        _referenceImagesByElementDictionary.Remove(id);
        _referenceModelsByElementDictionary.Remove(id);
        _referenceNodesByElementDictionary.Remove(id);
    }

    /// <summary>
    /// One selected element's border, and the scale its corners were baked at — so
    /// a transform change can tell whether the mesh has to be built again or only
    /// carried.
    /// </summary>
    private sealed class SelectionBorder
    {
        public SelectionBorder(MeshInstance3D node, Vector3 scale)
        {
            Node = node;
            Scale = scale;
        }

        public MeshInstance3D Node { get; }

        public Vector3 Scale { get; }
    }
}
