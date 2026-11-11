using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// One node of the document hierarchy: a transform plus a single optional
/// component. An element with no component is a pure grouping node, the
/// equivalent of an empty Unity GameObject.
/// </summary>
public interface IHierarchyElement : IHierarchyContainer
{
    string Name { get; }

    /// <summary>
    /// Never null — an element at the top level parents to the scene, which is a
    /// container in its own right.
    /// </summary>
    IHierarchyContainer Parent { get; }

    /// <summary>
    /// Local transform, already conforming to <see cref="TransformModes"/>.
    /// <c>Position</c> names where the element's <b>pivot</b> stands in the parent
    /// frame: the frame composes as <c>T(pos) · R · S · T(−pivot)</c>, so the
    /// number in the channel, the point the handles sit on and the point a
    /// rotation turns about are one and the same.
    /// </summary>
    TransformValues Transform { get; }

    TransformModes TransformModes { get; }

    /// <summary>
    /// The element's pivot, in its own cells, always on the half-cell lattice.
    /// Every element has one: it starts at zero — where position and local origin
    /// still coincide — is given a default at the element's first voxel edit (the
    /// middle of what that edit filled), and moves only through
    /// <see cref="IScene.SetPivot"/>, never by itself.
    /// </summary>
    Vector3 PivotPoint { get; }

    /// <summary>
    /// Where the element actually sits in the scene, with every ancestor's
    /// transform composed in. Walked on demand and never cached — the canon's
    /// rule, and the reason this is a property rather than stored state.
    /// </summary>
    Transform3D WorldTransform { get; }

    /// <summary>
    /// Whether the element is shown and exported. Every element has one — the flag
    /// belongs to the transform half of an element, not to any component — and it
    /// is the element's <b>own</b>: what is nested under a hidden element is hidden
    /// with it, because visibility composes down the tree the way a transform does,
    /// and not by this flag being written to the children.
    /// </summary>
    bool Visible { get; }

    ComponentKind ComponentKind { get; }

    IElementComponent? Component { get; }

    /// <summary>
    /// Null for ordinary elements. Otherwise the id of the prefab instance root
    /// this element belongs to, which also makes the element read-only.
    /// </summary>
    Guid? PrefabInstanceId { get; }
}
