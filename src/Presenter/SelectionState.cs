using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Presenter;

/// <summary>
/// A whole selection: what is in it, and which of them the panels speak for.
/// </summary>
/// <remarks>
/// One value rather than two fields, because the two are only ever right together.
/// A selection caught between them — an active target that is not in the set, a set
/// with nothing active — is a state every reader would have to defend against, so it
/// is made unreachable instead: <see cref="SelectionEdit"/> is the only thing that
/// composes one, and it resolves the active target as it goes.
/// </remarks>
/// <param name="Targets">
/// Everything selected, in the order it was built up. Always of one
/// <see cref="SelectionKind"/> — mixing a material into a set of elements is not a
/// selection anything downstream could act on.
/// </param>
/// <param name="Active">
/// The one the inspector shows, the handles sit on and the brush paints into.
/// <see langword="default"/> when nothing is selected.
/// </param>
internal readonly record struct SelectionState(
    IReadOnlyList<SelectionTarget> Targets,
    SelectionTarget Active)
{
    public static SelectionState Empty { get; } =
        new(Array.Empty<SelectionTarget>(), default);

    /// <summary>One thing selected, which is what most of the editor asks for.</summary>
    public static SelectionState Single(SelectionTarget target) =>
        new(new[] { target }, target);

    public bool Contains(SelectionTarget target)
    {
        for (var index = 0; index < Targets.Count; index++)
        {
            if (Targets[index] == target)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether two selections say the same thing, which is what decides that
    /// nothing has changed. A record struct's own equality compares the list by
    /// reference, and every one of these is a fresh list.
    /// </summary>
    public bool Matches(SelectionState other)
    {
        if (Active != other.Active || Targets.Count != other.Targets.Count)
            return false;

        for (var index = 0; index < Targets.Count; index++)
        {
            if (Targets[index] != other.Targets[index])
                return false;
        }

        return true;
    }
}
