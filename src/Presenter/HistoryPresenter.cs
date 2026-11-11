using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>Mirrors the undo history into its panel, and lets a click walk back to any point.</summary>
internal sealed class HistoryPresenter : PresenterBase
{
    private readonly IHistory _history;
    private readonly IHistoryView _view;

    public HistoryPresenter(IHistory history, IHistoryView view)
    {
        _history = history;
        _view = view;

        // The constructor fills the view from scratch, starting with Clear() — the
        // panel invariant, and this presenter is built once per document. What
        // SetRecords does not touch is what would otherwise survive one: the folds,
        // keyed by a session number that means nothing in the next document's
        // history.
        _view.Clear();

        Refresh();

        _history.Changed += OnHistoryChanged;
        _view.RecordClicked += OnRecordClicked;
    }

    protected override void OnDispose()
    {
        _history.Changed -= OnHistoryChanged;
        _view.RecordClicked -= OnRecordClicked;
    }

    /// <summary>
    /// The payload says which record moved; this panel rebuilds regardless,
    /// having no state per row worth preserving. See the tech-debt ledger.
    /// </summary>
    private void OnHistoryChanged(HistoryChange change) => Refresh();

    /// <summary>
    /// Words every record, and marks the ones an agent made with the session they
    /// belong to, so the panel can fold a connection's whole run of work into one
    /// row. The grouping is appearance only: the records themselves are ordinary
    /// entries, and undo walks through them one at a time exactly as before.
    /// </summary>
    private void Refresh()
    {
        IReadOnlyList<HistoryRecord> records = _history.Records;
        var rowsList = new List<HistoryRow>(records.Count);

        foreach (HistoryRecord record in records)
        {
            int session = GroupOf(record);

            rowsList.Add(new HistoryRow(
                ActionLabelHelper.Describe(record),
                record.Kind,
                session,
                session == 0 ? null : DescribeSession(records, record.Actor)));
        }

        _view.SetRecords(rowsList, _history.CurrentIndex);
    }

    /// <summary>
    /// The group a record belongs to: an agent's session, or none at all. The
    /// user's own edits are deliberately ungrouped — they are the ordinary case,
    /// and a foldout around them would hide the panel's normal contents.
    /// </summary>
    private static int GroupOf(HistoryRecord record) =>
        record.Actor.Kind == ActorKind.Agent ? record.Actor.Session : 0;

    /// <summary>
    /// The group row's own wording. Counted over the whole list rather than
    /// carried along, because a session that is still being added to has to say
    /// how much it holds now, not how much it held when its first record landed.
    /// </summary>
    private static string DescribeSession(
        IReadOnlyList<HistoryRecord> records,
        HistoryActor actor)
    {
        var count = 0;

        foreach (HistoryRecord record in records)
        {
            if (GroupOf(record) == actor.Session)
                count++;
        }

        string name = string.IsNullOrEmpty(actor.Name) ? "An agent" : $"\"{actor.Name}\"";

        return $"{name} — {count} {(count == 1 ? "action" : "actions")}";
    }

    private void OnRecordClicked(int index) => _history.UndoTo(index);
}
