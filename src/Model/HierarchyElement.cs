using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// The one and only element type. Variation between a group, a voxel model and a
/// prefab instance lives in the component slot, not in a subclass.
/// </summary>
internal sealed class HierarchyElement : IHierarchyElement
{
    private readonly HierarchyContainer _children;
    private readonly ElementTransform _transform = new();

    // Twice the pivot's coordinates in the element's own cells — the half-cell
    // lattice, stored the way a mirror plane is, so a point off it is
    // unrepresentable rather than merely rejected.
    private Vector3I? _pivotDoubled;

    /// <summary>
    /// <paramref name="id"/> is supplied by the scene: prefab contents need
    /// deterministic ids, so an element never mints its own.
    /// </summary>
    public HierarchyElement(Guid id, string name)
    {
        Id = id;
        Name = name;
        _children = new HierarchyContainer(this);
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    // Assigned by HierarchyContainer.Insert before the element is ever handed out.
    public IHierarchyContainer Parent { get; private set; } = null!;

    public IReadOnlyList<IHierarchyElement> Children => _children.Children;

    /// <summary>
    /// <c>Position</c> here names where the <b>pivot</b> stands in the parent frame
    /// — the public meaning of the channel. The stored form keeps the local origin
    /// (see <see cref="ElementTransform"/>), and the two coincide exactly while the
    /// pivot is at zero, which is every element until its first voxels land.
    /// </summary>
    public TransformValues Transform
    {
        get
        {
            TransformValues origin = _transform.Values;

            if (_pivotDoubled == null)
                return origin;

            Basis basis = TransformHelper.ToTransform(origin).Basis;

            return origin with { Position = origin.Position + basis * PivotPoint };
        }
    }

    public TransformModes TransformModes => _transform.Modes;

    public Vector3 PivotPoint => _pivotDoubled is { } doubled
        ? new Vector3(doubled.X, doubled.Y, doubled.Z) * 0.5f
        : Vector3.Zero;

    /// <summary>
    /// Null while the pivot has never been set — the state the first voxel edit's
    /// default is allowed to fill in. An explicit zero is a different thing: it is
    /// a decision, and the default must not overwrite it.
    /// </summary>
    internal Vector3I? PivotDoubled => _pivotDoubled;

    public Transform3D WorldTransform => Scene.CalculateWorldTransform(this);

    /// <inheritdoc/>
    public bool Visible { get; private set; } = true;

    public ComponentKind ComponentKind => Component?.Kind ?? ComponentKind.None;

    public IElementComponent? Component { get; private set; }

    public Guid? PrefabInstanceId { get; private set; }

    internal HierarchyContainer ChildContainer => _children;

    internal ElementTransform ElementTransform => _transform;

    internal void SetParent(IHierarchyContainer parent) => Parent = parent;

    /// <summary>
    /// Two-phase on purpose: a component needs its owner, so the element has to
    /// exist first. Called once, right after construction.
    /// </summary>
    internal void SetComponent(IElementComponent component) => Component = component;

    /// <summary>
    /// Replaces the slot's contents, emptying it when given null. Unlike
    /// <see cref="SetComponent"/> this happens to an element already in a tree, and
    /// there is exactly one thing that does it: unpacking a prefab instance, which
    /// leaves the element behind as an ordinary group. Undo puts the component back.
    /// </summary>
    internal void ApplyComponent(IElementComponent? component) => Component = component;

    /// <summary>
    /// Marks the element as a prefab instance's derived content, which is what
    /// makes it read-only. Null for everything else.
    /// </summary>
    internal void SetPrefabInstanceId(Guid? instanceId) => PrefabInstanceId = instanceId;

    internal void SetName(string name) => Name = name;

    internal void SetVisible(bool visible) => Visible = visible;

    internal void ApplyPivot(Vector3I? pivotDoubled) => _pivotDoubled = pivotDoubled;
}
