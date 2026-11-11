using System;
using System.Collections.Generic;

namespace VoxelCAD.Model;

/// <summary>
/// List mechanics for hierarchy children, held in a private field by both the
/// scene and every element — deliberately composition rather than a shared base
/// class, so a scene can never be mistaken for an element.
/// </summary>
/// <remarks>
/// The children list is the single source of truth: <see cref="HierarchyElement.Parent"/>
/// is written only from here, and only as part of an insert. This type sees only
/// its own children, so whole-tree rules such as cycle prevention belong to
/// <see cref="Scene"/>.
/// </remarks>
internal sealed class HierarchyContainer
{
    private readonly List<HierarchyElement> _childrenList = new();
    private readonly IHierarchyContainer _owner;

    public HierarchyContainer(IHierarchyContainer owner)
    {
        _owner = owner;
    }

    public IReadOnlyList<HierarchyElement> Children => _childrenList;

    public void Insert(HierarchyElement child, int index)
    {
        if (index < 0 || index > _childrenList.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, null);

        _childrenList.Insert(index, child);
        child.SetParent(_owner);
    }

    public void Add(HierarchyElement child) => Insert(child, _childrenList.Count);

    public int IndexOf(HierarchyElement child) => _childrenList.IndexOf(child);

    /// <summary>
    /// Drops every child, leaving their <see cref="HierarchyElement.Parent"/> as it
    /// was — they are on their way out, and nothing reads a detached element's
    /// parent. What this is for is the contents of a prefab instance, which are
    /// rebuilt whole rather than edited.
    /// </summary>
    public void Clear() => _childrenList.Clear();

    public int Remove(HierarchyElement child)
    {
        var index = _childrenList.IndexOf(child);

        if (index >= 0)
            _childrenList.RemoveAt(index);

        return index;
    }
}
