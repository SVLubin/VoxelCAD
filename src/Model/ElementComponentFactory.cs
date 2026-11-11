using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Builds the component that fills an element's slot. The only switch over
/// <see cref="ComponentKind"/> in the model.
/// </summary>
internal static class ElementComponentFactory
{
    /// <summary>Returns null for <see cref="ComponentKind.None"/> — a grouping element.</summary>
    public static IElementComponent? Create(ComponentKind kind, IHierarchyElement owner) => kind switch
    {
        ComponentKind.None => null,
        ComponentKind.VoxelModel => new VoxelModel(owner),
        ComponentKind.Camera => new Camera(owner),
        ComponentKind.ReferenceImage => new ReferenceImage(owner),
        ComponentKind.ReferenceModel => new ReferenceModel(owner),
        // A component that is nothing but a reference cannot be built from a kind:
        // IScene.CreateInstance names the prefab, and that is the only way in.
        ComponentKind.PrefabInstance => throw new NotSupportedException(
            "Prefab instances are created from a prefab source, not from a bare component kind."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
