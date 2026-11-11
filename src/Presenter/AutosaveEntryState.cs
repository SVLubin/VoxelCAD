namespace VoxelCAD.Presenter;

/// <summary>What a snapshot found on disk can be offered as.</summary>
internal enum AutosaveEntryState
{
    /// <summary>Complete, unlocked, and readable as far as the sidecar can tell.</summary>
    Recoverable,

    /// <summary>Its lock is held: a running editor — this one or another — owns it.</summary>
    InUse,

    /// <summary>Truncated, or its sidecar cannot be read. Listed, never offered, never deleted unasked.</summary>
    Unreadable
}
