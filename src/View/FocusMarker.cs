using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The point the camera turns around, drawn where it is: a ring lying in the
/// horizontal plane at the focus height, a small cross marking its exact
/// centre, and — only when that height is not zero — a stem down to the scene
/// floor with a second cross at its foot.
/// </summary>
/// <remarks>
/// A turntable always has this point — it is the origin of the orbit and, since
/// the camera looks straight at it, the centre of the screen. Leaving it
/// invisible is what makes orbiting feel arbitrary: the axis is real, the user
/// just cannot see which one it is. The stem is not decoration for the same
/// reason — a ring floating in space is indistinguishable from a ring on the
/// floor, and the height is precisely what a pan must not be able to change
/// behind the user's back. The cross pins down the centre the ring only
/// implies; the floor cross repeats it at the stem's foot, in the stem's own
/// shade rather than the ring's, so the drop reads as one height read-out —
/// stem and both its ends — distinct from the ring and centre cross above it.
/// <para>
/// It appears while the camera is being driven and fades once it stops, so it
/// costs nothing while modelling.
/// </para>
/// </remarks>
internal sealed partial class FocusMarker : Node3D
{
    // Cyan read poorly against the floor grid's own light blue-lavender — too
    // close in hue to stand out. Orange sits opposite that on the wheel, so it
    // reads against the floor, the walls and the dark void alike.
    private static readonly Color MarkerColor = new(1f, 0.55f, 0.1f);

    /// <summary>
    /// The stem's own shade — the same hue, darkened, so it reads as belonging
    /// to the marker without being mistaken for the ring or either cross.
    /// </summary>
    private static readonly Color StemColor = MarkerColor.Darkened(0.4f);

    private const int RingSegments = 64;

    /// <summary>Cross arm half-length as a fraction of the ring radius.</summary>
    private const float CrosshairFraction = 0.22f;

    /// <summary>Seconds at full opacity after the camera last moved.</summary>
    private const float HoldSeconds = 0.05f;

    /// <summary>
    /// Seconds spent easing out from full opacity to none. Hold plus fade is
    /// kept equal to <see cref="CameraActivity.CooldownSeconds"/>: when the
    /// marker has finished fading, the camera has finished counting as active, so
    /// the two read as one event. Change one and change the other.
    /// </summary>
    private const float FadeSeconds = 0.15f;

    private readonly MeshInstance3D _ring;
    private readonly MeshInstance3D _crosshair;
    private readonly MeshInstance3D _floorCrosshair;
    private readonly MeshInstance3D _stem;
    private readonly StandardMaterial3D _material;
    private readonly StandardMaterial3D _stemMaterial;

    private float _remainingSeconds;
    private bool _isHeld;
    private bool _isSuppressed;

    public FocusMarker()
    {
        Name = "FocusMarker";

        _material = CreateMaterial(MarkerColor);
        _stemMaterial = CreateMaterial(StemColor);

        ArrayMesh crosshairMesh = BuildCrosshair();

        _ring = CreateLines("Ring", BuildRing(), _material);
        _crosshair = CreateLines("Crosshair", crosshairMesh, _material);
        // The stem's shade, not the ring's: the floor cross is part of the
        // height read-out, the same way the stem is, not a second centre mark.
        _floorCrosshair = CreateLines("FloorCrosshair", crosshairMesh, _stemMaterial);
        _stem = CreateLines("Stem", BuildStem(), _stemMaterial);

        AddChild(_ring);
        AddChild(_crosshair);
        AddChild(_floorCrosshair);
        AddChild(_stem);

        Visible = false;
    }

    public override void _Ready() => SetProcess(true);

    /// <summary>
    /// Places the marker's geometry and reveals it. Called by the camera whenever
    /// it moves — which is exactly when the marker should be on screen, so the
    /// two are one call rather than two.
    /// </summary>
    /// <param name="ringRadius">
    /// In world units. The camera derives it from its distance, so the ring keeps
    /// a constant size on screen instead of vanishing when zoomed out.
    /// </param>
    /// <param name="heightAboveFloor">
    /// How far the focus point sits above the scene floor. The stem spans it,
    /// and the floor cross sits at its far end.
    /// </param>
    public void SetPose(float ringRadius, float heightAboveFloor)
    {
        // Suppressed means the marker is not the user's to see right now — an agent
        // holds the editor — so a camera move places its geometry but never reveals
        // it. Leaving early here, rather than at every caller, keeps the one rule in
        // one place.
        if (_isSuppressed)
            return;

        var ringScale = new Vector3(ringRadius, ringRadius, ringRadius);
        _ring.Scale = ringScale;
        _crosshair.Scale = ringScale;
        _floorCrosshair.Scale = ringScale;
        _floorCrosshair.Position = new Vector3(0f, -heightAboveFloor, 0f);

        // Nothing above zero height to draw a stem across, and a floor cross
        // sitting exactly on the centre cross is not a second mark, it is the
        // same one drawn twice.
        bool hasHeight = heightAboveFloor > 0.01f;
        _stem.Visible = hasHeight;
        _stem.Scale = new Vector3(1f, Mathf.Max(heightAboveFloor, 0.01f), 1f);
        _floorCrosshair.Visible = hasHeight;

        _remainingSeconds = HoldSeconds + FadeSeconds;
        Visible = true;
    }

    /// <summary>
    /// Whether a camera gesture is in progress. A held marker does not fade: a
    /// drag with the mouse briefly still, or an arrow key leaning on one axis,
    /// is a gesture with no events in it, and the marker has to outlast that.
    /// </summary>
    public void SetHeld(bool isHeld) => _isHeld = isHeld;

    /// <summary>
    /// Hides the marker for as long as an agent holds the editor, and lets it
    /// resume when the user has it back. Going quiet takes it off screen at once;
    /// it stays off through any camera the agent drives, then reappears on the
    /// user's next move.
    /// </summary>
    public void SetSuppressed(bool isSuppressed)
    {
        _isSuppressed = isSuppressed;

        if (isSuppressed)
            Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;

        if (_isHeld)
        {
            _remainingSeconds = HoldSeconds + FadeSeconds;
            ApplyOpacity(1f);
            return;
        }

        _remainingSeconds -= (float)delta;

        if (_remainingSeconds <= 0f)
        {
            Visible = false;
            return;
        }

        float linear = Mathf.Clamp(_remainingSeconds / FadeSeconds, 0f, 1f);

        // Smoothstep rather than a linear ramp: eases in and out of the fade
        // instead of switching abruptly from held to dimming.
        float eased = linear * linear * (3f - 2f * linear);

        ApplyOpacity(eased);
    }

    private void ApplyOpacity(float opacity)
    {
        _material.AlbedoColor = new Color(MarkerColor, opacity);
        _stemMaterial.AlbedoColor = new Color(StemColor, opacity);
    }

    private static MeshInstance3D CreateLines(string name, ArrayMesh mesh, StandardMaterial3D material) => new()
    {
        Name = name,
        Mesh = mesh,
        MaterialOverride = material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        Layers = RenderLayers.Voxels
    };

    /// <summary>
    /// Unshaded and outside the depth buffer, for the same reason as the erase
    /// marker: this is an overlay reporting where the camera is pointed, and it
    /// has to read against whatever it happens to be pointed at — including the
    /// inside of a voxel it sits within.
    /// </summary>
    private static StandardMaterial3D CreateMaterial(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = true,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        RenderPriority = 127
    };

    /// <summary>A unit circle in the horizontal plane, as a line list.</summary>
    private static ArrayMesh BuildRing()
    {
        var points = new Vector3[RingSegments * 2];

        for (var segment = 0; segment < RingSegments; segment++)
        {
            points[segment * 2] = GetRingPoint(segment);
            points[segment * 2 + 1] = GetRingPoint(segment + 1);
        }

        return BuildLineMesh(points);
    }

    private static Vector3 GetRingPoint(int segment)
    {
        float angle = Mathf.Tau * segment / RingSegments;

        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    /// <summary>
    /// A small "+" at the centre, pinning down the point the ring only implies.
    /// Reused, unscaled and unmoved, for the echo on the floor — only the node
    /// wrapping it differs in position.
    /// </summary>
    private static ArrayMesh BuildCrosshair() => BuildLineMesh(new[]
    {
        new Vector3(-CrosshairFraction, 0f, 0f), new Vector3(CrosshairFraction, 0f, 0f),
        new Vector3(0f, 0f, -CrosshairFraction), new Vector3(0f, 0f, CrosshairFraction)
    });

    /// <summary>A unit-length drop from the focus point, scaled to reach the floor.</summary>
    private static ArrayMesh BuildStem() =>
        BuildLineMesh(new[] { Vector3.Zero, Vector3.Down });

    private static ArrayMesh BuildLineMesh(Vector3[] points)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

        return mesh;
    }
}
