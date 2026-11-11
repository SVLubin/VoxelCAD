using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;

namespace VoxelCAD.Presenter;

/// <summary>
/// Which elements a transform gesture actually writes to: everything selected,
/// minus anything standing under something else that is selected, and minus
/// anything the model would refuse to write to at all.
/// </summary>
/// <remarks>
/// A group drag applies one delta to each element's own channel, about each
/// element's own pivot — that is the whole rule, and it is deliberately not "about
/// the selection's centre": wanting a common centre is what a group is for, and
/// inventing a second, invisible one that only exists while several rows happen to
/// be picked out would be a worse thing to explain.
/// <para>
/// The one case that rule cannot be left alone with is a <b>selected element under
/// a selected ancestor</b>. The ancestor's own move carries the whole subtree, so
/// writing the child's channel as well moves it twice — visibly, at double speed,
/// and it is reached without doing anything odd: a group and its children are
/// adjacent rows, which is exactly what a Shift range sweeps up. So a descendant of
/// a write target is dropped, and its geometry still follows because its parent's
/// does.
/// </para>
/// </remarks>
internal static class TransformTargets
{
    /// <summary>
    /// In the selection's own order, so which of them is first is the same answer
    /// twice running — the drag's ghost hangs on one of these.
    /// </summary>
    public static IReadOnlyList<Guid> Collect(
        IScene scene, IReadOnlyList<SelectionTarget> selection)
    {
        var selectedHashSet = new HashSet<Guid>();

        for (var index = 0; index < selection.Count; index++)
        {
            SelectionTarget target = selection[index];

            if (target.Kind == SelectionKind.Element && scene.TryGetElement(target.Id, out _))
                selectedHashSet.Add(target.Id);
        }

        var idsList = new List<Guid>(selectedHashSet.Count);

        for (var index = 0; index < selection.Count; index++)
        {
            SelectionTarget target = selection[index];

            // Prefab-instance contents are read-only wherever they appear, and the
            // model refuses a write to one by throwing — which out of a drag is a
            // backtrace per frame. They are selectable rows, so a hand reaches this
            // without doing anything odd: pick the inner row, press W, pull.
            if (!selectedHashSet.Contains(target.Id)
                || !scene.TryGetElement(target.Id, out IHierarchyElement element)
                || element.PrefabInstanceId != null
                || HasSelectedAncestor(element, selectedHashSet))
            {
                continue;
            }

            idsList.Add(target.Id);
        }

        return idsList;
    }

    private static bool HasSelectedAncestor(
        IHierarchyElement element, HashSet<Guid> selectedHashSet)
    {
        for (IHierarchyContainer current = element.Parent;
            current is IHierarchyElement ancestor;
            current = ancestor.Parent)
        {
            if (selectedHashSet.Contains(ancestor.Id))
                return true;
        }

        return false;
    }
}
