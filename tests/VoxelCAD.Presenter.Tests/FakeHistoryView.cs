using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;

namespace VoxelCAD.Presenter.Tests;

/// <summary>Remembers what the history panel was told, so a test can read it back.</summary>
internal sealed class FakeHistoryView : IHistoryView
{
    public event Action<int>? RecordClicked;

    public IReadOnlyList<HistoryRow> Rows { get; private set; } = Array.Empty<HistoryRow>();

    public IReadOnlyList<string> Labels => Rows.Select(row => row.Label).ToList();

    public int CurrentIndex { get; private set; } = -1;

    /// <summary>
    /// How many times the panel was emptied. A count, because what the real view
    /// drops here — the folds — is state a test cannot read back any other way.
    /// </summary>
    public int ClearCount { get; private set; }

    public void Clear()
    {
        ClearCount++;
        Rows = Array.Empty<HistoryRow>();
        CurrentIndex = -1;
    }

    public void SetRecords(IReadOnlyList<HistoryRow> rows, int currentIndex)
    {
        Rows = rows;
        CurrentIndex = currentIndex;
    }

    public void RaiseRecordClicked(int index) => RecordClicked?.Invoke(index);
}
