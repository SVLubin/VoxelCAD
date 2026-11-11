using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <inheritdoc cref="IHistory"/>
internal sealed class History : IHistory
{
    /// <summary>
    /// A record count, not a memory budget — voxel deltas are not in history yet.
    /// See the tech-debt ledger.
    /// </summary>
    private const int MaximumRecords = 200;

    private readonly List<ICommand> _commandsList = new();
    private readonly List<HistoryRecord> _recordsList = new();

    private Batch? _openBatch;

    public event Action<HistoryChange>? Changed;

    public HistoryActor CurrentActor { get; set; }

    public IReadOnlyList<HistoryRecord> Records => _recordsList;

    public int CurrentIndex { get; private set; } = -1;

    public bool CanUndo => CurrentIndex >= 0;

    public bool CanRedo => CurrentIndex < _commandsList.Count - 1;

    public void Undo()
    {
        if (!CanUndo)
            return;

        var reverted = CurrentIndex;

        _commandsList[reverted].Undo();
        CurrentIndex--;
        Raise(reverted);
    }

    public void Redo()
    {
        if (!CanRedo)
            return;

        var applied = CurrentIndex + 1;

        _commandsList[applied].Do();
        CurrentIndex = applied;
        Raise(applied);
    }

    public void UndoTo(int index)
    {
        if (index < -1 || index >= _commandsList.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, null);

        // Which record moved last is what the editor follows, so a walk across
        // several scenes ends up showing the one it stopped in.
        var lastTouched = -1;

        while (CurrentIndex > index)
        {
            lastTouched = CurrentIndex;
            _commandsList[CurrentIndex].Undo();
            CurrentIndex--;
        }

        while (CurrentIndex < index)
        {
            lastTouched = CurrentIndex + 1;
            _commandsList[lastTouched].Do();
            CurrentIndex++;
        }

        Raise(lastTouched);
    }

    public IDisposable CreateBatch(ActionKind kind, Guid sceneId, Guid targetId, string targetName)
    {
        if (_openBatch != null)
        {
            throw new InvalidOperationException(
                "A history batch is already open; nested batches are not supported.");
        }

        _openBatch = new Batch(
            this,
            HistoryRecordFactory.Create(kind, targetId, targetName) with { SceneId = sceneId });

        return _openBatch;
    }

    /// <summary>Runs a project-level command — one that belongs to no scene.</summary>
    public void Execute(ICommand command) => Execute(command, Guid.Empty);

    /// <summary>
    /// Runs the command and records it against <paramref name="sceneId"/>. Inside
    /// a batch the command is collected instead, so the whole gesture lands as a
    /// single record — one already stamped with the scene it was opened for.
    /// </summary>
    public void Execute(ICommand command, Guid sceneId)
    {
        command.Do();

        if (_openBatch != null)
        {
            _openBatch.Collect(command);
            return;
        }

        Push(command, sceneId);
    }

    private void Push(ICommand command, Guid sceneId)
    {
        // Anything that was undone is unreachable the moment new work happens.
        var firstUndone = CurrentIndex + 1;

        if (firstUndone < _commandsList.Count)
        {
            _commandsList.RemoveRange(firstUndone, _commandsList.Count - firstUndone);
            _recordsList.RemoveRange(firstUndone, _recordsList.Count - firstUndone);
        }

        _commandsList.Add(command);
        _recordsList.Add(command.Record with { Actor = CurrentActor, SceneId = sceneId });
        CurrentIndex = _commandsList.Count - 1;

        Trim();
        Raise(CurrentIndex);
    }

    /// <summary>
    /// Reports the record that moved. An index of -1 — a navigation that changed
    /// nothing — carries no scene, so nobody switches away on it.
    /// </summary>
    private void Raise(int index) =>
        Changed?.Invoke(new HistoryChange(
            index,
            index >= 0 && index < _recordsList.Count ? _recordsList[index].SceneId : Guid.Empty));

    private void Trim()
    {
        var excess = _commandsList.Count - MaximumRecords;

        if (excess <= 0)
            return;

        _commandsList.RemoveRange(0, excess);
        _recordsList.RemoveRange(0, excess);
        CurrentIndex -= excess;
    }

    private void CloseBatch(Batch batch)
    {
        if (!ReferenceEquals(_openBatch, batch))
            return;

        _openBatch = null;

        // An empty gesture — a drag that never moved anything — leaves no trace.
        if (batch.IsEmpty)
            return;

        Push(batch, batch.Record.SceneId);
    }

    private sealed class Batch : ICommand, IDisposable
    {
        private readonly History _history;
        private readonly List<ICommand> _commandsList = new();

        public Batch(History history, HistoryRecord record)
        {
            _history = history;
            Record = record;
        }

        public HistoryRecord Record { get; }

        public bool IsEmpty => _commandsList.Count == 0;

        public void Collect(ICommand command) => _commandsList.Add(command);

        public void Do()
        {
            foreach (ICommand command in _commandsList)
                command.Do();
        }

        public void Undo()
        {
            for (var i = _commandsList.Count - 1; i >= 0; i--)
                _commandsList[i].Undo();
        }

        public void Dispose() => _history.CloseBatch(this);
    }
}
