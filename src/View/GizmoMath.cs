using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The arithmetic behind the transform handles: where a pointer ray lands on an
/// axis, whether it touches a handle, and what angle it makes around one.
/// </summary>
/// <remarks>
/// Pure functions over vectors, deliberately separate from the node that draws
/// the handles — the same split <see cref="CameraFraming"/> and
/// <see cref="VoxelPicker"/> already make, and for the same reason: this is where
/// a sign error hides, and it is testable without an engine.
/// <para>
/// Handles are hit analytically rather than with colliders, exactly as voxels
/// are. There is nothing to keep in step with the meshes because both are derived
/// from the same handful of constants.
/// </para>
/// </remarks>
internal static class GizmoMath
{
    private const float Epsilon = 1e-6f;

    /// <summary>
    /// Where the pointer ray lands along a line, as the multiplier of
    /// <paramref name="lineDirection"/> from <paramref name="lineOrigin"/>.
    /// </summary>
    /// <remarks>
    /// The direction is deliberately not normalized: passing the parent frame's
    /// own axis vector makes the answer come back in the units that frame measures
    /// in — which is what an element's position is expressed in — so a scaled
    /// parent needs no correction anywhere else.
    /// <para>
    /// False when the ray is parallel to the line, where "where along it" has no
    /// answer at all.
    /// </para>
    /// </remarks>
    public static bool TryProjectOnAxis(
        Vector3 lineOrigin,
        Vector3 lineDirection,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        out float parameter)
    {
        parameter = 0f;

        Vector3 between = lineOrigin - rayOrigin;

        float lineLine = lineDirection.Dot(lineDirection);
        float lineRay = lineDirection.Dot(rayDirection);
        float rayRay = rayDirection.Dot(rayDirection);
        float lineBetween = lineDirection.Dot(between);
        float rayBetween = rayDirection.Dot(between);

        float denominator = lineLine * rayRay - lineRay * lineRay;

        if (Mathf.Abs(denominator) < Epsilon)
            return false;

        parameter = (lineRay * rayBetween - rayRay * lineBetween) / denominator;

        return true;
    }

    /// <summary>
    /// Where the pointer ray lands on the plane two directions span, as the pair of
    /// multipliers of those directions from <paramref name="planeOrigin"/>.
    /// </summary>
    /// <remarks>
    /// The two-axis counterpart of <see cref="TryProjectOnAxis"/>, and unnormalized
    /// on the same terms: pass the parent frame's own axis vectors and the answer
    /// comes back in the units that frame measures in, one component per channel the
    /// drag edits.
    /// <para>
    /// Solved as the pair that reproduces the offset — not as two independent
    /// projections — so a skewed parent, whose axes are genuinely not
    /// perpendicular, still gets the coordinates that add back up to where the
    /// pointer is.
    /// </para>
    /// <para>
    /// False when the two directions are parallel (they span no plane) or when the
    /// ray runs along it, where "where on it" has no answer.
    /// </para>
    /// </remarks>
    public static bool TryProjectOnPlane(
        Vector3 planeOrigin,
        Vector3 directionU,
        Vector3 directionV,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        out float alongU,
        out float alongV)
    {
        alongU = 0f;
        alongV = 0f;

        if (!TryIntersectSpannedPlane(
                planeOrigin,
                directionU,
                directionV,
                rayOrigin,
                rayDirection,
                out Vector3 point,
                out _))
        {
            return false;
        }

        return TrySolvePlaneCoordinates(point - planeOrigin, directionU, directionV, out alongU, out alongV);
    }

    /// <summary>
    /// Whether the pointer ray lands inside the square patch two directions span
    /// between <paramref name="near"/> and <paramref name="far"/> along each of
    /// them, and how far along the ray it did.
    /// </summary>
    /// <remarks>
    /// Bounds are in the same units as <see cref="TryProjectOnPlane"/> answers in —
    /// multiples of the directions given — so passing the very columns the quad is
    /// drawn from makes the region tested and the parallelogram drawn the same
    /// shape, skew and all, with nothing to keep in step.
    /// </remarks>
    public static bool TryHitQuad(
        Vector3 origin,
        Vector3 directionU,
        Vector3 directionV,
        float near,
        float far,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        out float rayDistance)
    {
        if (!TryIntersectSpannedPlane(
                origin,
                directionU,
                directionV,
                rayOrigin,
                rayDirection,
                out Vector3 point,
                out rayDistance))
        {
            return false;
        }

        if (!TrySolvePlaneCoordinates(
                point - origin,
                directionU,
                directionV,
                out float alongU,
                out float alongV))
        {
            return false;
        }

        return alongU >= near && alongU <= far && alongV >= near && alongV <= far;
    }

    /// <summary>
    /// Whether the plane two directions span is turned far enough towards the eye to
    /// be worth aiming at: the cosine between its normal and the direction the eye
    /// looks at <paramref name="origin"/> along, against a floor.
    /// </summary>
    /// <remarks>
    /// A patch seen nearly edge-on is a line on screen — unclickable, and worse,
    /// laid over the shafts it sits between, where it would take clicks meant for
    /// them. This is what lets it be hidden and skipped instead, and it is asked of
    /// the same arithmetic on both counts so the visible set and the hittable set
    /// cannot disagree.
    /// </remarks>
    public static bool IsPlaneFacing(
        Vector3 origin,
        Vector3 directionU,
        Vector3 directionV,
        Vector3 eye,
        float minimumCosine)
    {
        Vector3 normal = directionU.Cross(directionV);
        Vector3 towardsEye = eye - origin;

        if (normal.LengthSquared() < Epsilon || towardsEye.LengthSquared() < Epsilon)
            return false;

        return Mathf.Abs(normal.Normalized().Dot(towardsEye.Normalized())) >= minimumCosine;
    }

    /// <summary>
    /// Whether the pointer ray passes within <paramref name="radius"/> of the
    /// segment, and how far along the ray it did — the depth that decides which of
    /// two overlapping handles was meant.
    /// </summary>
    /// <remarks>
    /// The segment is clamped after projecting rather than solved as a constrained
    /// pair, which is the usual approximation for picking something thin: it can
    /// only differ near the very ends, where the two answers are within the
    /// handle's own radius of each other anyway.
    /// </remarks>
    public static bool TryHitSegment(
        Vector3 start,
        Vector3 end,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        float radius,
        out float rayDistance)
    {
        rayDistance = 0f;

        Vector3 segment = end - start;

        if (!TryProjectOnAxis(start, segment, rayOrigin, rayDirection, out float parameter))
            return false;

        Vector3 onSegment = start + segment * Mathf.Clamp(parameter, 0f, 1f);

        rayDistance = (onSegment - rayOrigin).Dot(rayDirection);

        if (rayDistance <= 0f)
            return false;

        return (rayOrigin + rayDirection * rayDistance).DistanceTo(onSegment) <= radius;
    }

    /// <summary>
    /// Whether the pointer ray crosses the ring's plane within
    /// <paramref name="tolerance"/> of its circumference, and how far along the ray
    /// it did.
    /// </summary>
    public static bool TryHitRing(
        Vector3 center,
        Vector3 normal,
        float radius,
        float tolerance,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        out float rayDistance)
    {
        rayDistance = 0f;

        if (!TryIntersectPlane(center, normal, rayOrigin, rayDirection, out Vector3 point, out rayDistance))
            return false;

        float distance = point.DistanceTo(center);

        return distance >= radius - tolerance && distance <= radius + tolerance;
    }

    /// <summary>
    /// The angle the pointer ray makes around <paramref name="normal"/>, measured
    /// from <paramref name="axisU"/> towards <paramref name="axisV"/>.
    /// </summary>
    /// <remarks>
    /// Right-handed when the caller passes the frame's own axes in order — u × v =
    /// normal — which is what makes a growing angle here the same direction as a
    /// growing rotation value on that channel.
    /// </remarks>
    public static bool TryAngleOnPlane(
        Vector3 center,
        Vector3 normal,
        Vector3 axisU,
        Vector3 axisV,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        out float radians)
    {
        radians = 0f;

        if (!TryIntersectPlane(center, normal, rayOrigin, rayDirection, out Vector3 point, out _))
            return false;

        Vector3 offset = point - center;

        if (offset.LengthSquared() < Epsilon)
            return false;

        radians = Mathf.Atan2(offset.Dot(axisV), offset.Dot(axisU));

        return true;
    }

    /// <summary>
    /// One step of a turn, brought within half a turn either way. What keeps a
    /// rotation drag accumulating through the point where the measured angle
    /// wraps, instead of jumping a whole turn the other way.
    /// </summary>
    public static float WrapRadians(float radians)
    {
        float wrapped = Mathf.PosMod(radians + Mathf.Pi, Mathf.Tau);

        return wrapped - Mathf.Pi;
    }

    /// <summary>
    /// Where the ray crosses the plane two directions span, with the degenerate
    /// pair — parallel directions, spanning nothing — refused before the plane is
    /// formed rather than producing a zero normal to divide by.
    /// </summary>
    private static bool TryIntersectSpannedPlane(
        Vector3 origin,
        Vector3 directionU,
        Vector3 directionV,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        out Vector3 point,
        out float rayDistance)
    {
        point = default;
        rayDistance = 0f;

        Vector3 normal = directionU.Cross(directionV);

        if (normal.LengthSquared() < Epsilon)
            return false;

        return TryIntersectPlane(
            origin,
            normal.Normalized(),
            rayOrigin,
            rayDirection,
            out point,
            out rayDistance);
    }

    /// <summary>
    /// The pair (u, v) with <c>u·directionU + v·directionV = offset</c>, for an
    /// offset already known to lie in their plane. The 2×2 Gram system rather than
    /// two dot products, which agree only where the directions are perpendicular.
    /// </summary>
    private static bool TrySolvePlaneCoordinates(
        Vector3 offset,
        Vector3 directionU,
        Vector3 directionV,
        out float alongU,
        out float alongV)
    {
        alongU = 0f;
        alongV = 0f;

        float uu = directionU.Dot(directionU);
        float vv = directionV.Dot(directionV);
        float uv = directionU.Dot(directionV);
        float uOffset = directionU.Dot(offset);
        float vOffset = directionV.Dot(offset);

        float determinant = uu * vv - uv * uv;

        if (Mathf.Abs(determinant) < Epsilon)
            return false;

        alongU = (uOffset * vv - vOffset * uv) / determinant;
        alongV = (vOffset * uu - uOffset * uv) / determinant;

        return true;
    }

    private static bool TryIntersectPlane(
        Vector3 center,
        Vector3 normal,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        out Vector3 point,
        out float rayDistance)
    {
        point = default;
        rayDistance = 0f;

        float slope = normal.Dot(rayDirection);

        // Edge on: the plane is a line from here, and everything on it is equally
        // close to the ray.
        if (Mathf.Abs(slope) < Epsilon)
            return false;

        rayDistance = normal.Dot(center - rayOrigin) / slope;

        if (rayDistance <= 0f)
            return false;

        point = rayOrigin + rayDirection * rayDistance;

        return true;
    }
}
