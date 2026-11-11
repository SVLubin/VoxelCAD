using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Conversion between the document's transform values and Godot's matrix form,
/// needed whenever transforms have to be composed (world transforms, reparenting).
/// </summary>
/// <remarks>
/// Euler angles use Godot's default YXZ order, matching <see cref="Node3D"/> — the
/// view applies these degrees to a node directly, so the two must agree.
/// Public because the export projection composes world transforms with the very
/// same conversion; a second copy could drift from this Euler order and quietly
/// mis-place baked geometry.
/// </remarks>
public static class TransformHelper
{
    /// <summary>
    /// Scale is applied in the element's own frame — rotation first, then scale
    /// along the rotated axes, which is exactly what <see cref="Node3D"/> composes
    /// from the same three values.
    /// </summary>
    /// <remarks>
    /// <c>Basis.Scaled</c> is the wrong one here, and its name does not say so: it
    /// scales in the parent frame (<c>diag(s) * R</c>). A Y-rotated element with
    /// scale (1, 2, 3) therefore came out stretched along the world's axes here
    /// and along its own in the viewport — the document and the picture
    /// disagreeing about the same element. <c>ScaledLocal</c> is <c>R * diag(s)</c>,
    /// which is the node's.
    /// </remarks>
    /// <remarks>
    /// This overload reads <c>values.Position</c> as the <b>local origin</b> — the
    /// model's stored form. Everything that crosses a public seam speaks the pivot
    /// form instead; use the overload below there, or the two frames drift apart
    /// by exactly <c>basis · pivot</c>.
    /// </remarks>
    public static Transform3D ToTransform(TransformValues values)
    {
        Basis basis = Basis.FromEuler(ToRadians(values.RotationDegrees))
            .ScaledLocal(values.Scale);

        return new Transform3D(basis, values.Position);
    }

    /// <summary>
    /// The composition for values whose <c>Position</c> names where the element's
    /// <b>pivot</b> stands in the parent frame — the public meaning of the channel.
    /// The frame is <c>T(pos) · R · S · T(−p)</c>: the local origin lands at
    /// <c>pos − basis · pivot</c>, so the point at local <paramref name="pivot"/>
    /// lands exactly at <c>pos</c>.
    /// </summary>
    public static Transform3D ToTransform(TransformValues values, Vector3 pivot)
    {
        Basis basis = Basis.FromEuler(ToRadians(values.RotationDegrees))
            .ScaledLocal(values.Scale);

        return new Transform3D(basis, values.Position - basis * pivot);
    }

    /// <summary>Splits a matrix back into the three values, inverting <see cref="ToTransform"/>.</summary>
    /// <remarks>
    /// The rotation has to be read off an orthonormal basis. Godot's
    /// <c>GetEuler</c> reads the matrix entries directly and takes them for a
    /// rotation, so on a scaled basis it returns angles that are not merely
    /// imprecise but wrong: (30, 40, 50)° with scale (1, 2, 3) came back as
    /// (90, -45.8, 0)°, which is how a reparent could move an element's geometry.
    /// <c>GetRotationQuaternion</c> normalises the basis, and fixes the sign of a
    /// mirrored one, before the angles are read — which is the whole of what was
    /// missing.
    /// </remarks>
    public static TransformValues ToValues(Transform3D transform)
    {
        var rotation = new Basis(transform.Basis.GetRotationQuaternion());

        return new TransformValues(
            transform.Origin,
            ToDegrees(rotation.GetEuler()),
            transform.Basis.Scale);
    }

    /// <summary>Inverts the pivot overload: <c>Position</c> comes back as where the pivot stands.</summary>
    public static TransformValues ToValues(Transform3D transform, Vector3 pivot)
    {
        TransformValues origin = ToValues(transform);

        return origin with { Position = transform.Origin + transform.Basis * pivot };
    }

    private static Vector3 ToRadians(Vector3 degrees) =>
        new(Mathf.DegToRad(degrees.X), Mathf.DegToRad(degrees.Y), Mathf.DegToRad(degrees.Z));

    private static Vector3 ToDegrees(Vector3 radians) =>
        new(Mathf.RadToDeg(radians.X), Mathf.RadToDeg(radians.Y), Mathf.RadToDeg(radians.Z));
}
