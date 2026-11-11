using System;
using VoxelCAD.Model;

namespace VoxelCAD.Presenter;

/// <summary>
/// The identity of the history record a document sits on. One function, because
/// two questions are asked of it — "is there anything to lose" and "did anything
/// move since the last snapshot" — and two copies of the rule could come to
/// answer differently.
/// </summary>
/// <remarks>
/// Identity rather than index: undoing back to the saved point and editing again
/// lands on the same index with a different record, and an index comparison would
/// call that saved.
/// </remarks>
internal static class RecordIdentity
{
    /// <summary><see cref="Guid.Empty"/> when nothing is applied — a new or loaded project.</summary>
    public static Guid Current(IHistory history)
    {
        int index = history.CurrentIndex;

        return index < 0 ? Guid.Empty : history.Records[index].Id;
    }
}
