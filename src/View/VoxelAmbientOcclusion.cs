using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// How much darker a face corner is for the cells crowding it — Goxel's ambient
/// occlusion, which is what gives a voxel model its soft creases where two
/// surfaces meet.
/// </summary>
/// <remarks>
/// The occluders of a corner are the three cells touching it in the plane one
/// step along the face normal: the two across the corner's edges, and the
/// diagonal between them. Goxel ORs exactly those three
/// (<c>block_get_vertice_border</c> in <c>volume_to_vertices.c</c>), so the value
/// AT a corner is binary — fully darkened if anything is there, untouched
/// otherwise — and all the softness is the falloff across the face.
/// <para>
/// Goxel spreads that falloff as <c>sqrt(distance to the occluder)</c>, sampled
/// per pixel out of a 256-tile atlas. Here the four corner values are handed to
/// the vertex colours instead and the rasterizer interpolates them, so the ramp
/// is linear where Goxel's curves. That is the one visible difference, it is
/// confined to the middle of a face, and it costs nothing per frame; a shader
/// reproducing the exact field is the fallback if it ever reads too heavy.
/// </para>
/// <para>
/// A cell occludes when it is filled, whatever its material's opacity. Goxel
/// asks for alpha over half instead; "filled occludes" is the rule that needs no
/// second answer when a material's colour changes.
/// </para>
/// </remarks>
internal static class VoxelAmbientOcclusion
{
    /// <summary>
    /// Goxel's own default (<c>occlusion_strength</c> in <c>goxel_init</c>): a
    /// fully crowded corner keeps 60% of its light, never less.
    /// </summary>
    public const float OcclusionStrength = 0.4f;

    private const float OccludedShade = 1f - OcclusionStrength;

    /// <summary>
    /// The 3x3x3 occupancy around a cell as one bit per offset, indexed
    /// <c>(dx + 1) + (dy + 1) * 3 + (dz + 1) * 9</c>. Read once per cell and
    /// reused by all six of its faces: twenty-six lookups beat four corners times
    /// three neighbours on every face the cell shows.
    /// </summary>
    public static uint GetNeighborhood(IReadOnlyDictionary<Vector3I, Guid> voxels, Vector3I cell)
    {
        uint neighborhood = 0;
        var bit = 0;

        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (voxels.ContainsKey(cell + new Vector3I(dx, dy, dz)))
                        neighborhood |= 1u << bit;

                    bit++;
                }
            }
        }

        return neighborhood;
    }

    /// <summary>
    /// The shade at one corner of one face: 1 when nothing crowds it, else
    /// <c>1 - OcclusionStrength</c>. <paramref name="corner"/> is the unit-cube
    /// corner offset, so each component is 0 or 1 and says which side of the face
    /// the corner sits on.
    /// </summary>
    public static float GetCornerShade(uint neighborhood, Vector3I normal, Vector3I corner)
    {
        Vector3I first = GetTangent(normal, corner, 0);
        Vector3I second = GetTangent(normal, corner, 1);

        bool occluded = IsFilled(neighborhood, normal + first)
            || IsFilled(neighborhood, normal + second)
            || IsFilled(neighborhood, normal + first + second);

        return occluded ? OccludedShade : 1f;
    }

    /// <summary>
    /// The same shade weakened by how solid the surface carrying it is: unchanged
    /// on an opaque material, gone entirely on a fully clear one, and proportional
    /// in between.
    /// </summary>
    /// <remarks>
    /// Occlusion darkens the albedo, and on a pane you look *through* a darkened
    /// albedo does not read as less light reaching a crevice — it reads as dirt on
    /// the glass, because the eye attributes what it sees to the surface rather
    /// than to the fold behind it. Weakening it with the opacity is the one rule
    /// that says so without a threshold anywhere: an opaque model is bit-for-bit
    /// what it always was, so nothing that has ever been looked at moves.
    /// <para>
    /// Baked rather than applied in the material, because <c>StandardMaterial3D</c>
    /// multiplies by the vertex colour or does not, with nothing in between — and
    /// because the mesh is already rebuilt whenever a material's colour changes,
    /// which it must be: the opacity decides which faces exist at all.
    /// </para>
    /// </remarks>
    public static float FadeShade(float shade, float opacity) =>
        1f - (1f - shade) * opacity;

    /// <summary>
    /// Whether a quad's two triangles should meet along B-D rather than the
    /// default A-C.
    /// </summary>
    /// <remarks>
    /// A quad interpolates its four corners across two triangles, and which pair
    /// the seam joins decides what the gradient looks like. Split through the
    /// darker pair and a face with one shaded corner creases along the seam
    /// instead of fading across it; split through the lighter pair and the shading
    /// spreads from the dark corner in both directions, which is what a corner
    /// shadow looks like.
    /// </remarks>
    public static bool ShouldFlipDiagonal(float shadeA, float shadeB, float shadeC, float shadeD) =>
        shadeA + shadeC > shadeB + shadeD;

    /// <summary>
    /// One of the two in-plane directions from the face's centre towards
    /// <paramref name="corner"/>, taken in cyclic axis order after the normal's.
    /// </summary>
    private static Vector3I GetTangent(Vector3I normal, Vector3I corner, int which)
    {
        int normalAxis = GetNormalAxis(normal);
        int axis = (normalAxis + 1 + which) % 3;
        int sign = GetAxis(corner, axis) == 1 ? 1 : -1;

        return GetAxisUnit(axis) * sign;
    }

    private static bool IsFilled(uint neighborhood, Vector3I offset) =>
        (neighborhood & (1u << ((offset.X + 1) + (offset.Y + 1) * 3 + (offset.Z + 1) * 9))) != 0;

    private static int GetNormalAxis(Vector3I normal)
    {
        if (normal.X != 0)
            return 0;

        if (normal.Y != 0)
            return 1;

        if (normal.Z != 0)
            return 2;

        throw new ArgumentOutOfRangeException(
            nameof(normal), normal, "A face normal must point along an axis.");
    }

    private static int GetAxis(Vector3I vector, int axis) =>
        axis switch
        {
            0 => vector.X,
            1 => vector.Y,
            2 => vector.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };

    private static Vector3I GetAxisUnit(int axis) =>
        axis switch
        {
            0 => new Vector3I(1, 0, 0),
            1 => new Vector3I(0, 1, 0),
            2 => new Vector3I(0, 0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };
}
