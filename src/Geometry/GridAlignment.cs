using Godot;

namespace VoxelCAD.Geometry;

/// <summary>
/// Whether a world transform puts an element's cells on the world's own cell
/// grid: same size, same orientation, same phase.
/// </summary>
/// <remarks>
/// Three layers ask this and they must not answer it differently. The exporter
/// asks it to decide whether a volume can be baked exactly; the viewport asks it
/// to decide whether the region's walls are a surface the brush can place
/// against; the command surface reports it, because it changes what an agent's
/// next click can do. That is why this lives in an assembly of its own under both
/// View and Model rather than being copied into each.
/// </remarks>
public static class GridAlignment
{
    /// <summary>How far a basis axis or an origin may sit from exact.</summary>
    /// <remarks>
    /// Deliberately loose. A right angle is not exact in floating point to begin
    /// with — Godot's cosine of 90° is -4.4e-8 — and a transform composed through
    /// several parents accumulates from there, so a tight bound would call a
    /// genuinely aligned element misaligned after a reparent. The two mistakes are
    /// not symmetric: a false "misaligned" takes an affordance away from a user who
    /// did nothing wrong, while a false "aligned" places a cell a ten-thousandth of
    /// a cell off, which nothing can see and no stored value records.
    /// </remarks>
    public const float Epsilon = 1e-4f;

    /// <summary>
    /// True when the transform maps the unit cell grid onto the world's: every
    /// basis column a signed unit axis, the three of them distinct, and the origin
    /// whole.
    /// </summary>
    /// <remarks>
    /// Rotations by multiples of 90° pass by construction — those matrices are
    /// exactly the signed permutations — so this needs no special case for them,
    /// and it is the reason to ask about the lattice rather than about the angles.
    /// A scale other than ±1 fails: the cells would be a different size from the
    /// world's, which is the sense in which the two grids do not coincide.
    /// </remarks>
    public static bool IsAligned(Transform3D transform)
    {
        return TryGetAxis(transform.Basis.X, out int x)
            && TryGetAxis(transform.Basis.Y, out int y)
            && TryGetAxis(transform.Basis.Z, out int z)
            && x != y && y != z && x != z
            && IsWhole(transform.Origin.X)
            && IsWhole(transform.Origin.Y)
            && IsWhole(transform.Origin.Z);
    }

    /// <summary>
    /// True when a vector is a signed unit coordinate axis — exactly one component
    /// is ±1 and the rest are zero, within <see cref="Epsilon"/> — and reports
    /// which axis. This is the whole of "axis-aligned and unit-scaled".
    /// </summary>
    public static bool TryGetAxis(Vector3 vector, out int axis)
    {
        axis = -1;

        for (var i = 0; i < 3; i++)
        {
            float component = vector[i];

            if (Mathf.Abs(Mathf.Abs(component) - 1f) < Epsilon)
                axis = i;
            else if (Mathf.Abs(component) > Epsilon)
                return false;
        }

        return axis >= 0;
    }

    private static bool IsWhole(float value) =>
        Mathf.Abs(value - Mathf.Round(value)) < Epsilon;
}
