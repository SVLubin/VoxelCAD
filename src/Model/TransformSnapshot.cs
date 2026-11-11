using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// An element's complete transform state. Values and modes travel together
/// because undo has to restore both: a reparent can change a mode as well as a
/// value, and restoring one without the other would re-quantise the other.
/// </summary>
internal readonly record struct TransformSnapshot(TransformValues Values, TransformModes Modes)
{
    public Vector3 ValueOf(TransformChannel channel) => channel switch
    {
        TransformChannel.Position => Values.Position,
        TransformChannel.Rotation => Values.RotationDegrees,
        TransformChannel.Scale => Values.Scale,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
    };

    public FieldMode ModeOf(TransformChannel channel) => channel switch
    {
        TransformChannel.Position => Modes.Position,
        TransformChannel.Rotation => Modes.Rotation,
        TransformChannel.Scale => Modes.Scale,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
    };

    public TransformSnapshot WithValue(TransformChannel channel, Vector3 value) => channel switch
    {
        TransformChannel.Position => this with { Values = Values with { Position = value } },
        TransformChannel.Rotation => this with { Values = Values with { RotationDegrees = value } },
        TransformChannel.Scale => this with { Values = Values with { Scale = value } },
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
    };

    public TransformSnapshot WithMode(TransformChannel channel, FieldMode mode) => channel switch
    {
        TransformChannel.Position => this with { Modes = Modes with { Position = mode } },
        TransformChannel.Rotation => this with { Modes = Modes with { Rotation = mode } },
        TransformChannel.Scale => this with { Modes = Modes with { Scale = mode } },
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
    };
}
