using Godot;

namespace VoxelCAD.Render;

/// <summary>
/// One emissive cell, as something to aim at. The tracer samples the sphere that
/// bounds it and accepts the sample only when the ray lands on that very cell.
/// </summary>
/// <remarks>
/// Its identity travels with it — which element, which cell — because that is
/// what makes the estimator exact with more than one light in the scene: a
/// direction drawn towards this cell that lands on another one is not this cell's
/// contribution, and counting it would be counting the other one at the wrong
/// density.
/// <para>
/// The centre and the radius are in world space, resolved at compile time, so the
/// shader needs no transform to place a light. The radius bounds the whole cell
/// rather than fitting inside it: a cone that misses part of the cube would lose
/// exactly the light from its corners.
/// </para>
/// </remarks>
public readonly record struct RenderLight(int Element, Vector3I Cell, Vector3 Centre, float Radius)
{
    /// <summary>How many floats one light occupies in the GPU buffer.</summary>
    public const int Floats = 8;

    public void Write(float[] buffer, int offset)
    {
        buffer[offset + 0] = Centre.X;
        buffer[offset + 1] = Centre.Y;
        buffer[offset + 2] = Centre.Z;
        buffer[offset + 3] = Radius;

        buffer[offset + 4] = System.BitConverter.Int32BitsToSingle(Element);
        buffer[offset + 5] = System.BitConverter.Int32BitsToSingle(Cell.X);
        buffer[offset + 6] = System.BitConverter.Int32BitsToSingle(Cell.Y);
        buffer[offset + 7] = System.BitConverter.Int32BitsToSingle(Cell.Z);
    }
}
