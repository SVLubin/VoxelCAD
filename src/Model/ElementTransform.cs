using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// The three constrained vectors of one hierarchy element.
/// </summary>
/// <remarks>
/// Deliberately dumb: no events, no history, no validation. The scene owns all
/// of those — it is the only caller, and the only object that can see whether a
/// change is legal (for example that Snap90 is rotation-only).
/// </remarks>
internal sealed class ElementTransform
{
    // A new element is on the grid: whole cells, right angles, unit cells. That is
    // the CAD default, and it is also what makes an element's own grid coincide
    // with the scene's, so the walls of the build region are a surface the brush
    // can place against. Going off the grid stays available, and is a choice.
    // A kind for which none of that reasoning holds says so through
    // ElementTransformDefaults, which the scene applies as the element is created.
    private IFieldVector3 _position = FieldVector3Factory.Create(FieldMode.Integer, Vector3.Zero);
    private IFieldVector3 _rotation = FieldVector3Factory.Create(FieldMode.Snap90, Vector3.Zero);
    private IFieldVector3 _scale = FieldVector3Factory.Create(FieldMode.Integer, Vector3.One);

    public TransformValues Values => new(_position.AsFloat, _rotation.AsFloat, _scale.AsFloat);

    public TransformModes Modes => new(_position.Mode, _rotation.Mode, _scale.Mode);

    public Vector3 Get(TransformChannel channel) => Slot(channel).AsFloat;

    public void Set(TransformChannel channel, Vector3 value) => Slot(channel).Set(value);

    public void Set(TransformChannel channel, Vector3I value) => Slot(channel).Set(value);

    public void SetModes(TransformModes modes)
    {
        SetMode(TransformChannel.Position, modes.Position);
        SetMode(TransformChannel.Rotation, modes.Rotation);
        SetMode(TransformChannel.Scale, modes.Scale);
    }

    public void SetMode(TransformChannel channel, FieldMode mode)
    {
        ref IFieldVector3 slot = ref Slot(channel);
        slot = FieldVector3Factory.Create(mode, slot.AsFloat);
    }

    /// <summary>
    /// Mode first, then value: setting the mode re-quantises whatever is stored, so
    /// the order matters when restoring a state that undo captured.
    /// </summary>
    public void Restore(TransformValues values, TransformModes modes)
    {
        SetModes(modes);

        Set(TransformChannel.Position, values.Position);
        Set(TransformChannel.Rotation, values.RotationDegrees);
        Set(TransformChannel.Scale, values.Scale);
    }

    private ref IFieldVector3 Slot(TransformChannel channel)
    {
        switch (channel)
        {
            case TransformChannel.Position:
                return ref _position;
            case TransformChannel.Rotation:
                return ref _rotation;
            case TransformChannel.Scale:
                return ref _scale;
            default:
                throw new ArgumentOutOfRangeException(nameof(channel), channel, null);
        }
    }
}
