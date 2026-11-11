using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// What fills an element's single component slot. A component has no identity of
/// its own — it is addressed through its <see cref="Owner"/>.
/// </summary>
public interface IElementComponent
{
    ComponentKind Kind { get; }

    /// <summary>
    /// The element this component belongs to. Assigned when the component is
    /// attached and never null afterwards; not serialized, since it is implied by
    /// the tree.
    /// </summary>
    IHierarchyElement Owner { get; }
}
