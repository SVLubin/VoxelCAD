using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Grid-aligned vector. Backed by <see cref="Vector3I"/>, so a fractional value
/// cannot be stored at all.
/// </summary>
internal sealed class IntFieldVector3 : IFieldVector3
{
    private Vector3I _value;

    public IntFieldVector3(Vector3 value)
    {
        Set(value);
    }

    public FieldMode Mode => FieldMode.Integer;

    public Vector3 AsFloat => _value;

    public Vector3I AsInt => _value;

    public void Set(Vector3 value) => _value = RoundingHelper.ToNearest(value);

    public void Set(Vector3I value) => _value = value;
}
