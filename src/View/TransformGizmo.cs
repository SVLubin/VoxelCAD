using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The transform handles: three axis arrows, rings or cubes on the selected
/// element — plus, under the move tool, three plane patches that slide two axes at
/// once — and the drag that turns pulling one into a change of its transform.
/// </summary>
/// <remarks>
/// A node of its own rather than a child of the element, because it is not part of
/// the scene: it sits at the element's origin wearing the frame the element's
/// values are expressed in — its parent's — and is rescaled every frame so it
/// stays the same size on screen at any zoom. An element's own rotation and scale
/// deliberately do not reach it; a handle that shrank with the object it scales
/// would be unusable.
/// <para>
/// It reports geometry and decides no policy. What comes out of a drag is a
/// cumulative delta from where it started, so the presenter can apply the canon's
/// <c>start + Σdelta</c> rule and let the model quantise the result. Nothing here
/// knows what a history record or a field mode is. A plane drag needs nothing
/// added on that seam: two components of the delta are non-zero instead of one,
/// and the rule that adds it to the value the drag started from is the same rule.
/// </para>
/// <para>
/// Everything is drawn unshaded and out of the depth test. That is not the
/// lighting model being faked: this is editor chrome, like the erase marker and
/// the focus ring, and it has to read on top of whatever it is drawn over. The
/// voxels themselves keep the one real Lambert model they have always had.
/// </para>
/// </remarks>
internal sealed partial class TransformGizmo : Node3D
{
    /// <summary>
    /// Three axis handles, then three plane handles — handle <c>3 + n</c> being the
    /// plane whose normal is axis <c>n</c>, so it slides the other two. One index
    /// space for both, because everything that asks "which handle" — the hover, the
    /// hit test, the drag in flight — asks it the same way of either kind.
    /// </summary>
    private const int AxisCount = 3;

    private const int HandleCount = AxisCount * 2;

    /// <summary>
    /// How much of the distance to the camera the handles span. A fraction rather
    /// than a length, which is what makes them one size on screen at every zoom —
    /// the same rule the focus marker's ring follows.
    /// </summary>
    private const float ScreenFraction = 0.14f;

    /// <summary>Where a shaft starts, clear of the origin the three of them share.</summary>
    private const float ShaftStart = 0.16f;

    private const float ArrowShaftEnd = 0.78f;
    private const float ScaleShaftEnd = 0.86f;
    private const float HandleLength = 1f;
    private const float ShaftRadius = 0.014f;
    private const float ArrowHeadRadius = 0.06f;
    private const float ScaleCubeSize = 0.13f;

    /// <summary>How close the pointer has to pass a shaft to have grabbed it.</summary>
    private const float HitRadius = 0.13f;

    private const float RingRadius = 0.9f;
    private const float RingHitTolerance = 0.12f;
    private const int RingSegments = 72;

    /// <summary>
    /// Where a plane patch sits in the quadrant its two axes span — clear of the
    /// shafts that run along them, and near enough the origin to read as belonging
    /// to both rather than to neither.
    /// </summary>
    private const float PlaneNear = 0.30f;

    private const float PlaneFar = 0.62f;

    /// <summary>
    /// How much of its own normal a plane has to be turned towards the eye before it
    /// is drawn and aimed at. Below it the patch is a line on screen, lying over the
    /// very shafts it sits between, and would take clicks meant for them.
    /// </summary>
    private const float PlaneFacingMinimum = 0.2f;

    /// <summary>How solid a patch's fill is — a hint at the plane, not a lid on it.</summary>
    private const float PlaneFillAlpha = 0.26f;

    private readonly Node3D[] _handleNodes = new Node3D[HandleCount];
    private readonly List<HandlePart>[] _partsByHandleList =
    {
        new(), new(), new(), new(), new(), new()
    };

    private readonly StandardMaterial3D[] _axisMaterials = new StandardMaterial3D[AxisCount];
    private readonly StandardMaterial3D[] _planeFillMaterials = new StandardMaterial3D[AxisCount];
    private readonly StandardMaterial3D _hoverMaterial = CreateMaterial(EditorAxisColors.Hover);
    private readonly StandardMaterial3D _hoverFillMaterial = CreateFillMaterial(EditorAxisColors.Hover);

    private Node3D? _target;

    /// <summary>Where on the target the handles sit, in its own local space.</summary>
    private Vector3 _pivot;

    private ToolKind _tool;
    private float _screenScale = 1f;
    private int _hoveredHandle = -1;

    // Which plane patches are turned towards the eye this frame, one bit per normal
    // axis. Recomputed with the placement and read by both the drawing and the hit
    // test, so what can be seen and what can be grabbed are the same set.
    private int _planeFacingMask;

    // The drag, captured once at the press and never re-derived: the element moves
    // under the pointer as it is dragged, so a line re-measured from it each frame
    // would chase itself.
    private int _dragHandle = -1;
    private Vector3 _dragOrigin;
    private Vector3 _dragDirection;
    private Vector3 _dragNormal;
    private Vector3 _dragAxisU;
    private Vector3 _dragAxisV;
    private float _dragStart;
    private float _dragLastAngle;
    private float _dragTotalAngle;
    private float _dragHandleLength = 1f;

    // A plane drag's own capture: the two parent axes it slides along, unnormalized
    // so the pair of parameters comes out in the units the element's values are
    // written in, and where on them the press landed.
    private Vector3 _dragPlaneDirectionU;
    private Vector3 _dragPlaneDirectionV;
    private float _dragPlaneStartU;
    private float _dragPlaneStartV;

    public TransformGizmo()
    {
        Name = "TransformGizmo";
        Visible = false;

        for (var axis = 0; axis < AxisCount; axis++)
        {
            _axisMaterials[axis] = CreateMaterial(EditorAxisColors.Get(axis));
            _planeFillMaterials[axis] = CreateFillMaterial(EditorAxisColors.Get(axis));

            AddHandleNode(axis, $"Axis{axis}", CreateAxisBasis(axis));

            // The patches are built in this node's own frame, out of both axes they
            // span at once, so they get no basis of their own — one frame to read
            // them in is what keeps the drawing and the hit region the same shape
            // under a skewed parent.
            AddHandleNode(AxisCount + axis, $"Plane{axis}", Basis.Identity);
        }
    }

    /// <summary>A handle was grabbed. The edits that follow are one gesture.</summary>
    public event Action? DragBegan;

    /// <summary>
    /// How far the grabbed handle has been pulled since the press, in the units of
    /// the channel its tool edits: cells for a move, degrees for a rotation,
    /// multiples of one for a scale. Cumulative, so the receiver adds it to the
    /// value the drag started from and never to the current one.
    /// </summary>
    public event Action<Vector3>? Dragged;

    public event Action? DragEnded;

    /// <summary>
    /// The drag was abandoned rather than finished. What that ought to leave behind
    /// is not this layer's decision — it says only that the user let go of the idea.
    /// </summary>
    public event Action? DragCancelled;

    public bool IsDragging => _dragHandle >= 0;

    /// <summary>
    /// Whether the tool has plane patches at all. Only the move tool does: sliding
    /// two axes at once is a plain sum of two moves, where a two-axis rotation is
    /// not a sum of two Euler steps and a two-axis scale has no handle shape that
    /// says which pair it belongs to.
    /// </summary>
    private bool HasPlaneHandles => _tool == ToolKind.Move;

    public void SetTool(ToolKind tool)
    {
        if (_tool == tool)
            return;

        // Changing tools mid-drag abandons it: the handle being held is not one of
        // the handles the new tool has.
        CancelDrag();

        _tool = tool;

        RebuildHandles();
    }

    /// <summary>
    /// Which element the handles sit on, as the node the viewport keeps for it —
    /// so this reads the live world transform the engine composes rather than
    /// keeping a copy of it — and where on that element they sit, as a point in
    /// its own local space.
    /// </summary>
    /// <remarks>
    /// The pivot is pushed rather than derived here for the reason everything else
    /// in this layer is: resolving it means walking the element's cells, and the
    /// rule that places it is the model's. Only a change of <em>element</em>
    /// abandons a drag in flight — a pivot that moved under the same target is the
    /// handles sliding, not a different thing being held.
    /// </remarks>
    public void SetTarget(Node3D? node, Vector3 pivot)
    {
        if (!ReferenceEquals(_target, node))
            CancelDrag();

        _target = node;
        _pivot = pivot;
    }

    /// <summary>
    /// Places the handles for this frame and works out which one the pointer is
    /// over. <paramref name="allowHandles"/> is false while the viewport is not the
    /// panel listening, <paramref name="allowHover"/> is false while the camera is
    /// being driven — exactly as the brush preview is suppressed then — and
    /// <paramref name="buttonHeld"/> says whether the driving party still holds
    /// the button this drag started with.
    /// </summary>
    /// <remarks>
    /// The handles are a tool's pointer, and a tool acts where the keyboard is: with
    /// the viewport not listening, a selected element shows its border and nothing
    /// else. The pointer being outside the viewport is a different question and is
    /// not asked here — handles vanishing every time the mouse crossed into a dock
    /// would be worse than useless.
    /// <para>
    /// A drag already in flight is exempt. Whatever took the focus, the handle being
    /// held is not something to remove from under the hand, and the drag is ended by
    /// its own button rather than by anything about focus.
    /// </para>
    /// </remarks>
    public void Update(
        Camera3D camera,
        Vector2 pointer,
        bool allowHandles,
        bool allowHover,
        bool buttonHeld)
    {
        // The button is the authority on whether a drag is still happening. A
        // release delivered somewhere else — the pointer left the window, another
        // control took the event — would otherwise leave a handle held for good.
        // Whose button that is comes from outside: asking the machine directly let
        // a bystander's click end an agent's drag.
        if (IsDragging && !buttonHeld)
            EndDrag();

        if ((!allowHandles && !IsDragging) || !TryPlace(camera))
        {
            Visible = false;
            SetHovered(-1);

            return;
        }

        Visible = true;

        SyncPlaneFacing(camera);

        // The handle being dragged stays lit however far the pointer has wandered
        // off it — it is the one being acted on, which is what the colour says.
        SetHovered(_dragHandle >= 0 ? _dragHandle : allowHover ? FindHandle(camera, pointer) : -1);
    }

    /// <summary>
    /// Viewport input, offered after the camera has refused it. The handles use the
    /// plain left button, which the camera never claims, so the two never contend
    /// for the same gesture.
    /// </summary>
    public bool HandleInput(InputEvent @event, Camera3D camera)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                    return !IsDragging && !button.AltPressed && TryBeginDrag(camera, button.Position);

                if (!IsDragging)
                    return false;

                EndDrag();

                return true;

            case InputEventMouseMotion motion when IsDragging:
                UpdateDrag(camera, motion.Position);

                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// The element's origin, wearing its parent's frame — the one an element's
    /// transform values are expressed in, so pulling the red handle changes X and
    /// nothing else.
    /// </summary>
    /// <remarks>
    /// The axes are normalized rather than orthonormalized: a skewed parent has
    /// axes that are genuinely not perpendicular, and Gram-Schmidt would draw a
    /// square frame that lies about which way X goes. A parent flattened to zero on
    /// some axis has no direction to draw at all, and the handles simply go away.
    /// </remarks>
    private bool TryPlace(Camera3D camera)
    {
        // The tools that qualify are named, not the tools that do not — refusing
        // "Paint or Eyedropper" let the extruder fall through this gap when it
        // was added, and the gizmo then placed itself invisibly
        // under them: no parts drawn, but the mathematical hit test still
        // answering, eating any cell-tool click that passed near the pivot's
        // axes. See TransformTools.
        if (!TransformTools.IsTransformTool(_tool)
            || _target == null
            || !IsInstanceValid(_target)
            || !_target.IsInsideTree()
            || _target.GetParent() is not Node3D parent)
        {
            return false;
        }

        // The element's own frame carries the pivot up, so the handles land on the
        // point the model named however the element is rotated or scaled. Their
        // *axes* still come from the parent, unchanged: a handle points along the
        // frame the transform is written in, not along the element's own.
        Vector3 origin = _target.ToGlobal(_pivot);
        Basis parentBasis = parent.GlobalTransform.Basis;
        var columns = new Vector3[3];

        for (var axis = 0; axis < 3; axis++)
        {
            Vector3 column = GetAxis(parentBasis, axis);

            if (column.LengthSquared() < 1e-8f)
                return false;

            columns[axis] = column.Normalized();
        }

        _screenScale = Mathf.Max(camera.GlobalPosition.DistanceTo(origin) * ScreenFraction, 0.001f);

        GlobalTransform = new Transform3D(
            new Basis(
                columns[0] * _screenScale,
                columns[1] * _screenScale,
                columns[2] * _screenScale),
            origin);

        return true;
    }

    /// <summary>
    /// Which plane patches are worth drawing and aiming at from where the camera
    /// now is, and hides the rest. Recomputed every frame because the answer is the
    /// camera's: nothing about the element changes it, and any orbit does.
    /// </summary>
    /// <remarks>
    /// The patch being dragged is kept regardless. A drag carries the element about,
    /// so its plane can turn edge-on part-way through — and a handle vanishing from
    /// under the pointer that is holding it would be the interface losing its nerve
    /// mid-gesture. Nothing else follows from the visibility: the drag was captured
    /// at the press.
    /// </remarks>
    private void SyncPlaneFacing(Camera3D camera)
    {
        _planeFacingMask = 0;

        if (!HasPlaneHandles)
        {
            for (var axis = 0; axis < AxisCount; axis++)
                _handleNodes[AxisCount + axis].Visible = false;

            return;
        }

        Basis basis = GlobalTransform.Basis;
        Vector3 eye = camera.GlobalPosition;

        for (var axis = 0; axis < AxisCount; axis++)
        {
            bool facing = GizmoMath.IsPlaneFacing(
                GlobalPosition,
                GetAxis(basis, (axis + 1) % AxisCount),
                GetAxis(basis, (axis + 2) % AxisCount),
                eye,
                PlaneFacingMinimum);

            if (facing)
                _planeFacingMask |= 1 << axis;

            _handleNodes[AxisCount + axis].Visible = facing || _dragHandle == AxisCount + axis;
        }
    }

    /// <summary>
    /// Which handle the pointer is on, or -1. The nearest along the ray wins, so
    /// where two handles overlap on screen the one in front is the one grabbed.
    /// </summary>
    private int FindHandle(Camera3D camera, Vector2 pointer)
    {
        Vector3 rayOrigin = camera.ProjectRayOrigin(pointer);
        Vector3 rayDirection = camera.ProjectRayNormal(pointer);

        var best = -1;
        var bestDistance = float.MaxValue;

        for (var handle = 0; handle < HandleCount; handle++)
        {
            if (!TryHitHandle(handle, rayOrigin, rayDirection, out float distance) || distance >= bestDistance)
                continue;

            best = handle;
            bestDistance = distance;
        }

        return best;
    }

    private bool TryHitHandle(int handle, Vector3 rayOrigin, Vector3 rayDirection, out float distance)
    {
        distance = 0f;

        if (handle >= AxisCount)
            return TryHitPlane(handle - AxisCount, rayOrigin, rayDirection, out distance);

        if (_tool == ToolKind.Rotate)
        {
            return GizmoMath.TryHitRing(
                GlobalPosition,
                GetAxis(GlobalTransform.Basis, handle).Normalized(),
                RingRadius * _screenScale,
                RingHitTolerance * _screenScale,
                rayOrigin,
                rayDirection,
                out distance);
        }

        return GizmoMath.TryHitSegment(
            GlobalTransform * (UnitAxis(handle) * ShaftStart),
            GlobalTransform * (UnitAxis(handle) * HandleLength),
            rayOrigin,
            rayDirection,
            HitRadius * _screenScale,
            out distance);
    }

    /// <summary>
    /// The patch is tested against the very columns it is drawn from, with its bounds
    /// in their units — so the region that answers a click is the parallelogram on
    /// screen, whatever the parent's scale or skew, with no second copy of the shape
    /// to keep in step. A patch turned away is not there to hit.
    /// </summary>
    private bool TryHitPlane(int normalAxis, Vector3 rayOrigin, Vector3 rayDirection, out float distance)
    {
        distance = 0f;

        if ((_planeFacingMask & (1 << normalAxis)) == 0)
            return false;

        Basis basis = GlobalTransform.Basis;

        return GizmoMath.TryHitQuad(
            GlobalPosition,
            GetAxis(basis, (normalAxis + 1) % AxisCount),
            GetAxis(basis, (normalAxis + 2) % AxisCount),
            PlaneNear,
            PlaneFar,
            rayOrigin,
            rayDirection,
            out distance);
    }

    private bool TryBeginDrag(Camera3D camera, Vector2 pointer)
    {
        if (!Visible || _target == null || _target.GetParent() is not Node3D parent)
            return false;

        int handle = FindHandle(camera, pointer);

        if (handle < 0)
            return false;

        Vector3 rayOrigin = camera.ProjectRayOrigin(pointer);
        Vector3 rayDirection = camera.ProjectRayNormal(pointer);

        Basis parentBasis = parent.GlobalTransform.Basis;

        // The drag is measured from where the handles are drawn — the gizmo's own
        // placed origin, which TryPlace put on the pivot this frame. Measuring
        // from the element's node origin instead was the bug this line replaces:
        // once the pivot entered the matrix the two came apart by basis · pivot,
        // and a drag anchored sixteen cells from the arrow under the pointer
        // inverted moves and turned rotation sensitivity into a function of how
        // far the element stood from its own local zero.
        Vector3 origin = GlobalTransform.Origin;

        if (handle >= AxisCount)
        {
            if (!TryBeginPlaneDrag(handle - AxisCount, parentBasis, origin, rayOrigin, rayDirection))
                return false;

            _dragHandle = handle;
            _dragOrigin = origin;

            DragBegan?.Invoke();

            return true;
        }

        int axis = handle;

        // The parent's own axis vector, left unnormalized: it carries the parent's
        // scale, so the parameter along it comes out in the units the element's
        // transform is written in.
        Vector3 direction = GetAxis(parentBasis, axis);

        if (_tool == ToolKind.Rotate)
        {
            Vector3 normal = direction.Normalized();
            Vector3 axisU = GetAxis(parentBasis, (axis + 1) % 3).Normalized();
            Vector3 axisV = GetAxis(parentBasis, (axis + 2) % 3).Normalized();

            if (!GizmoMath.TryAngleOnPlane(
                    origin,
                    normal,
                    axisU,
                    axisV,
                    rayOrigin,
                    rayDirection,
                    out float angle))
            {
                return false;
            }

            _dragNormal = normal;
            _dragAxisU = axisU;
            _dragAxisV = axisV;
            _dragLastAngle = angle;
            _dragTotalAngle = 0f;
        }
        else
        {
            if (!GizmoMath.TryProjectOnAxis(origin, direction, rayOrigin, rayDirection, out float parameter))
                return false;

            _dragStart = parameter;

            // What the visible handle spans, measured in the parent's units. A
            // scale drag is expressed against it, so pulling a handle out by its
            // own length is one unit of scale whatever the zoom or the parent.
            _dragHandleLength = Mathf.Max(_screenScale / direction.Length(), 1e-4f);
        }

        _dragHandle = axis;
        _dragOrigin = origin;
        _dragDirection = direction;

        DragBegan?.Invoke();

        return true;
    }

    /// <summary>
    /// Captures a plane drag: the two parent axes the patch spans and where on them
    /// the press landed. Same bargain as the axis case — the directions are the
    /// parent's own, unnormalized, so the pair of parameters is already in the units
    /// the element's position is written in and nothing downstream corrects for a
    /// scaled parent.
    /// </summary>
    private bool TryBeginPlaneDrag(
        int normalAxis,
        Basis parentBasis,
        Vector3 origin,
        Vector3 rayOrigin,
        Vector3 rayDirection)
    {
        Vector3 directionU = GetAxis(parentBasis, (normalAxis + 1) % AxisCount);
        Vector3 directionV = GetAxis(parentBasis, (normalAxis + 2) % AxisCount);

        if (!GizmoMath.TryProjectOnPlane(
                origin,
                directionU,
                directionV,
                rayOrigin,
                rayDirection,
                out float startU,
                out float startV))
        {
            return false;
        }

        _dragPlaneDirectionU = directionU;
        _dragPlaneDirectionV = directionV;
        _dragPlaneStartU = startU;
        _dragPlaneStartV = startV;

        return true;
    }

    private void UpdateDrag(Camera3D camera, Vector2 pointer)
    {
        Vector3 rayOrigin = camera.ProjectRayOrigin(pointer);
        Vector3 rayDirection = camera.ProjectRayNormal(pointer);

        if (_dragHandle >= AxisCount)
        {
            UpdatePlaneDrag(rayOrigin, rayDirection);

            return;
        }

        if (_tool == ToolKind.Rotate)
        {
            if (!GizmoMath.TryAngleOnPlane(
                    _dragOrigin,
                    _dragNormal,
                    _dragAxisU,
                    _dragAxisV,
                    rayOrigin,
                    rayDirection,
                    out float angle))
            {
                return;
            }

            // Accumulated a step at a time so a turn past the half-way point keeps
            // going the way it was going, instead of reading as a jump backwards.
            _dragTotalAngle += GizmoMath.WrapRadians(angle - _dragLastAngle);
            _dragLastAngle = angle;

            Dragged?.Invoke(UnitAxis(_dragHandle) * Mathf.RadToDeg(_dragTotalAngle));

            return;
        }

        if (!GizmoMath.TryProjectOnAxis(
                _dragOrigin,
                _dragDirection,
                rayOrigin,
                rayDirection,
                out float parameter))
        {
            return;
        }

        float travelled = parameter - _dragStart;

        Dragged?.Invoke(_tool == ToolKind.Scale
            ? UnitAxis(_dragHandle) * (travelled / _dragHandleLength)
            : UnitAxis(_dragHandle) * travelled);
    }

    /// <summary>
    /// Both components of a plane drag at once, each measured from where the press
    /// landed on its own axis — one delta with two non-zero components, which is
    /// what the receiver adds to the value the drag started from.
    /// </summary>
    private void UpdatePlaneDrag(Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (!GizmoMath.TryProjectOnPlane(
                _dragOrigin,
                _dragPlaneDirectionU,
                _dragPlaneDirectionV,
                rayOrigin,
                rayDirection,
                out float alongU,
                out float alongV))
        {
            return;
        }

        int normalAxis = _dragHandle - AxisCount;

        Dragged?.Invoke(
            UnitAxis((normalAxis + 1) % AxisCount) * (alongU - _dragPlaneStartU)
            + UnitAxis((normalAxis + 2) % AxisCount) * (alongV - _dragPlaneStartV));
    }

    private void EndDrag()
    {
        if (!IsDragging)
            return;

        _dragHandle = -1;

        DragEnded?.Invoke();
    }

    /// <summary>
    /// Abandons the drag: Escape, or the tool or the target changing under it.
    /// Internal because Escape arrives from the shell, through the scene view —
    /// see <c>SceneView.TryAnswerEscape</c>; the key never reaches
    /// <see cref="HandleInput"/> while a handle is held.
    /// </summary>
    internal void CancelDrag()
    {
        if (!IsDragging)
            return;

        _dragHandle = -1;

        DragCancelled?.Invoke();
    }

    private void SetHovered(int handle)
    {
        if (_hoveredHandle == handle)
            return;

        _hoveredHandle = handle;

        for (var index = 0; index < HandleCount; index++)
        {
            bool hovered = index == handle;

            foreach (HandlePart part in _partsByHandleList[index])
                part.Instance.MaterialOverride = hovered ? part.Hovered : part.Normal;
        }
    }

    private void RebuildHandles()
    {
        for (var handle = 0; handle < HandleCount; handle++)
        {
            foreach (HandlePart part in _partsByHandleList[handle])
            {
                _handleNodes[handle].RemoveChild(part.Instance);
                part.Instance.QueueFree();
            }

            _partsByHandleList[handle].Clear();
        }

        for (var axis = 0; axis < AxisCount; axis++)
        {
            switch (_tool)
            {
                case ToolKind.Move:
                    AddAxisPart(axis, CreateShaft(ShaftStart, ArrowShaftEnd, ShaftRadius));
                    AddAxisPart(axis, CreateArrowHead(ArrowShaftEnd, HandleLength, ArrowHeadRadius));
                    AddPlaneParts(axis);
                    break;

                case ToolKind.Rotate:
                    AddAxisPart(axis, CreateRing());
                    break;

                case ToolKind.Scale:
                    AddAxisPart(axis, CreateShaft(ShaftStart, ScaleShaftEnd, ShaftRadius));
                    AddAxisPart(axis, CreateScaleCube(ScaleShaftEnd));
                    break;
            }
        }

        // The parts are new, so whatever was lit before is not.
        _hoveredHandle = -1;
    }

    private void AddAxisPart(int axis, MeshInstance3D instance) =>
        AddPart(axis, instance, _axisMaterials[axis], _hoverMaterial);

    /// <summary>
    /// The patch for the plane whose normal is this axis: a translucent fill to say
    /// which plane, and a solid border to say how far it reaches. Coloured by the
    /// normal, like the axis it belongs to — what the patch means is "the plane
    /// across X", and the border is the same red the X arrow wears.
    /// </summary>
    private void AddPlaneParts(int normalAxis)
    {
        int handle = AxisCount + normalAxis;
        Vector3 axisU = UnitAxis((normalAxis + 1) % AxisCount);
        Vector3 axisV = UnitAxis((normalAxis + 2) % AxisCount);

        AddPart(
            handle,
            CreatePlaneFill(axisU, axisV),
            _planeFillMaterials[normalAxis],
            _hoverFillMaterial);

        AddPart(
            handle,
            CreatePlaneBorder(axisU, axisV),
            _axisMaterials[normalAxis],
            _hoverMaterial);
    }

    private void AddPart(
        int handle,
        MeshInstance3D instance,
        StandardMaterial3D normal,
        StandardMaterial3D hovered)
    {
        instance.MaterialOverride = normal;
        instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        instance.Layers = RenderLayers.Voxels;

        _handleNodes[handle].AddChild(instance);
        _partsByHandleList[handle].Add(new HandlePart(instance, normal, hovered));
    }

    private void AddHandleNode(int handle, string name, Basis basis)
    {
        var node = new Node3D { Name = name, Basis = basis };

        _handleNodes[handle] = node;
        AddChild(node);
    }

    /// <summary>A cylinder along the axis node's own +Y, between two offsets.</summary>
    private static MeshInstance3D CreateShaft(float from, float to, float radius) => new()
    {
        Name = "Shaft",
        Mesh = new CylinderMesh
        {
            TopRadius = radius,
            BottomRadius = radius,
            Height = to - from,
            RadialSegments = 8,
            Rings = 0
        },
        Position = new Vector3(0f, (from + to) * 0.5f, 0f)
    };

    private static MeshInstance3D CreateArrowHead(float from, float to, float radius) => new()
    {
        Name = "Head",
        Mesh = new CylinderMesh
        {
            TopRadius = 0f,
            BottomRadius = radius,
            Height = to - from,
            RadialSegments = 12,
            Rings = 0
        },
        Position = new Vector3(0f, (from + to) * 0.5f, 0f)
    };

    private static MeshInstance3D CreateScaleCube(float at) => new()
    {
        Name = "Cube",
        Mesh = new BoxMesh { Size = Vector3.One * ScaleCubeSize },
        Position = new Vector3(0f, at + ScaleCubeSize * 0.5f, 0f)
    };

    /// <summary>
    /// The patch itself, as two triangles over the square its two axes cut out
    /// between <see cref="PlaneNear"/> and <see cref="PlaneFar"/>.
    /// </summary>
    private static MeshInstance3D CreatePlaneFill(Vector3 axisU, Vector3 axisV)
    {
        Vector3[] corners = GetPlaneCorners(axisU, axisV);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[]
        {
            corners[0], corners[1], corners[2],
            corners[0], corners[2], corners[3]
        };

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D { Name = "PlaneFill", Mesh = mesh };
    }

    private static MeshInstance3D CreatePlaneBorder(Vector3 axisU, Vector3 axisV)
    {
        Vector3[] corners = GetPlaneCorners(axisU, axisV);
        var points = new Vector3[corners.Length * 2];

        for (var index = 0; index < corners.Length; index++)
        {
            points[index * 2] = corners[index];
            points[index * 2 + 1] = corners[(index + 1) % corners.Length];
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

        return new MeshInstance3D { Name = "PlaneBorder", Mesh = mesh };
    }

    /// <summary>
    /// The square's four corners, in order round it — which is what lets the fill and
    /// the border be built from one function and so be the same square.
    /// </summary>
    private static Vector3[] GetPlaneCorners(Vector3 axisU, Vector3 axisV) => new[]
    {
        axisU * PlaneNear + axisV * PlaneNear,
        axisU * PlaneFar + axisV * PlaneNear,
        axisU * PlaneFar + axisV * PlaneFar,
        axisU * PlaneNear + axisV * PlaneFar
    };

    /// <summary>
    /// The turn circle, in the plane the axis node's +Y is normal to — drawn as a
    /// closed line loop for the same reason the selection box is: a ring of solid
    /// geometry would occlude the model it is meant to be a handle on.
    /// </summary>
    private static MeshInstance3D CreateRing()
    {
        var points = new Vector3[RingSegments * 2];

        for (var segment = 0; segment < RingSegments; segment++)
        {
            points[segment * 2] = RingPoint(segment);
            points[segment * 2 + 1] = RingPoint(segment + 1);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

        return new MeshInstance3D { Name = "Ring", Mesh = mesh };
    }

    private static Vector3 RingPoint(int segment)
    {
        float angle = Mathf.Tau * segment / RingSegments;

        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * RingRadius;
    }

    /// <summary>
    /// The frame an axis's handle is built in: its own +Y points along that axis,
    /// so one set of meshes serves all three. Written out as columns rather than as
    /// a rotation, because "which way does +Y end up" is the only thing that
    /// matters and this is where it can be read off.
    /// </summary>
    private static Basis CreateAxisBasis(int axis) => axis switch
    {
        0 => new Basis(new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f)),
        1 => Basis.Identity,
        _ => new Basis(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, -1f, 0f))
    };

    private static Vector3 UnitAxis(int axis) => axis switch
    {
        0 => new Vector3(1f, 0f, 0f),
        1 => new Vector3(0f, 1f, 0f),
        _ => new Vector3(0f, 0f, 1f)
    };

    private static Vector3 GetAxis(Basis basis, int axis) => axis switch
    {
        0 => basis.Column0,
        1 => basis.Column1,
        _ => basis.Column2
    };

    /// <summary>
    /// Editor chrome, not scene geometry: a handle has to be visible over whatever
    /// it is drawn on, so it answers to no light and loses to no depth. The same
    /// treatment the erase marker gets, and for the same reason — see the class
    /// remarks about the lighting model.
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

    /// <summary>
    /// A patch's fill: the handle material at a fraction of its opacity, and drawn
    /// from both sides.
    /// </summary>
    /// <remarks>
    /// Culling off is what a single-quad handle needs rather than a rule being bent
    /// to hide a winding bug — the patch is a two-sided sheet with no inside, and it
    /// is grabbed from whichever side of the plane the camera happens to be on. The
    /// alternative is emitting the same square twice wound both ways, which is the
    /// same picture with twice the geometry. The voxels keep the one real lighting
    /// and culling model; this is chrome, like the arrows it sits between.
    /// </remarks>
    private static StandardMaterial3D CreateFillMaterial(Color color)
    {
        StandardMaterial3D material = CreateMaterial(
            new Color(color.R, color.G, color.B, PlaneFillAlpha));

        material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

        return material;
    }

    /// <summary>
    /// One mesh of a handle with the two materials it wears — its own, and the one
    /// that says the pointer is on it. Kept per part rather than per handle because a
    /// patch is a translucent fill and a solid border, which cannot highlight through
    /// the same material.
    /// </summary>
    private readonly struct HandlePart
    {
        public HandlePart(MeshInstance3D instance, StandardMaterial3D normal, StandardMaterial3D hovered)
        {
            Instance = instance;
            Normal = normal;
            Hovered = hovered;
        }

        public MeshInstance3D Instance { get; }

        public StandardMaterial3D Normal { get; }

        public StandardMaterial3D Hovered { get; }
    }
}
