using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The turntable camera's arithmetic, kept out of the node so it can be checked
/// without an engine: how far a pan slides the focus point, and how far back the
/// camera has to sit for a target to fit on screen.
/// </summary>
internal static class CameraFraming
{
    /// <summary>
    /// How far the turntable may pitch either way, in radians — just short of
    /// straight up or down, where a look-at would gimbal-lock against Vector3.Up.
    /// </summary>
    /// <remarks>
    /// Here rather than in the camera node because two things need it now: the
    /// camera, which enforces it, and the view cube, which has to aim at a top
    /// view the camera will actually accept instead of the ±90° it would silently
    /// correct.
    /// </remarks>
    public const float PitchLimit = 1.55f;

    /// <summary>
    /// How much of the view's height a framed target is allowed to fill. The
    /// remainder is breathing room, so a framed model does not touch the edges.
    /// </summary>
    private const float FramingFill = 0.8f;

    /// <summary>
    /// Half a cell. A single voxel has a bounding sphere that small, and without
    /// a floor the camera would be asked to sit on top of it.
    /// </summary>
    private const float MinimumFitRadius = 0.5f;

    /// <summary>
    /// How many region diagonals the camera may retreat to. Anchored on the
    /// default 32³ scene, whose diagonal is 55.4: 2.2 of them is 122, which is
    /// what the zoom limit used to be as a constant. So the default scene keeps
    /// exactly the range it had, and every other scene now gets that range
    /// scaled to its own size instead of the 32³ one's.
    /// </summary>
    /// <remarks>
    /// A diagonal is the right unit because it is the worst case the camera has
    /// to cover: the focus point is caged inside the region, so with it pressed
    /// into a corner the far corner sits one full diagonal away. Fitting that on
    /// screen costs about 1.1 diagonals, which leaves the rest as headroom
    /// rather than as the whole budget.
    /// </remarks>
    private const float ZoomOutDiagonals = 2.2f;

    /// <summary>
    /// A floor under the zoom range, so a scene small enough to be measured in
    /// single cells can still be backed away from.
    /// </summary>
    private const float MinimumMaxDistance = 16f;

    /// <summary>
    /// How far past the reach the far plane is pushed: one cell, the smallest
    /// distance the document can express.
    /// </summary>
    /// <remarks>
    /// The plane has to sit <em>beyond</em> the furthest thing the camera can be
    /// pointed at, not on it. <see cref="CalculateViewDistance"/> is a tight
    /// bound and the worst case genuinely reaches it — focus point pressed into
    /// one corner, camera pulled all the way back, the opposite corner directly
    /// behind it — and geometry lying exactly on the far plane rasterizes to the
    /// cleared depth value, which under Forward+'s reversed depth loses its own
    /// depth test. So the room's far corner would wink out at full zoom instead
    /// of the whole far half being cut, which is a subtler version of the same
    /// bug. A cell is the unit everything else here is measured in, and it is
    /// four orders of magnitude above float epsilon at these distances.
    /// </remarks>
    private const float FarPlaneClearance = 1f;

    /// <summary>
    /// The fraction of a directional light's shadow distance at which Godot
    /// starts fading the shadows out. Godot's own default, restated here because
    /// <see cref="CalculateShadowDistance"/> divides by it — the light must be
    /// set to this same value or the budget it returns is wrong.
    /// </summary>
    public const float ShadowFadeStart = 0.8f;

    /// <summary>
    /// The shallowest view angle a pan still resolves at, as a sine — about 20°.
    /// </summary>
    /// <remarks>
    /// Sliding along the ground exactly under the cursor costs 1/sin(pitch)
    /// world units per pixel, which runs away to infinity as the view approaches
    /// level with the ground: at the horizon a pixel of travel means an unbounded
    /// slide. Bounding the gain caps that at ~2.9×. Above the threshold the pan
    /// is exact — the ground stays under the cursor; below it the ground slips,
    /// which is the honest failure for a mapping that genuinely has no answer
    /// there.
    /// </remarks>
    private const float GrazingSine = 0.35f;

    /// <summary>
    /// Where the focus point lands after a pan, given how far the cursor has
    /// travelled since the gesture began. Motion is horizontal at every view
    /// angle: the focus point's height is deliberately not something a pan can
    /// change.
    /// </summary>
    /// <param name="cursorTravel">Current cursor position minus the drag's start, in pixels.</param>
    /// <param name="yaw">Turntable yaw, radians.</param>
    /// <param name="pitch">
    /// Turntable pitch, radians. Negative looks down at the plane, positive up
    /// at it from below — and the two are not the same gesture, because the
    /// projection mirrors between them.
    /// </param>
    /// <param name="distance">Camera distance from the focus point.</param>
    /// <param name="verticalFovDegrees">The camera's vertical field of view.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    public static Vector3 CalculatePanOffset(
        Vector2 cursorTravel,
        float yaw,
        float pitch,
        float distance,
        float verticalFovDegrees,
        float viewportHeight)
    {
        // What one pixel of cursor travel is worth in world units at the focus
        // point's depth — the exact rate for a plane facing the camera, and the
        // rate the ground plane approaches as the view goes top-down.
        float worldPerPixel = 2f * distance
            * Mathf.Tan(Mathf.DegToRad(verticalFovDegrees) * 0.5f)
            / Mathf.Max(1f, viewportHeight);

        Vector3 right = GetHorizontalRight(yaw);
        Vector3 forward = GetHorizontalForward(yaw);

        // Only the magnitude is bounded. The sign is the ground plane's own,
        // and it flips when the camera passes under the focus point: seen from
        // below, the plane is projected mirrored, so a point further along
        // forward appears lower on screen rather than higher. Taking the
        // absolute value here — which this used to — made a pan from underneath
        // drag the world against the cursor while the arrow-key slide, which
        // reads no pitch at all, kept going the right way.
        float sine = Mathf.Sin(pitch);
        float forwardGain = (sine < 0f ? 1f : -1f) / Mathf.Max(Mathf.Abs(sine), GrazingSine);

        // The focus point moves against the cursor, so that the world appears to
        // follow it. Screen Y grows downwards, which is why only X is negated:
        // dragging down pulls the ground towards the viewer, and that is the rig
        // advancing along its own forward.
        return right * (-cursorTravel.X * worldPerPixel)
            + forward * (cursorTravel.Y * worldPerPixel * forwardGain);
    }

    /// <summary>
    /// The turntable's own ground-plane "right", used by both the mouse pan and
    /// the Shift+arrow strafe. Taken from the yaw rather than from the camera
    /// basis because the basis degenerates as the view approaches straight
    /// down, where its horizontal part vanishes — the yaw stays exact at every
    /// pitch. There is no matching tilt to consider: a turntable has no roll,
    /// so this vector never leaves the horizontal plane regardless of pitch.
    /// </summary>
    public static Vector3 GetHorizontalRight(float yaw) =>
        new(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));

    /// <summary>The ground-plane forward that pairs with <see cref="GetHorizontalRight"/>.</summary>
    public static Vector3 GetHorizontalForward(float yaw) =>
        new(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));

    /// <summary>
    /// A pitch brought inside the turntable's range, in radians. Silent, like every
    /// other clamp here: the answer is the nearest angle the camera will hold.
    /// </summary>
    public static float ClampPitch(float pitch) => Mathf.Clamp(pitch, -PitchLimit, PitchLimit);

    /// <summary>
    /// Where the camera stands relative to the focus point, as a unit vector — the
    /// Y-up turntable equivalent of Goxel's Z-up <c>camera_turntable</c>. Positive
    /// pitch puts it below the focus point, which is why looking down at a scene is
    /// a negative one.
    /// </summary>
    /// <remarks>
    /// Shared rather than repeated because the view cube shows the same turntable
    /// from outside: the widget turns its own camera by this, the scene camera
    /// places itself by this, and a copy of the formula in either would be a cube
    /// that slowly stops agreeing with the view it describes.
    /// </remarks>
    public static Vector3 GetOrbitOffset(float yaw, float pitch) =>
        new(
            Mathf.Cos(pitch) * Mathf.Sin(yaw),
            -Mathf.Sin(pitch),
            Mathf.Cos(pitch) * Mathf.Cos(yaw));

    /// <summary>
    /// The focus point confined to the buildable region. Per axis, so that a pan
    /// running into a wall slides along it instead of stopping dead.
    /// </summary>
    /// <remarks>
    /// The last clamp of its kind in the editor: element positions used to be
    /// leashed to the same box and no longer are. This one stays because it is
    /// about the camera rather than the document — a view orbiting a point outside
    /// the room is worse than an off-centre subject.
    /// </remarks>
    /// <remarks>
    /// Silent, like the model's own clamping: the answer is the nearest legal
    /// point rather than a refusal, so no caller needs bounds logic of its own.
    /// A pan is what makes the per-axis part matter, and it stays reversible
    /// because the gesture is measured from where it began rather than
    /// accumulated — dragging back off a wall returns immediately instead of
    /// having to undo the distance spent pressed against it.
    /// </remarks>
    public static Vector3 ClampToRegion(Vector3 point, Vector3 regionSize) =>
        new(
            Mathf.Clamp(point.X, 0f, regionSize.X),
            Mathf.Clamp(point.Y, 0f, regionSize.Y),
            Mathf.Clamp(point.Z, 0f, regionSize.Z));

    /// <summary>
    /// How far back the camera may be pulled in a region of this size. Derived
    /// rather than fixed: a constant limit is either a cage around a large scene
    /// or a licence to lose a small one somewhere in the distance, and the model
    /// puts no bound on how large a scene may be.
    /// </summary>
    public static float CalculateMaxDistance(Vector3 regionSize) =>
        Mathf.Max(MinimumMaxDistance, regionSize.Length() * ZoomOutDiagonals);

    /// <summary>
    /// The furthest anything in a region of this size can be from the camera:
    /// the camera pulled all the way back from a focus point pressed into one
    /// corner, with the opposite corner one further diagonal beyond it. Every
    /// depth budget the viewport has is measured from this one number.
    /// </summary>
    /// <remarks>
    /// A tight bound rather than a generous one, and both halves are exact: the
    /// zoom clamp puts the camera at most <see cref="CalculateMaxDistance"/>
    /// from the focus point, and <see cref="ClampToRegion"/> keeps that point
    /// inside the room, so nothing in the room is further than one diagonal
    /// past it.
    /// <para>
    /// It is the region rather than the model on purpose, and it is the same
    /// answer every other camera rule gives: the focus point is caged in the
    /// room and the zoom limit is the room's, so the room is exactly what the
    /// camera can be pointed at. Voxels placed outside it are already out of
    /// reach of framing — that is its own ledger entry, and it wants one fix,
    /// not a second copy of the question here.
    /// </para>
    /// </remarks>
    public static float CalculateViewDistance(Vector3 regionSize) =>
        CalculateMaxDistance(regionSize) + regionSize.Length();

    /// <summary>
    /// Where the camera's far plane goes in a region of this size.
    /// </summary>
    /// <remarks>
    /// Derived for the same reason the zoom limit and the shadow budget are, and
    /// it was the last of the three left as a literal: 500 units carried the
    /// default 32³ room and cut a 208x72x152 one clean in half, because the zoom
    /// limit there is 588 and the camera could simply retreat through its own
    /// far plane. The class rule is the one the shadow entry already stated —
    /// <b>every distance the viewport sees by is the room's answer, never a
    /// constant</b> — so all three now come off <see cref="CalculateViewDistance"/>
    /// and cannot drift apart.
    /// </remarks>
    public static float CalculateFarPlane(Vector3 regionSize) =>
        CalculateViewDistance(regionSize) + FarPlaneClearance;

    /// <summary>
    /// How far a directional light must carry its shadows in a region of this
    /// size, so that nothing the camera can reach falls outside them.
    /// </summary>
    /// <remarks>
    /// Godot budgets a directional shadow over a distance measured from the
    /// camera, fades it out over the last stretch of that budget, and defaults
    /// the budget to 100 — a number about walking around a level, not about
    /// orbiting a room. Left at the default, backing away from a scene bigger
    /// than the budget makes every shadow thin out and then vanish, which reads
    /// as the shadows being broken rather than as a distance being exceeded.
    /// <para>
    /// So the budget comes from the same place the zoom limit does: the reach
    /// has to land before the fade begins rather than inside it, hence the
    /// division. Derived once per region and not per frame, deliberately: a
    /// budget that followed the camera would keep the shadow sharpest at every
    /// zoom and would therefore change as the camera moved, which is the very
    /// thing being fixed.
    /// </para>
    /// </remarks>
    public static float CalculateShadowDistance(Vector3 regionSize) =>
        CalculateViewDistance(regionSize) / ShadowFadeStart;

    /// <summary>
    /// How far the camera must sit from a target's centre for the whole target
    /// to fit on screen, whichever of the two axes is the tighter one.
    /// </summary>
    public static float CalculateFitDistance(
        Aabb bounds,
        float verticalFovDegrees,
        float aspect)
    {
        // A sphere rather than the box itself: the framing then holds however the
        // turntable is turned, instead of only from the angle it was asked at.
        float radius = Mathf.Max(bounds.Size.Length() * 0.5f, MinimumFitRadius) / FramingFill;

        float verticalHalfFov = Mathf.DegToRad(verticalFovDegrees) * 0.5f;
        float horizontalHalfFov = Mathf.Atan(Mathf.Tan(verticalHalfFov) * Mathf.Max(0.01f, aspect));

        return Mathf.Max(
            radius / Mathf.Sin(verticalHalfFov),
            radius / Mathf.Sin(horizontalHalfFov));
    }
}
