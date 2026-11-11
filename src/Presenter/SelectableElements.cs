using System;
using System.Collections.Generic;
using VoxelCAD.Model;

namespace VoxelCAD.Presenter;

/// <summary>
/// What a click on an element actually means. A prefab instance's contents are
/// derived and refuse every edit, so pointing at one means pointing at the instance
/// that stands for it.
/// </summary>
/// <remarks>
/// Found by driving the editor: a click on a model that had just become a prefab
/// selected the read-only twin inside the instance, and the inspector went quiet —
/// no transform to edit, no bin, no prefab row. What every editor with prefabs does
/// instead is select the instance, and this is that rule.
/// <para>
/// The walk is outward rather than one hop: inside a nested instance the contents
/// belong to the inner instance, which is itself the outer one's contents. It ends
/// at the first element that is nobody's contents, which every chain reaches.
/// </para>
/// </remarks>
internal static class SelectableElements
{
    /// <summary>
    /// The stack of hits, each mapped to what may be acted on, nearest first. A run
    /// of hits inside one instance collapses to a single entry: to the hand they are
    /// one thing, and leaving duplicates in would make clicking the same pixel again
    /// "cycle" between two names for the same element.
    /// </summary>
    public static IReadOnlyList<Guid> Resolve(IScene scene, IReadOnlyList<Guid> elementsList)
    {
        var resolvedList = new List<Guid>(elementsList.Count);
        var seenHashSet = new HashSet<Guid>();

        foreach (Guid id in elementsList)
        {
            Guid owner = OwnerOf(scene, id);

            if (seenHashSet.Add(owner))
                resolvedList.Add(owner);
        }

        return resolvedList;
    }

    /// <summary>
    /// What one element stands for under the pointer: itself, or the outermost
    /// instance whose contents it is. Asked of a selection made in the project tree
    /// too, which may name a row a click could never return.
    /// </summary>
    public static Guid OwnerOf(IScene scene, Guid id)
    {
        Guid owner = id;

        while (scene.TryGetElement(owner, out IHierarchyElement element)
            && element.PrefabInstanceId is { } instanceId)
        {
            owner = instanceId;
        }

        return owner;
    }
}
