using System;
using System.IO;

namespace VoxelCAD.Presenter;

/// <summary>
/// One snapshot on disk, with the facts derived about it. Presenter-internal:
/// what crosses to the view is a composed <see cref="CommonTypes.ConfirmRequest"/>,
/// what crosses to an agent is a dictionary.
/// </summary>
/// <param name="WrittenAt">UTC.</param>
/// <param name="Records">Applied history records at the write; 0 when unknown (a lone document with no sidecar).</param>
/// <param name="Problem">Why it is unreadable; empty otherwise.</param>
internal readonly record struct AutosaveEntry(
    Guid Id,
    string SnapshotPath,
    string? OriginalPath,
    DateTime WrittenAt,
    int Records,
    RecoveryOriginal Original,
    AutosaveEntryState State,
    string Problem)
{
    public string Title =>
        OriginalPath is null ? WindowTitleHelper.UntitledName : Path.GetFileName(OriginalPath);

    public bool IsRecoverable => State == AutosaveEntryState.Recoverable;
}
