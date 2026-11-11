using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Presenter;

/// <summary>
/// What a click does to the selection. One function, asked by every surface that
/// can change one — the viewport's cursor, the transform tools, the project tree —
/// so that a modifier cannot come to mean one thing in the scene and another in the
/// list.
/// </summary>
/// <remarks>
/// It is a pure function over the selection rather than a method on
/// <see cref="EditorSession"/> for the reason every rule in this codebase ends up
/// out of the state it governs: the session is what two panels share, and a rule
/// living there is a rule nothing can test without one.
/// </remarks>
internal static class SelectionEdit
{
    /// <summary>
    /// The selection after the click. <paramref name="targets"/> is what the click
    /// named — one thing, or a run of them under <see cref="SelectionIntent.Extend"/>
    /// — and <paramref name="active"/> is the one actually clicked, which is also
    /// what decides the kind the whole selection has to be.
    /// </summary>
    /// <remarks>
    /// Two rules are worth stating rather than reading out of the code.
    /// <b>A selection holds one kind</b>: a click naming a kind other than the one
    /// selected replaces rather than mixes, because nothing downstream — the
    /// inspector, the handles, the bin — has a meaning for an element and a material
    /// held at once. And <b>a modifier that named nothing leaves the selection
    /// alone</b>: the hand was adding to it and missed, which is not the same
    /// sentence as "select nothing".
    /// </remarks>
    public static SelectionState Apply(
        SelectionState current,
        IReadOnlyList<SelectionTarget> targets,
        SelectionTarget active,
        SelectionIntent intent)
    {
        List<SelectionTarget> named = KeepKindOf(targets, active);

        if (named.Count == 0)
            return intent == SelectionIntent.Replace ? SelectionState.Empty : current;

        if (intent == SelectionIntent.Replace || !IsSameKind(current, active))
            return new SelectionState(named, ResolveActive(named, active));

        var result = new List<SelectionTarget>(current.Targets);

        foreach (SelectionTarget target in named)
        {
            int index = result.IndexOf(target);

            if (index < 0)
                result.Add(target);
            else if (intent == SelectionIntent.Toggle)
                result.RemoveAt(index);
        }

        return new SelectionState(result, ResolveActive(result, active));
    }

    /// <summary>
    /// Whatever is left, without one target. What the shell does when the thing the
    /// selection named is deleted or undone away — the rest of the selection is
    /// still perfectly good, and dropping it whole would be a second thing happening
    /// that nobody asked for.
    /// </summary>
    public static SelectionState Remove(SelectionState current, SelectionTarget target)
    {
        if (!current.Contains(target))
            return current;

        var result = new List<SelectionTarget>(current.Targets);
        result.Remove(target);

        return new SelectionState(result, ResolveActive(result, current.Active));
    }

    /// <summary>
    /// The clicked target if it is still in, else the last thing standing. A toggle
    /// that took the active target out has to hand the role on rather than leave it
    /// naming something no longer selected.
    /// </summary>
    private static SelectionTarget ResolveActive(
        IReadOnlyList<SelectionTarget> targets, SelectionTarget preferred)
    {
        if (targets.Count == 0)
            return default;

        for (var index = 0; index < targets.Count; index++)
        {
            if (targets[index] == preferred)
                return preferred;
        }

        return targets[targets.Count - 1];
    }

    /// <summary>
    /// The named targets of the clicked row's own kind, in the order they arrived
    /// and without repeats. A run dragged across the tree's categories genuinely
    /// crosses kinds, and the row under the pointer is what says which one was meant.
    /// </summary>
    private static List<SelectionTarget> KeepKindOf(
        IReadOnlyList<SelectionTarget> targets, SelectionTarget active)
    {
        var kept = new List<SelectionTarget>(targets.Count);

        for (var index = 0; index < targets.Count; index++)
        {
            SelectionTarget target = targets[index];

            if (target.Kind == active.Kind && !kept.Contains(target))
                kept.Add(target);
        }

        return kept;
    }

    private static bool IsSameKind(SelectionState current, SelectionTarget active) =>
        current.Targets.Count > 0 && current.Targets[0].Kind == active.Kind;
}
