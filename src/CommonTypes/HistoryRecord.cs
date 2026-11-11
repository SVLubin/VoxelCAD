using System;

namespace VoxelCAD.CommonTypes;

/// <summary>One entry of the undo history, as the history panel sees it.</summary>
/// <param name="Id">Identity of the record itself, not of the element it touched.</param>
/// <param name="TargetName">
/// A snapshot taken when the action happened. It cannot be looked up on demand:
/// an entry describing a deletion outlives the element it names.
/// </param>
public readonly record struct HistoryRecord(
    Guid Id,
    ActionKind Kind,
    Guid TargetId,
    string TargetName,
    DateTime At)
{
    /// <summary>
    /// Who caused this. Stamped by the history when the record is filed, not by
    /// the command, so no command has to carry it. Defaults to the user.
    /// </summary>
    public HistoryActor Actor { get; init; }

    /// <summary>
    /// Which scene the action happened in, or <see cref="Guid.Empty"/> for a
    /// project-level action such as a material edit. Stamped by the history from
    /// the scene that ran the command, so no command has to carry it either.
    /// </summary>
    public Guid SceneId { get; init; }
}
