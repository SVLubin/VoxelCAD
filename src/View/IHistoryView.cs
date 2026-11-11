using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The undo history panel. Rows arrive already worded by the presenter — the
/// model stores an action kind, not English — with the kind alongside so the
/// panel can dress each row in the glyph its action wears.
/// </summary>
public interface IHistoryView
{
    /// <summary>Index of the record clicked; -1 means the state before everything.</summary>
    event Action<int> RecordClicked;

    /// <summary>
    /// Drops everything the open document put here — the rows, and the folds the
    /// user made over them. The panel's constructor-fills-from-scratch invariant
    /// needs this half stated: <see cref="SetRecords"/> replaces the rows on every
    /// change, so what survives a document is exactly what it does <em>not</em>
    /// touch, and a session number is only unique inside one document's history.
    /// </summary>
    void Clear();

    /// <summary>Records after <paramref name="currentIndex"/> are shown as undone.</summary>
    void SetRecords(IReadOnlyList<HistoryRow> rows, int currentIndex);
}
