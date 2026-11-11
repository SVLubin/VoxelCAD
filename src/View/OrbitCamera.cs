using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// A turntable around a focus point: rotate spins about the vertical axis
/// through it, pan slides it horizontally, zoom is exponential distance, and the
/// arrow keys turn continuously.
/// </summary>
/// <remarks>
/// The focus point is the whole of the camera's state that the user has to
/// reason about, so two rules keep it predictable. Its height changes only when
/// asked for explicitly — by framing something or by Shift+wheel — never as a
/// side effect of a plain pan, zoom or Shift+arrow slide, which is what used to
/// leave the orbit axis hanging at an altitude nobody chose. And it is visible
/// whenever the camera moves: see <see cref="FocusMarker"/>.
/// </remarks>
internal sealed partial class OrbitCamera : Node3D
{
    /// <summary>
    /// The focus ring's radius as a fraction of the camera distance, which is
    /// what keeps it a constant size on screen at any zoom.
    /// </summary>
    private const float RingScreenFraction = 0.05f;

    public float Distance { get; set; } = 24f;
    public float MinDistance { get; set; } = 2f;
    public float MaxDistance { get; set; } = 120f;

    /// <summary>Goxel uses ~0.05 rad per frame (~60fps) → ~3 rad/s.</summary>
    public float ArrowTurnSpeed { get; set; } = 3f;

    /// <summary>
    /// World units per second the focus point slides under Shift+arrow, in the
    /// camera's own ground plane — left/right sideways, up/down forward and
    /// back.
    /// </summary>
    public float FocusMoveSpeed { get; set; } = 6f;

    /// <summary>World units the focus point rises or falls per wheel notch under Shift+wheel.</summary>
    public float FocusHeightPerWheelStep { get; set; } = 1f;

    /// <summary>
    /// Told on every move the camera makes, so the viewport knows it is being
    /// driven and holds the brush off — no preview, nothing to place — for as
    /// long as it is, plus the cooldown. Set by the owner; the camera only
    /// reports into it, never reads it.
    /// </summary>
    public CameraActivity? Activity { get; set; }

    /// <summary>
    /// Whose keys count as held. Set by the owner; the camera only asks. While an
    /// agent drives, the person sitting in front of the machine leaning on Alt is
    /// not navigating this camera.
    /// </summary>
    public IInputState? InputState { get; set; }

    /// <summary>
    /// The focus key was pressed. What deserves framing is the selection, which
    /// this layer knows nothing about — so the request is reported, and a
    /// presenter answers it with <see cref="FocusOn"/>.
    /// </summary>
    public event Action? FocusRequested;

    private readonly HashSet<StringName> _heldTurnActionsHashSet = new();

    private Camera3D? _camera;
    private FocusMarker? _marker;
    private Vector3 _regionSize = Vector3.One;
    private Vector3 _pivot;
    private float _yaw = Mathf.Pi / 4f;
    private float _pitch = -Mathf.Pi / 6f;

    // The turn a view-cube press started, and the one write that must not read as
    // somebody else taking the camera. See ApplyTransform and StepAnimation.
    private ViewCubeAnimation? _animation;
    private bool _isApplyingAnimation;

    private enum DragMode
    {
        None,
        Rotate,
        Pan,
        Zoom
    }

    private DragMode _dragMode = DragMode.None;

    // Rotate (Goxel on_rotate): absolute delta from gesture begin.
    private float _rotateBeginYaw;
    private float _rotateBeginPitch;
    private Vector2 _rotateBeginMouse;
    private Vector2 _viewportSize = Vector2.One;

    // Pan (horizontal slide of the focus point, from gesture begin).
    private Vector3 _panBeginPivot;
    private Vector2 _panBeginMouse;

    // Zoom drag (Ctrl+MMB).
    private float _zoomBeginDistance;
    private float _zoomBeginMouseY;

    /// <summary>
    /// True while the camera is driving, or one click away from it: a drag in
    /// progress, or the modifier that turns the next click into one. The brush
    /// asks so it stops proposing cells a click is not going to place.
    /// </summary>
    /// <remarks>
    /// The modifier is a state rather than a message — nobody sends a held Alt,
    /// and the answer has to be current on a frame where no input arrived at all
    /// — so it is asked of <see cref="InputState"/> rather than tracked from
    /// events. What that answers for is the party currently driving the editor,
    /// not the machine's keyboard: while an agent holds the editor, a hand resting
    /// on Alt in front of it is not navigating this camera. Which modifier means
    /// navigate is <see cref="ResolveDragMode"/>'s rule; the point of answering
    /// here is that the brush no longer keeps its own copy of it to disagree with.
    /// </remarks>
    public bool IsNavigating => _dragMode != DragMode.None || IsHeld(Key.Alt);

    /// <summary>Whether a turn started by the view cube is still running.</summary>
    public bool IsAnimating => _animation != null;

    public Vector3 Pivot
    {
        get => _pivot;
        set => SetPivot(value);
    }

    /// <summary>
    /// The whole turntable state, in the degrees the command surface and the
    /// handover speak. Reads the very fields the mouse gestures move, so what an
    /// agent sees and what a save records is exactly where the camera is.
    /// </summary>
    public CameraPose GetPose() =>
        new(_pivot, Mathf.RadToDeg(_yaw), Mathf.RadToDeg(_pitch), Distance);

    /// <summary>
    /// Where the camera ended up, rather than what the turntable was set to: its
    /// global transform and the angle it sees, straight off the
    /// <see cref="Camera3D"/> the scene is actually drawn through.
    /// </summary>
    /// <remarks>
    /// Read from the node instead of being recomputed from the pose, so a renderer
    /// asking this cannot frame a hair away from what the viewport is showing —
    /// including the pitch clamp and the region clamp on the focus point, which are
    /// already in the answer by the time it is a transform.
    /// </remarks>
    public CameraView GetView() =>
        _camera is null
            ? default
            : new CameraView(_camera.GlobalTransform, _camera.Fov);

    /// <summary>
    /// Aims the turntable at a whole pose. The one setter the aim commands and the
    /// handover's restore both drive, so there is a single place the state is put —
    /// and it lands through <see cref="SetPivot"/> and the same clamps a gesture
    /// obeys, so an out-of-range angle, distance or focus point is brought into
    /// range rather than refused, exactly as dragging into a wall is.
    /// </summary>
    public void SetPose(CameraPose pose)
    {
        _yaw = Mathf.DegToRad(pose.YawDegrees);
        _pitch = CameraFraming.ClampPitch(Mathf.DegToRad(pose.PitchDegrees));
        Distance = Mathf.Clamp(pose.Distance, MinDistance, MaxDistance);

        // Last, because it is the one that applies the transform; the region clamp
        // on the pivot comes for free with it.
        SetPivot(pose.Pivot);
    }

    /// <summary>
    /// Starts an eased turn to <paramref name="target"/>'s two angles. Only the
    /// angles: the focus point and the distance travel with the current pose every
    /// frame, so a turn cannot move the point the camera turns around even if it is
    /// handed a target that names a different one.
    /// </summary>
    /// <remarks>
    /// Every frame of it goes through <see cref="SetPose"/>, which is what makes the
    /// pitch clamp, the region clamp, the focus marker and the activity watch apply
    /// to an animated turn with no machinery of its own — a tool is held off during
    /// one for exactly the reason it is held off during a drag.
    /// </remarks>
    public void AnimateTo(CameraPose target)
    {
        // At the editor's own pace, so a turn and a panel opening move together
        // — and instantly when animation is off, which this curve is safe at by
        // construction: Advance runs before IsFinished is asked, so a
        // zero-length turn writes its target pose and then retires.
        _animation = new ViewCubeAnimation(
            Mathf.RadToDeg(_yaw),
            Mathf.RadToDeg(_pitch),
            target.YawDegrees,
            target.PitchDegrees,
            Timing.ScaleFixed(ViewCubeAnimation.DurationSeconds));
    }

    /// <summary>How fast this editor moves. Set once, when the scene is built.</summary>
    public MotionTiming Timing { get; set; } = new();

    /// <summary>
    /// Abandons a turn in flight, leaving the camera wherever it had reached. What
    /// a press on the cube does before deciding whether to start a new one.
    /// </summary>
    public void CancelAnimation() => _animation = null;

    /// <summary>
    /// Silences the focus marker regardless of camera motion, and reveals it
    /// again. An agent holds the real editor and drives the camera through the aim
    /// commands, so its clean frame must never carry the user's orbit marker; the
    /// user gets it back the moment control returns.
    /// </summary>
    public void SetMarkerSuppressed(bool suppressed) => _marker?.SetSuppressed(suppressed);

    /// <summary>
    /// The buildable region, which is also the focus point's cage: the camera
    /// turns around somewhere in the scene or it turns around nothing.
    /// </summary>
    /// <remarks>
    /// Re-clamps what is already there, because a document opened with a smaller
    /// region would otherwise leave the focus point outside it.
    /// </remarks>
    public void SetRegion(Vector3 size)
    {
        _regionSize = size;

        // The zoom limit is the region's, not a constant: a scene bigger than the
        // old fixed 120 units could not be backed away from far enough to be seen
        // whole. Re-clamped rather than left, because a smaller region now has a
        // nearer limit than the distance already in hand.
        MaxDistance = CameraFraming.CalculateMaxDistance(size);
        Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);

        SetPivot(_pivot);
    }

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera3D");

        // A child of the rig, whose origin is the focus point — so the marker
        // needs no position of its own, only a size and a stem length.
        _marker = new FocusMarker();
        AddChild(_marker);

        SetProcess(true);
        ApplyTransform();
    }

    /// <summary>
    /// Turning is continuous, which is why held actions are a set consulted every
    /// frame rather than an event acted on once. What the set is emphatically not
    /// is a reading of the physical keyboard: it holds what arrived while the
    /// viewport had focus, so an arrow walking the hierarchy is not also an arrow
    /// turning the camera.
    /// </summary>
    /// <remarks>
    /// Shift reassigns all four arrows from turning to sliding the focus point
    /// itself, rather than adding a second set of actions — read from
    /// <see cref="InputState"/> exactly as <see cref="IsNavigating"/> reads Alt,
    /// since it is a modifier's current state and not an event of its own. Both pairs stay
    /// in the turntable's own ground plane (<see
    /// cref="CameraFraming.GetHorizontalRight"/> and <see
    /// cref="CameraFraming.GetHorizontalForward"/>) rather than a fixed world
    /// axis, so the direction always matches what the camera is currently
    /// facing; that plane has no tilt to account for, because a turntable has
    /// no roll and both vectors are horizontal at every pitch. The result goes
    /// through <see cref="SetPivot"/> like every other move of the focus
    /// point, so the region clamp applies for free: nothing here stops it at a
    /// wall, because <see cref="CameraFraming.ClampToRegion"/> already does.
    /// </remarks>
    public override void _Process(double delta)
    {
        if (_camera == null)
        {
            return;
        }

        // Held keys first, and the turn in flight second, so that an arrow already
        // down when the cube was pressed cancels the turn on the frame it takes
        // effect rather than a frame after — the two never write the same frame.
        if (_heldTurnActionsHashSet.Count > 0)
        {
            ProcessHeldKeys(delta);
        }

        StepAnimation(delta);
    }

    private void ProcessHeldKeys(double delta)
    {
        float step = ArrowTurnSpeed * (float)delta;
        float focusStep = FocusMoveSpeed * (float)delta;
        bool isMovingFocus = IsHeld(Key.Shift);

        float yawDelta = 0f;
        float pitchDelta = 0f;
        Vector3 focusDelta = Vector3.Zero;
        Vector3 right = CameraFraming.GetHorizontalRight(_yaw);
        Vector3 forward = CameraFraming.GetHorizontalForward(_yaw);

        if (_heldTurnActionsHashSet.Contains(CameraActions.TurnLeft))
        {
            if (isMovingFocus)
                focusDelta -= right * focusStep;
            else
                yawDelta += step;
        }

        if (_heldTurnActionsHashSet.Contains(CameraActions.TurnRight))
        {
            if (isMovingFocus)
                focusDelta += right * focusStep;
            else
                yawDelta -= step;
        }

        if (_heldTurnActionsHashSet.Contains(CameraActions.TurnUp))
        {
            if (isMovingFocus)
                focusDelta += forward * focusStep;
            else
                pitchDelta += step;
        }

        if (_heldTurnActionsHashSet.Contains(CameraActions.TurnDown))
        {
            if (isMovingFocus)
                focusDelta -= forward * focusStep;
            else
                pitchDelta -= step;
        }

        if (yawDelta != 0f || pitchDelta != 0f)
        {
            ApplyTurntable(yawDelta, pitchDelta);
        }

        if (focusDelta != Vector3.Zero)
        {
            SetPivot(_pivot + focusDelta);
        }
    }

    /// <summary>
    /// One frame of a turn in flight. Reads the pivot and distance back off the
    /// current pose rather than remembering them, so the turn genuinely only writes
    /// two angles and anything else about the camera is left exactly as it is.
    /// </summary>
    private void StepAnimation(double delta)
    {
        if (_animation is not { } animation)
        {
            return;
        }

        animation.Advance(delta);

        ApplyAnimationPose(GetPose() with
        {
            YawDegrees = animation.YawDegrees,
            PitchDegrees = animation.PitchDegrees
        });

        if (animation.IsFinished)
        {
            _animation = null;
        }
    }

    /// <summary>
    /// The animation's own write, marked as such for the length of the call. It is
    /// the one pose change that is not somebody else taking the camera, and
    /// <see cref="ApplyTransform"/> tells the two apart by this and nothing else.
    /// </summary>
    private void ApplyAnimationPose(CameraPose pose)
    {
        _isApplyingAnimation = true;

        try
        {
            SetPose(pose);
        }
        finally
        {
            _isApplyingAnimation = false;
        }
    }

    /// <summary>
    /// The viewport handed the keyboard back. Held actions are dropped rather
    /// than carried: their release will be delivered to whoever has focus now, so
    /// keeping them would leave the camera turning forever.
    /// </summary>
    private bool IsHeld(Key key) => InputState?.IsKeyDown(key) ?? false;

    public void ReleaseKeys()
    {
        _heldTurnActionsHashSet.Clear();
        UpdateMarkerHold();
    }

    /// <summary>
    /// Frames a target: the focus point moves to its centre and the camera backs
    /// off far enough to fit it. Alongside Shift+wheel, one of the two gestures
    /// that may change the focus height — a plain pan, zoom or Shift+arrow slide
    /// never does.
    /// </summary>
    public void FocusOn(Aabb worldBounds)
    {
        if (_camera == null)
            return;

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float aspect = viewportSize.X / Mathf.Max(1f, viewportSize.Y);

        Distance = Mathf.Clamp(
            CameraFraming.CalculateFitDistance(worldBounds, _camera.Fov, aspect),
            MinDistance,
            MaxDistance);

        // The region wins over the framing: an element parked outside the scene
        // is looked at from the nearest point inside it, off-centre, rather than
        // by following the camera out of the room.
        SetPivot(worldBounds.GetCenter());
    }

    /// <summary>
    /// Navigation input, handed over by the viewport surface in viewport
    /// coordinates. Returns true when the camera used the event — the camera has
    /// first refusal, and what it declines is offered to the brush.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>_UnhandledInput</c>: reaching the camera must mean
    /// "the pointer is over the scene", which only the GUI hit test can answer.
    /// Unhandled input answers "no panel claimed it", and a trackpad scroll over
    /// a list is a case where the two disagree.
    /// </remarks>
    public bool HandleInput(InputEvent @event)
    {
        if (_camera == null)
        {
            return false;
        }

        _viewportSize = GetViewport().GetVisibleRect().Size;

        if (@event is InputEventMagnifyGesture magnify)
        {
            ApplyZoomFactor((float)magnify.Factor, magnify.ShiftPressed);
            return true;
        }

        if (@event is InputEventPanGesture panGesture && _dragMode == DragMode.None)
        {
            if (Mathf.Abs(panGesture.Delta.Y) >= Mathf.Abs(panGesture.Delta.X))
            {
                // Two-finger scroll ≈ mouse wheel (Goxel: dist *= 1.1^-wheel).
                float wheel = -panGesture.Delta.Y * 0.15f;
                ApplyZoomWheel(wheel, panGesture.ShiftPressed);
                return true;
            }
        }

        if (@event is InputEventMouseButton mouseButton)
        {
            return HandleMouseButton(mouseButton);
        }

        if (@event is InputEventMouseMotion mouseMotion)
        {
            return HandleMouseMotion(mouseMotion);
        }

        if (@event is InputEventKey key)
        {
            return HandleKey(key);
        }

        return false;
    }

    /// <summary>
    /// Claiming the turn keys matters twice over: it is what makes them turn the
    /// camera, and it is what stops the engine reading them as a request to move
    /// focus to a neighbouring panel — which is what an unclaimed arrow on a
    /// focused control becomes.
    /// </summary>
    private bool HandleKey(InputEventKey key)
    {
        if (key.IsAction(CameraActions.Focus))
        {
            if (key.Pressed && !key.Echo)
            {
                FocusRequested?.Invoke();
            }

            return true;
        }

        if (FindTurnAction(key) is not { } action)
        {
            return false;
        }

        // An echo is the OS repeating a key already in the set.
        if (!key.Echo)
        {
            if (key.Pressed)
            {
                _heldTurnActionsHashSet.Add(action);
            }
            else
            {
                _heldTurnActionsHashSet.Remove(action);
            }

            UpdateMarkerHold();
        }

        return true;
    }

    private static StringName? FindTurnAction(InputEventKey key)
    {
        foreach (StringName action in CameraActions.Turn)
        {
            if (key.IsAction(action))
            {
                return action;
            }
        }

        return null;
    }

    private bool HandleMouseButton(InputEventMouseButton mouseButton)
    {
        // Plain wheel is distance and nothing else — retargeting the focus
        // point at the surface under the cursor would move the orbit axis
        // without anyone having asked for it. Shift+wheel is the one
        // deliberate exception: it is the focus point's height control.
        if (mouseButton.ButtonIndex == MouseButton.WheelUp && mouseButton.Pressed)
        {
            ApplyZoomWheel(+1f, mouseButton.ShiftPressed);
            return true;
        }

        if (mouseButton.ButtonIndex == MouseButton.WheelDown && mouseButton.Pressed)
        {
            ApplyZoomWheel(-1f, mouseButton.ShiftPressed);
            return true;
        }

        if (mouseButton.Pressed)
        {
            _dragMode = ResolveDragMode(mouseButton);
            if (_dragMode == DragMode.None)
            {
                return false;
            }

            BeginDrag(mouseButton.Position);
            UpdateMarkerHold();
            return true;
        }

        if (_dragMode != DragMode.None)
        {
            _dragMode = DragMode.None;
            UpdateMarkerHold();
            return true;
        }

        return false;
    }

    private static DragMode ResolveDragMode(InputEventMouseButton mouseButton)
    {
        bool isAltPressed = mouseButton.AltPressed;
        bool isShiftPressed = mouseButton.ShiftPressed;
        bool isCtrlPressed = mouseButton.CtrlPressed || mouseButton.MetaPressed;

        // Goxel: Alt held → LMB emulates MMB.
        bool isMiddleButton = mouseButton.ButtonIndex == MouseButton.Middle
            || (mouseButton.ButtonIndex == MouseButton.Left && isAltPressed);

        if (isMiddleButton && isCtrlPressed)
        {
            return DragMode.Zoom;
        }

        if (isMiddleButton && isShiftPressed)
        {
            return DragMode.Pan;
        }

        if (isMiddleButton)
        {
            return DragMode.Rotate;
        }

        // Default keymap: RMB = pan.
        if (mouseButton.ButtonIndex == MouseButton.Right)
        {
            return DragMode.Pan;
        }

        return DragMode.None;
    }

    private void BeginDrag(Vector2 mousePosition)
    {
        switch (_dragMode)
        {
            case DragMode.Rotate:
                _rotateBeginYaw = _yaw;
                _rotateBeginPitch = _pitch;
                _rotateBeginMouse = mousePosition;
                break;

            case DragMode.Pan:
                _panBeginPivot = _pivot;
                _panBeginMouse = mousePosition;
                break;

            case DragMode.Zoom:
                _zoomBeginDistance = Distance;
                _zoomBeginMouseY = mousePosition.Y;
                break;
        }
    }

    private bool HandleMouseMotion(InputEventMouseMotion mouseMotion)
    {
        if (_dragMode == DragMode.None || _camera == null)
        {
            return false;
        }

        Vector2 mousePosition = mouseMotion.Position;
        float viewportWidth = Mathf.Max(1f, _viewportSize.X);
        float viewportHeight = Mathf.Max(1f, _viewportSize.Y);

        switch (_dragMode)
        {
            case DragMode.Rotate:
            {
                // Goxel on_rotate: z_rot/x_rot from begin → current as fraction of
                // viewport * 2π. Through TurntableRate, which is the same answer
                // the material preview turns by — the same fingers over two
                // surfaces of this editor turn them at the same rate.
                float zRot = TurntableRate.Turn(
                    _rotateBeginMouse.X - mousePosition.X, viewportWidth);

                float xRot = TurntableRate.Turn(
                    mousePosition.Y - _rotateBeginMouse.Y, viewportHeight);
                _yaw = _rotateBeginYaw + zRot;
                _pitch = CameraFraming.ClampPitch(_rotateBeginPitch + xRot);
                ApplyTransform();
                break;
            }

            case DragMode.Pan:
            {
                // Measured from the gesture's start, not accumulated frame by
                // frame: the same reason a quantised drag is, that a sum of
                // deltas drifts away from what the cursor actually did — and
                // what lets a drag come back off a wall it has been pressed
                // against, since the clamp never feeds itself.
                SetPivot(_panBeginPivot + CameraFraming.CalculatePanOffset(
                    mousePosition - _panBeginMouse,
                    _yaw,
                    _pitch,
                    Distance,
                    _camera.Fov,
                    viewportHeight));

                break;
            }

            case DragMode.Zoom:
            {
                // Goxel on_zoom: zoom = (y - last_y) / 10 → dist *= 1.1^-zoom
                float zoom = (_zoomBeginMouseY - mousePosition.Y) / 10f;
                Distance = Mathf.Clamp(_zoomBeginDistance * Mathf.Pow(1.1f, -zoom), MinDistance, MaxDistance);
                ApplyTransform();
                break;
            }
        }

        return true;
    }

    /// <summary>
    /// A wheel notch: distance by default, or — held with Shift — the focus
    /// point's height instead. The two share one gesture rather than one more
    /// modifier combination to remember, because a wheel is otherwise idle
    /// while Shift is down for the arrow slide.
    /// </summary>
    private void ApplyZoomWheel(float wheelSteps, bool isShiftPressed)
    {
        if (isShiftPressed)
        {
            SetPivot(_pivot + new Vector3(0f, wheelSteps * FocusHeightPerWheelStep, 0f));
            return;
        }

        // Goxel: dist *= pow(1.1, -wheel)
        Distance = Mathf.Clamp(Distance * Mathf.Pow(1.1f, -wheelSteps), MinDistance, MaxDistance);
        ApplyTransform();
    }

    private void ApplyZoomFactor(float magnifyFactor, bool isShiftPressed)
    {
        if (magnifyFactor <= 0f)
        {
            return;
        }

        // Solving dist *= pow(1.1, -wheel) for wheel given dist /= magnifyFactor,
        // so a pinch and a wheel notch agree on what "the same amount" means —
        // including, under Shift, the same height per equivalent notch.
        float wheelSteps = Mathf.Log(magnifyFactor) / Mathf.Log(1.1f);

        ApplyZoomWheel(wheelSteps, isShiftPressed);
    }

    private void ApplyTurntable(float yawDelta, float pitchDelta)
    {
        _yaw += yawDelta;
        _pitch = CameraFraming.ClampPitch(_pitch + pitchDelta);
        ApplyTransform();
    }

    /// <summary>
    /// The only way the focus point moves. Everything that would set it goes
    /// through here, which is what makes the region a rule rather than something
    /// each gesture has to remember to respect.
    /// </summary>
    private void SetPivot(Vector3 pivot)
    {
        _pivot = CameraFraming.ClampToRegion(pivot, _regionSize);

        ApplyTransform();
    }

    /// <summary>
    /// Whether a camera gesture is under way, so the marker can stay up through
    /// the still moments inside one.
    /// </summary>
    private void UpdateMarkerHold() =>
        _marker?.SetHeld(_dragMode != DragMode.None || _heldTurnActionsHashSet.Count > 0);

    private void ApplyTransform()
    {
        if (_camera == null)
        {
            return;
        }

        // Every path that moves the camera ends here, which is what makes it the
        // one place a turn in flight can be given up without every caller having to
        // remember to — a drag, a wheel notch, an arrow, an aim command, a handover
        // restore, a new document. A cancel each of them had to do for itself is a
        // cancel the next gesture added forgets.
        if (!_isApplyingAnimation)
        {
            _animation = null;
        }

        Vector3 offset = CameraFraming.GetOrbitOffset(_yaw, _pitch);

        GlobalPosition = _pivot;
        _camera.Position = offset * Distance;
        _camera.LookAt(_pivot, Vector3.Up);

        // Every path that moves the camera ends here, which is what makes this
        // the one place the marker has to be told anything — and, for the same
        // reason, the one place the activity watch is refreshed, so a gesture
        // added later cannot forget to hold the brush off. The two spurious
        // callers (startup and a region change) cost only a harmless cooldown
        // with nothing to place through it.
        _marker?.SetPose(Distance * RingScreenFraction, _pivot.Y);
        Activity?.NoteMove();
    }
}
