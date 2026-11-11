using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>Unconstrained vector — every value conforms, nothing is quantised.</summary>
internal sealed class FloatFieldVector3 : IFieldVector3
{
    private Vector3 _value;

    public FloatFieldVector3(Vector3 value)
    {
        _value = value;
    }

    public FieldMode Mode => FieldMode.Float;

    public Vector3 AsFloat => _value;

    public Vector3I AsInt => RoundingHelper.ToNearest(_value);

    public void Set(Vector3 value) => _value = value;

    public void Set(Vector3I value) => _value = value;
}
