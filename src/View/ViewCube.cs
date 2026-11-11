using System;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The navigation cube: a small shaded cube in a corner of the viewport, turned to
/// match the camera, whose 26 zones each turn the camera to a standard view when
/// pressed.
/// </summary>
/// <remarks>
/// A <see cref="SubViewport"/> with a world of its own, rather than a projection
/// drawn by hand. Three things come out of that and all three matter: the widget
/// gets real perspective, the hit test becomes a real ray through its own camera
/// instead of arithmetic that has to agree with what was drawn, and nothing in the
/// scene's world — its lights, its environment, its layers — can reach the cube or
/// be reached by it.
/// <para>
/// The cube itself never turns. The widget's camera is placed by exactly the same
/// function that places the scene's (<see cref="CameraFraming.GetOrbitOffset"/>),
/// which is what makes the two agree by construction — and it leaves the cube
/// axis-aligned at the origin, so a ray arrives at
/// <see cref="ViewCubeGeometry"/> already in the space it measures.
/// </para>
/// <para>
/// It decides nothing. A press is reported as the zone it landed on; what that
/// means for the camera is the scene view's answer, exactly as the viewport
/// surface reports a click rather than interpreting one.
/// </para>
/// </remarks>
internal sealed partial class ViewCube : Control
{
    /// <summary>The widget's side, in canvas units. Large enough to aim at, small enough to ignore.</summary>
    public const float WidgetSize = 96f;

    /// <summary>
    /// How far the widget's camera stands from the cube, with the field of view
    /// below. Together they frame the cube's circumscribed sphere with a little room
    /// to spare, so no corner of it is ever clipped however the view is turned.
    /// </summary>
    private const float CameraDistance = 3.2f;

    private const float CameraFov = 35f;

    /// <summary>
    /// The most the widget will render above its canvas size. A safety rail on a
    /// number read from the window rather than a target: it caps the render target
    /// a wild stretch transform could otherwise ask for.
    /// </summary>
    private const float MaximumResolutionScale = 4f;

    /// <summary>
    /// How dark a grid line is against the face it divides, and how far towards that
    /// the line is actually mixed. Both are <see cref="SceneBox"/>'s, so the widget's
    /// grid and the scene's are the same grid: a line ends up at 74% of the surface's
    /// own colour rather than at anything approaching black.
    /// </summary>
    private const float GridDarken = 0.48f;

    private const float GridStrength = 0.5f;

    /// <summary>
    /// The letters' colour. Ink rather than the axis's own: the face is already
    /// that colour, and a red letter on a red face is a watermark.
    /// </summary>
    /// <remarks>
    /// A soft grey, not the near-black it started as. The faces are pale, so
    /// near-black on them is the highest contrast anything in this editor has —
    /// on a widget whose job is to be glanced at and otherwise ignored, that
    /// reads as printing rather than as labelling. Opaque rather than a faded
    /// alpha, because the letters are cut rather than blended: an alpha under
    /// the scissor threshold does not fade a glyph, it deletes it.
    /// </remarks>
    private static readonly Color LabelInk = new(0.42f, 0.44f, 0.48f);

    /// <summary>How far a letter stands off its face, so it is not inside it.</summary>
    private const float LabelStandoff = ViewCubeGeometry.HalfExtent + 0.002f;

    /// <summary>
    /// World units per glyph pixel, with the font size below: together they put a
    /// letter at about a third of a face, which is what a cube of this size can
    /// carry without the negatives' two glyphs meeting the edges.
    /// </summary>
    private const float LabelPixelSize = 0.0042f;

    private const int LabelFontSize = 64;

    private readonly IInputState _input;
    private readonly SubViewport _viewport = new();
    private readonly TextureRect _surface = new();
    private readonly Camera3D _camera = new();
    private readonly MeshInstance3D _cells = new();

    private Vector3I? _hovered;

    public ViewCube(IInputState input)
    {
        _input = input;

        Name = "ViewCube";
        CustomMinimumSize = new Vector2(WidgetSize, WidgetSize);
        Size = new Vector2(WidgetSize, WidgetSize);

        // It claims the mouse, which is the whole of how a press on the cube is
        // kept from also being a press on the scene: the GUI hit test hands the
        // event here and the viewport surface below never sees it. A coordinate
        // check inside the brush would be the same rule written twice.
        MouseFilter = MouseFilterEnum.Stop;

        // A mouse target, not a keyboard stop: pressing a face is not a request to
        // take the keyboard away from the viewport, and an arrow no control claims
        // is read by the engine as "move focus".
        FocusMode = FocusModeEnum.None;

        BuildViewport();
    }

    /// <summary>A zone was pressed. Which way that is to look is decided a layer up.</summary>
    public event Action<Vector3I>? ZonePressed;

    /// <summary>
    /// Whether the driving party's pointer is over the widget rather than over the
    /// scene behind it. Asked of <see cref="IInputState"/> and answered against its
    /// own rect, exactly as <see cref="ViewportSurface.IsPointerInside"/> is, and
    /// for the same reason: the engine's own hover follows the machine's cursor
    /// whoever happens to be driving.
    /// </summary>
    public bool IsPointerInside =>
        IsVisibleInTree()
        && _input.TryGetPointer(out Vector2 position)
        && GetGlobalRect().HasPoint(position);

    /// <summary>
    /// Turns the widget to match the camera. Called only on frames the pose
    /// actually changed — the repaint below is a render of a whole 3D world, and
    /// there is nothing to redraw when nothing has moved.
    /// </summary>
    public void SetOrientation(float yawDegrees, float pitchDegrees)
    {
        Vector3 offset = CameraFraming.GetOrbitOffset(
            Mathf.DegToRad(yawDegrees),
            Mathf.DegToRad(pitchDegrees));

        _camera.Position = offset * CameraDistance;
        _camera.LookAt(Vector3.Zero, Vector3.Up);

        RequestRedraw();
    }

    public override void _Ready()
    {
        // The window's stretch is what decides how many real pixels a canvas unit
        // is worth, so a resize is the one event that changes how much detail the
        // widget should be rendering.
        GetWindow().SizeChanged += UpdateResolution;

        UpdateResolution();
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(GetWindow()))
            GetWindow().SizeChanged -= UpdateResolution;
    }

    /// <summary>
    /// Renders the widget at the pixels it actually occupies rather than at the
    /// canvas units it measures. Under a <c>canvas_items</c> stretch the two are
    /// not the same — measured at 2.58 on this machine — and every other control
    /// hides the difference by redrawing itself at the final resolution. A
    /// sub-viewport cannot: it is a texture of a fixed size, so left at its canvas
    /// size it is a small picture blown up, jagged beside chrome that is not.
    /// </summary>
    /// <remarks>
    /// Read live on every resize, never cached, for the reason
    /// <see cref="UiDriver"/> gives about the same transform: it changes with the
    /// window's size, and a stale one here is a blurred widget the way a stale one
    /// there is a click in the wrong place.
    /// </remarks>
    private void UpdateResolution()
    {
        Vector2 scale = GetWindow().GetFinalTransform().Scale;
        float factor = Mathf.Clamp(Mathf.Max(scale.X, scale.Y), 1f, MaximumResolutionScale);

        var pixels = new Vector2I(
            Mathf.Max(1, Mathf.RoundToInt(Size.X * factor)),
            Mathf.Max(1, Mathf.RoundToInt(Size.Y * factor)));

        if (_viewport.Size == pixels)
            return;

        _viewport.Size = pixels;

        RequestRedraw();
    }

    /// <summary>
    /// Which zone is lit, asked once a frame rather than answered from the last
    /// motion event.
    /// </summary>
    /// <remarks>
    /// Two things move here, and only one of them used to count. A motion event
    /// says the pointer moved; it says nothing when the <em>cube</em> turns under
    /// a pointer that is standing still — after a click on a face, or while the
    /// camera is orbited — and the highlight then sat on whichever zone had been
    /// there before, naming an axis that was no longer under the hand. Polling
    /// asks the question the widget is actually being asked: what is under the
    /// pointer <em>now</em>.
    /// <para>
    /// It also retires the clearing that hung off <c>NotificationMouseExit</c>,
    /// which the engine recomputes from the machine's cursor — so a bystander's
    /// mouse could put out a driving agent's highlight. The pointer this asks
    /// for is the driving party's, like every other affordance in the editor.
    /// </para>
    /// </remarks>
    public override void _Process(double delta) =>
        SetHovered(
            IsPointerInside && _input.TryGetPointer(out Vector2 pointer)
                ? Pick(pointer - GlobalPosition)
                : null);

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            // Claimed but not read: where the pointer is is polled above, and a
            // motion that fell through would reach the scene behind the widget.
            case InputEventMouseMotion:
                AcceptEvent();

                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } button:
            {
                if (Pick(button.Position) is { } zone)
                {
                    SetHovered(zone);
                    ZonePressed?.Invoke(zone);
                }

                // Claimed whether or not it landed on a zone: a press inside the
                // widget's rect but outside the cube's silhouette is still a press
                // on the widget, and letting it fall through would paint a voxel
                // behind it.
                AcceptEvent();

                break;
            }
        }
    }

    /// <summary>
    /// Which zone a point inside the widget is over, or null for the space around
    /// the cube. The point arrives in the control's canvas units and the camera
    /// projects in the sub-viewport's pixels, which are deliberately not the same
    /// number — see <see cref="UpdateResolution"/> — so it is scaled across rather
    /// than assumed to be one to one.
    /// </summary>
    private Vector3I? Pick(Vector2 position)
    {
        if (_viewport.Size.X <= 0 || _viewport.Size.Y <= 0 || Size.X <= 0f || Size.Y <= 0f)
            return null;

        var inViewport = new Vector2(
            position.X * _viewport.Size.X / Size.X,
            position.Y * _viewport.Size.Y / Size.Y);

        return ViewCubeGeometry.TryPick(
            _camera.ProjectRayOrigin(inViewport),
            _camera.ProjectRayNormal(inViewport),
            out Vector3I zone)
            ? zone
            : null;
    }

    private void SetHovered(Vector3I? zone)
    {
        if (_hovered == zone)
            return;

        _hovered = zone;

        _cells.Mesh = ViewCubeMesh.BuildCells(_hovered);

        RequestRedraw();
    }

    /// <summary>
    /// One more frame of the widget's own world, then nothing until something
    /// changes. A sub-viewport left updating every frame would be a second 3D
    /// render running behind a picture that is usually identical to the last one.
    /// </summary>
    private void RequestRedraw() =>
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;

    private void BuildViewport()
    {
        _viewport.Name = "CubeViewport";
        _viewport.OwnWorld3D = true;
        _viewport.TransparentBg = true;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;

        // A widget this small lives or dies on its silhouette, and the cube is
        // nothing but straight edges at an angle.
        _viewport.Msaa3D = Viewport.Msaa.Msaa4X;

        // Started at the canvas size so there is a valid target before the first
        // layout; UpdateResolution replaces it with the real pixel count.
        _viewport.Size = new Vector2I((int)WidgetSize, (int)WidgetSize);

        _camera.Name = "CubeCamera";
        _camera.Current = true;
        _camera.Fov = CameraFov;
        _camera.Near = 0.05f;
        _camera.Far = 100f;
        _camera.Position = new Vector3(0f, 0f, CameraDistance);

        _cells.Name = "Cells";
        _cells.Mesh = ViewCubeMesh.BuildCells(null);
        _cells.MaterialOverride = CreateCellMaterial();
        _cells.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        _viewport.AddChild(_camera);
        _viewport.AddChild(_cells);

        BuildFaceLabels();

        // Shown through a texture rather than a SubViewportContainer, which would
        // insist the sub-viewport be exactly the control's canvas size — the one
        // thing it must not be. See UpdateResolution.
        _surface.Name = "CubeSurface";
        _surface.Texture = _viewport.GetTexture();
        _surface.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _surface.StretchMode = TextureRect.StretchModeEnum.Scale;
        _surface.SetAnchorsPreset(LayoutPreset.FullRect);

        // The mouse is this control's; the picture over it must not take it.
        _surface.MouseFilter = MouseFilterEnum.Ignore;

        AddChild(_viewport);
        AddChild(_surface);
    }

    /// <summary>
    /// Unshaded, taking its colour from the vertices, with the 3×3 grid drawn on
    /// top of it. The faces are identifiers, so they must read the same at every
    /// angle — see <see cref="ViewCubeMesh"/> for why that is not the lighting model
    /// being stood in for.
    /// </summary>
    /// <remarks>
    /// The grid is <see cref="SceneBox"/>'s, deliberately: the same
    /// <c>albedo × GridDarken</c> mixed at the same <c>GridStrength</c>, and the same
    /// <c>fwidth</c> measurement so a line is a hairline on screen however close the
    /// cube is. Two consequences are the point of copying it rather than inventing a
    /// look. A line is a darkened shade of the face it divides, so it never reads as
    /// a black seam cut into the widget — and it is one screen pixel and a quarter,
    /// like the scene's, rather than a fraction of a small object that grows with it.
    /// <para>
    /// It draws on the boundaries between the cells <em>and</em> on the cube's own
    /// edges, because those are boundaries too and the scene box treats the walls of
    /// its region exactly the same way. Half of that outermost line falls outside the
    /// geometry, so the silhouette gains a slight edge rather than an outline.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The axis letter on each of the six faces: X, Y, Z on the way out and
    /// -X, -Y, -Z on the way back.
    /// </summary>
    /// <remarks>
    /// The widget shipped with no writing on it at all, and which way round it
    /// was had to be read off the face tints alone. The tints stay and still say
    /// which end of an axis a face is — the paler of a pair is the positive one —
    /// but a letter says which axis without anybody having to have learnt the
    /// palette first, and it is the same letter the transform rows and the
    /// handles use.
    /// <para>
    /// The ledger costed this as "a font atlas per face" and refused it. That was
    /// about six <em>words</em>; a glyph or two is a <see cref="Label3D"/>, which
    /// the engine rasterises once at the size it is asked for. Cut rather than
    /// blended, so the letters write depth like the cube does and nothing has to
    /// be sorted against anything.
    /// </para>
    /// </remarks>
    private void BuildFaceLabels()
    {
        var font = ResourceLoader.Load<Font>(EditorThemeFactory.FontPath);

        for (var axis = 0; axis < 3; axis++)
        foreach (int sign in new[] { 1, -1 })
        {
            Vector3 normal = Vector3.Zero;
            normal[axis] = sign;

            var label = new Label3D
            {
                Name = $"Label{axis}{(sign > 0 ? "Positive" : "Negative")}",
                Text = ViewCubeLabels.TextFor(axis, sign),
                Modulate = LabelInk,
                PixelSize = LabelPixelSize,
                FontSize = LabelFontSize,

                // Discarded rather than blended: a cut-out label writes depth, so
                // the cube hides the three faces pointing away without anything
                // having to be sorted against anything.
                AlphaCut = Label3D.AlphaCutMode.Discard,

                // It is chrome on chrome — the faces answer to no light either.
                Shaded = false,
                DoubleSided = false,
                Billboard = BaseMaterial3D.BillboardModeEnum.Disabled
            };

            if (font is not null)
                label.Font = font;

            label.Transform = new Transform3D(
                ViewCubeLabels.FaceBasis(normal), normal * LabelStandoff);

            _viewport.AddChild(label);
        }
    }

    private static ShaderMaterial CreateCellMaterial()
    {
        // The numbers are set as parameters rather than written into the source.
        // Interpolating them is how this first shipped and it did not compile: a
        // float formatted under a comma-decimal culture becomes "0,3333", which is
        // two tokens to a shader compiler. Nothing here has to be a literal.
        var shader = new Shader
        {
            Code =
                """
                shader_type spatial;
                // Chrome, not scene geometry: the faces name axes, so they answer to
                // no light. The grid below is SceneBox's, so the widget's grid and
                // the scene's are one look rather than two.
                render_mode unshaded;

                uniform float cell_size = 0.3333333;
                uniform float grid_strength = 0.5;
                uniform float grid_darken = 0.48;

                void fragment() {
                    // The widget's cube never moves, so its world space is its own.
                    vec3 local = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
                    vec3 nAbs = abs(normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz));

                    // The two axes lying in this face's plane.
                    vec2 uv;
                    if (nAbs.y >= nAbs.x && nAbs.y >= nAbs.z) {
                        uv = local.xz;
                    } else if (nAbs.x >= nAbs.z) {
                        uv = local.yz;
                    } else {
                        uv = local.xy;
                    }

                    // Cell boundaries are the half-way points of the thirds, so the
                    // half turns them into integers and this becomes the scene's own
                    // distance-to-the-nearest-line.
                    vec2 t = uv / cell_size + vec2(0.5);
                    vec2 fw = max(fwidth(t), vec2(1e-4));
                    vec2 dist = abs(t - round(t));
                    vec2 line = 1.0 - smoothstep(vec2(0.0), fw * 1.25, dist);
                    float grid = max(line.x, line.y);

                    vec3 base = COLOR.rgb;
                    ALBEDO = mix(base, base * grid_darken, clamp(grid * grid_strength, 0.0, 1.0));
                }
                """
        };

        var material = new ShaderMaterial { Shader = shader };

        material.SetShaderParameter("cell_size", ViewCubeMesh.CellSize);
        material.SetShaderParameter("grid_strength", GridStrength);
        material.SetShaderParameter("grid_darken", GridDarken);

        return material;
    }
}
