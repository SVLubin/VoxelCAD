using Godot;

namespace VoxelCAD.Render;

/// <summary>
/// One voxel model as the tracer sees it: a dense grid of material indices in the
/// element's own cells, and the transform that puts those cells in the world.
/// </summary>
/// <remarks>
/// The grid stays in the element's own frame rather than being baked into world
/// cells, which is what lets a rotated or scaled element be traced exactly: the
/// ray is carried into the element's frame and marched there. Baking into a shared
/// world grid is what forces the exporter's <c>UnbakeableElements</c> hole, and
/// this side does not have to have it.
/// </remarks>
/// <param name="WorldToLocal">
/// The inverse of the element's world transform. Applied to the ray's direction
/// <b>unnormalised</b>, so the ray parameter stays world distance and hits from
/// differently scaled elements can be compared against each other.
/// </param>
/// <param name="GridMin">The lowest filled cell, so a grid index is cell - GridMin.</param>
/// <param name="GridSize">Cells per axis of the dense grid.</param>
/// <param name="VoxelOffset">Where this element's cells start in the shared voxel buffer.</param>
/// <param name="BoundsMin">World-space AABB, for rejecting the element before marching it.</param>
internal readonly record struct RenderElement(
    Transform3D WorldToLocal,
    Vector3I GridMin,
    Vector3I GridSize,
    int VoxelOffset,
    Vector3 BoundsMin,
    Vector3 BoundsMax)
{
    /// <summary>How many floats one element occupies in the GPU buffer.</summary>
    public const int Floats = 32;

    public int CellCount => GridSize.X * GridSize.Y * GridSize.Z;

    /// <summary>
    /// Writes this element into a float buffer at <paramref name="offset"/>, in the
    /// layout the shader's <c>Element</c> struct declares: a column-major mat4,
    /// then the two bounds vectors, then the two integer triples — every member on
    /// a sixteen-byte boundary, which is what std430 asks of a vec4 or an ivec4.
    /// </summary>
    public void Write(float[] buffer, int offset)
    {
        // A Godot Transform3D is three basis columns and an origin; GLSL's mat4 is
        // four columns of four. The fourth row is 0,0,0,1 by construction.
        WriteColumn(buffer, offset + 0, WorldToLocal.Basis.Column0, 0f);
        WriteColumn(buffer, offset + 4, WorldToLocal.Basis.Column1, 0f);
        WriteColumn(buffer, offset + 8, WorldToLocal.Basis.Column2, 0f);
        WriteColumn(buffer, offset + 12, WorldToLocal.Origin, 1f);

        WriteColumn(buffer, offset + 16, BoundsMin, 0f);
        WriteColumn(buffer, offset + 20, BoundsMax, 0f);

        WriteInts(buffer, offset + 24, GridMin.X, GridMin.Y, GridMin.Z, VoxelOffset);
        // The fourth integer is spare; the shader's struct keeps it for the
        // sixteen-byte alignment an ivec4 wants.
        WriteInts(buffer, offset + 28, GridSize.X, GridSize.Y, GridSize.Z, 0);
    }

    private static void WriteColumn(float[] buffer, int offset, Vector3 value, float w)
    {
        buffer[offset + 0] = value.X;
        buffer[offset + 1] = value.Y;
        buffer[offset + 2] = value.Z;
        buffer[offset + 3] = w;
    }

    /// <summary>
    /// Integers into a float buffer as their own bits, because the shader reads
    /// this run as an <c>ivec4</c>: converting them to floats would arrive as
    /// nonsense on the other side.
    /// </summary>
    private static void WriteInts(float[] buffer, int offset, int x, int y, int z, int w)
    {
        buffer[offset + 0] = System.BitConverter.Int32BitsToSingle(x);
        buffer[offset + 1] = System.BitConverter.Int32BitsToSingle(y);
        buffer[offset + 2] = System.BitConverter.Int32BitsToSingle(z);
        buffer[offset + 3] = System.BitConverter.Int32BitsToSingle(w);
    }
}
