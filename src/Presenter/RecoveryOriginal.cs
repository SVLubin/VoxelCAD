namespace VoxelCAD.Presenter;

/// <summary>What has become of the file a snapshot was taken over.</summary>
internal enum RecoveryOriginal
{
    /// <summary>The document had never been saved; the snapshot is the only copy.</summary>
    Untitled,

    /// <summary>The file is where it was and older than the snapshot.</summary>
    Present,

    /// <summary>The file is no longer at the path the snapshot names.</summary>
    Missing,

    /// <summary>
    /// The file's modification time is not the one the snapshot recorded when it
    /// was written — another instance, another tool, or a save this editor did
    /// not live to record, and a backup restored over it reads the same way. The
    /// snapshot is then opened untitled, so a save has to ask where rather than
    /// aim at a file that moved on.
    /// </summary>
    ChangedSinceSnapshot
}
