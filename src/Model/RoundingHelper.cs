using System;
using Godot;

namespace VoxelCAD.Model;

/// <summary>The single rounding rule of the model. Every quantiser goes through it.</summary>
/// <remarks>
/// Half-way values round away from zero, which is what someone dragging on a grid
/// expects. Godot's own <c>Vector3.Round</c> inherits .NET's banker's rounding
/// (2.5 becomes 2, 3.5 becomes 4), so snapping would look arbitrary — and having
/// two rounding rules in one codebase guarantees the two paths drift apart.
/// </remarks>
internal static class RoundingHelper
{
    public static Vector3I ToNearest(Vector3 value) =>
        new(ToNearest(value.X), ToNearest(value.Y), ToNearest(value.Z));

    private static int ToNearest(float value) =>
        (int)MathF.Round(value, MidpointRounding.AwayFromZero);
}
