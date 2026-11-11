namespace VoxelCAD.CommonTypes;

/// <summary>
/// One row of the history panel: the label the presenter worded, and the kind of
/// action it describes. The kind rides along so the view can dress the row — an
/// icon is appearance, and appearance is decided on the view side.
/// </summary>
/// <param name="GroupId">
/// Which foldable group the row belongs to, or zero for a row that stands on its
/// own. Rows of one group are always consecutive — control is exclusive, so an
/// agent's run of records cannot have the user's interleaved with it — which is
/// what lets the panel raise a group from a run of equal ids without being told
/// separately where each one starts and ends.
/// </param>
/// <param name="GroupLabel">
/// What the group's own row reads, worded by the presenter like every other
/// label here. Null for an ungrouped row.
/// </param>
public readonly record struct HistoryRow(
    string Label,
    ActionKind Kind,
    int GroupId,
    string? GroupLabel);
