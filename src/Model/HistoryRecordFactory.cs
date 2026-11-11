using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// The one place a history record is minted. A command names what happened and
/// to whom; the record's own identity and its timestamp are bookkeeping every
/// command was repeating — and the actor and scene are not here either, because
/// the history stamps those when the record is filed.
/// </summary>
internal static class HistoryRecordFactory
{
    public static HistoryRecord Create(ActionKind kind, Guid targetId, string targetName) =>
        new(Guid.NewGuid(), kind, targetId, targetName, DateTime.UtcNow);
}
