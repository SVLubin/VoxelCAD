using System;

namespace VoxelCAD.Presenter;

/// <summary>
/// The open document's autosave state in one value: the presenter answers it,
/// the command flattens it, the label helper words it, a test asserts on it.
/// </summary>
/// <param name="PendingChanges">Modified, and the record moved since the last snapshot.</param>
/// <param name="Deferred">A due write is waiting on a gesture or a busy user.</param>
/// <param name="WriteInFlight">
/// A snapshot is being written on a worker thread, or has been and is not booked
/// yet — so it goes false on the same tick <see cref="HasSnapshot"/> goes true.
/// </param>
/// <param name="SessionOrphans">Snapshots this session's own commands left behind.</param>
internal readonly record struct AutosaveStatus(
    bool Enabled,
    double IntervalSeconds,
    string Directory,
    string Title,
    string? OriginalPath,
    Guid? SnapshotId,
    bool HasSnapshot,
    bool PendingChanges,
    bool Deferred,
    bool WriteInFlight,
    double SecondsUntilNextWrite,
    DateTime? LastWrittenAt,
    double? LastWriteMilliseconds,
    string? LastFailure,
    int SessionOrphans);
