using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <inheritdoc cref="IMaterial"/>
internal sealed class Material : IMaterial
{
    public Material(Guid id, MaterialSnapshot state)
    {
        Id = id;
        Apply(state);
    }

    public Guid Id { get; }

    public string Name { get; private set; } = string.Empty;

    public Color Color { get; private set; }

    public MaterialAppearance Appearance { get; private set; } = MaterialAppearance.Default;

    public bool IsStandard => StandardMaterials.Contains(Id);

    public MaterialSnapshot Capture() => new(Name, Color, Appearance);

    public void Apply(MaterialSnapshot state)
    {
        Name = state.Name;
        Color = state.Color;
        Appearance = state.AppearanceOrDefault;
    }
}
