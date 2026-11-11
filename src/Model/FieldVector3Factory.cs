using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// The only place in the codebase that switches over <see cref="FieldMode"/>.
/// A mode is never changed in place — the vector is replaced, carrying its
/// value across as a float.
/// </summary>
internal static class FieldVector3Factory
{
    public static IFieldVector3 Create(FieldMode mode, Vector3 value) => mode switch
    {
        FieldMode.Float => new FloatFieldVector3(value),
        FieldMode.Integer => new IntFieldVector3(value),
        FieldMode.Snap90 => new Snap90FieldVector3(value),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}
