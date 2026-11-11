using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The view cube's arithmetic, kept out of the node so it can be checked without
/// an engine: which of the 26 zones a ray through the widget hits, and which way
/// the camera has to look to be standing in a zone's direction.
/// </summary>
/// <remarks>
/// A zone is a <see cref="Vector3I"/> whose components are each -1, 0 or +1 and
/// which is not all zero: 6 faces (one non-zero component), 12 edges (two) and 8
/// corners (three). That is the whole table — every pose below is derived from the
/// zone's own direction vector rather than written out 26 times, so a face, an edge
/// and a corner cannot disagree about what "look from there" means.
/// <para>
/// The cube it measures is the unit cube at the origin, axis-aligned. The widget
/// keeps it that way and turns its own camera instead, which is what lets a ray
/// arrive here needing no transform at all.
/// </para>
/// <para>
/// Public, unlike the rest of the widget, and that is the point rather than an
/// oversight: the <c>view</c> command answers with the pose this returns, so the
/// mouse and the dispatcher cannot come to disagree about where "front" is. It is
/// pure functions over arithmetic types, so nothing about the view leaks out with
/// it.
/// </para>
/// </remarks>
public static class ViewCubeGeometry
{
    /// <summary>Half the cube's edge, so the faces sit at ±this on each axis.</summary>
    public const float HalfExtent = 0.5f;

    /// <summary>
    /// Where the middle band of a face's 3×3 division ends. A third of the face
    /// either side of its centre.
    /// </summary>
    private const float BandEdge = HalfExtent / 3f;

    /// <summary>
    /// Below this a ray counts as parallel to a pair of faces. Small enough that a
    /// genuine near-miss still resolves, large enough that the reciprocal below
    /// cannot overflow.
    /// </summary>
    private const float ParallelEpsilon = 1e-6f;

    /// <summary>
    /// Which zone a ray hits, in the cube's own space, or false for a ray that
    /// misses it entirely.
    /// </summary>
    /// <remarks>
    /// Slab intersection, which gives both the entry distance and — as the axis
    /// that produced it — the face entered, so the face never has to be recovered
    /// by comparing the hit point against three extents.
    /// <para>
    /// A ray starting inside the cube is a miss: the widget's camera is always
    /// outside, and answering "the face behind you" for a ray that never entered
    /// through one would be inventing a hit.
    /// </para>
    /// </remarks>
    public static bool TryPick(Vector3 rayOrigin, Vector3 rayDirection, out Vector3I zone)
    {
        zone = Vector3I.Zero;

        var entryAxis = -1;
        var near = 0f;
        float far = float.MaxValue;

        for (var axis = 0; axis < 3; axis++)
        {
            float origin = GetComponent(rayOrigin, axis);
            float direction = GetComponent(rayDirection, axis);

            if (Mathf.Abs(direction) < ParallelEpsilon)
            {
                // Parallel to this pair of faces: either between them for the
                // ray's whole length, or outside them for all of it.
                if (Mathf.Abs(origin) > HalfExtent)
                    return false;

                continue;
            }

            float inverse = 1f / direction;
            float first = (-HalfExtent - origin) * inverse;
            float second = (HalfExtent - origin) * inverse;

            if (first > second)
                (first, second) = (second, first);

            if (first > near)
            {
                near = first;
                entryAxis = axis;
            }

            far = Mathf.Min(far, second);

            if (near > far)
                return false;
        }

        if (entryAxis < 0)
            return false;

        Vector3 hit = rayOrigin + rayDirection * near;
        int sign = GetComponent(hit, entryAxis) < 0f ? -1 : 1;

        // The entered face fixes one component; the other two are which third of
        // that face the hit landed in.
        zone = entryAxis switch
        {
            0 => new Vector3I(sign, GetBand(hit.Y), GetBand(hit.Z)),
            1 => new Vector3I(GetBand(hit.X), sign, GetBand(hit.Z)),
            _ => new Vector3I(GetBand(hit.X), GetBand(hit.Y), sign)
        };

        return true;
    }

    /// <summary>
    /// Where a zone stands, as a unit vector from the cube's centre. The camera
    /// direction a click on it asks for, and the thing every other answer here is
    /// derived from.
    /// </summary>
    public static Vector3 GetDirection(Vector3I zone) =>
        new Vector3(zone.X, zone.Y, zone.Z).Normalized();

    /// <summary>
    /// The pose that looks at the scene from <paramref name="zone"/>: the current
    /// one with its two angles replaced.
    /// </summary>
    /// <remarks>
    /// Built from <paramref name="current"/> with <c>with</c> rather than
    /// assembled from scratch, so the focus point and the distance are carried
    /// across by construction. Turning to a standard view is not a request to move
    /// the point the camera turns around, nor to change how far away it is, and
    /// this is the one place that could have got it wrong.
    /// <para>
    /// Straight up and straight down keep the yaw they arrived with: those two
    /// directions have no yaw of their own — <c>atan2(0, 0)</c> is an arbitrary
    /// answer — and spinning the model on the way to a top view is a turn nobody
    /// asked for. Their pitch is the turntable's limit rather than ±90°, because
    /// that is the value the camera will actually hold: it refuses to gimbal-lock
    /// against <c>Vector3.Up</c>, so aiming at 90° would silently land somewhere
    /// else and an agent reading the pose back would see a number it did not ask
    /// for.
    /// </para>
    /// </remarks>
    public static CameraPose GetPose(CameraPose current, Vector3I zone)
    {
        Vector3 direction = GetDirection(zone);

        // The camera sits at -sin(pitch) above the pivot, so looking down from
        // above is a negative pitch. See OrbitCamera.ApplyTransform.
        float pitch = CameraFraming.ClampPitch(-Mathf.Asin(Mathf.Clamp(direction.Y, -1f, 1f)));

        float yaw = zone.X == 0 && zone.Z == 0
            ? Mathf.DegToRad(current.YawDegrees)
            : Mathf.Atan2(direction.X, direction.Z);

        return current with
        {
            YawDegrees = Mathf.RadToDeg(yaw),
            PitchDegrees = Mathf.RadToDeg(pitch)
        };
    }

    /// <summary>Whether a triple names one of the 26 zones rather than the cube's centre.</summary>
    public static bool IsZone(Vector3I zone) =>
        zone != Vector3I.Zero
        && IsBand(zone.X)
        && IsBand(zone.Y)
        && IsBand(zone.Z);

    /// <summary>
    /// Which third of a face an offset from its centre falls in.
    /// </summary>
    /// <remarks>
    /// The middle band owns both its boundaries, so a ray landing exactly on one
    /// resolves towards the face rather than towards the edge or corner beside it.
    /// A face is the biggest target and by far the most-wanted, so that is where an
    /// ambiguity should fall — and it is pinned by a test rather than left to
    /// whichever comparison happened to be written first.
    /// </remarks>
    private static int GetBand(float value) =>
        value < -BandEdge ? -1 : value > BandEdge ? 1 : 0;

    private static bool IsBand(int component) => component is -1 or 0 or 1;

    private static float GetComponent(Vector3 vector, int axis) => axis switch
    {
        0 => vector.X,
        1 => vector.Y,
        _ => vector.Z
    };
}
