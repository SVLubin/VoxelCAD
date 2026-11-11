using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Preferences;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Keeps a recovery snapshot of the open document, offers the ones earlier
/// sessions left behind, and answers what it is doing. Outlives documents like
/// <see cref="ControlPresenter"/>; the shell binds each document to it as it
/// opens and unbinds it as it goes.
/// </summary>
/// <remarks>
/// <b>The resting invariant: a snapshot exists iff, at the last interval that
/// looked, the document had unsaved work.</b> <see cref="AutosaveRule"/> is that
/// sentence as a table, asked once per interval with what this presenter knows —
/// whether the record identity moved since the last snapshot, whether a gesture
/// is in flight, whether the user is busy. A save by any route deletes the
/// snapshot; so does the user's own Discard. A document replaced without either
/// — an agent's <c>new</c>, <c>open</c> or a recovery over it — is written on
/// the way out and kept as an orphan of this session.
/// <para>
/// <b>The write is split across two threads.</b> Capturing the document is
/// cheap (the tree walked, the cells copied) and runs here, on the frame that
/// decided; encoding and writing it is where a save spends its time — measured
/// at about 130 ms on a model of 170,000 cells — and runs on a worker. Nothing on
/// the worker touches the model: the capture owes it nothing. One write is in
/// flight at a time, and every path that acts on the files — a save, a discard,
/// an unbind — waits for it first, so a stale write can never land after a
/// delete.
/// </para>
/// <para>
/// It reads its own bound document and the path it captured at bind, never
/// <see cref="CommandContext.Project"/>: <see cref="Unbind"/> runs inside
/// <c>ProjectChanged</c>, after the context has already switched to the document
/// that replaced the one being written on its way out.
/// </para>
/// </remarks>
internal sealed class AutosavePresenter : PresenterBase
{
    private readonly AutosaveStore _store;
    private readonly CommandContext _context;
    private readonly EditorSession _session;
    private readonly ControlArbiter _arbiter;
    private readonly IDialogService _dialogs;
    private readonly IEditorPreferences _preferences;

    /// <summary>
    /// The snapshots this session's own commands left behind — the only ones an
    /// agent may discard. Earlier sessions' snapshots are the user's.
    /// </summary>
    private readonly HashSet<Guid> _sessionOrphansHashSet = new();

    private DocumentPresenter? _document;
    private Guid _id;
    private AutosaveStore.Lease? _lease;
    private bool _hasSnapshot;
    private string? _originalPath;

    /// <summary>The record identity the last snapshot was taken at.</summary>
    private Guid _snapshotRecordId;

    private double _elapsed;

    /// <summary>The entry a recovery adopts; consumed by the <see cref="Bind"/> that follows.</summary>
    private AutosaveEntry? _pendingRecovery;

    /// <summary>A snapshot opened as if it were a document, to be turned into a recovery on the next tick.</summary>
    private Guid? _redirect;

    private Task? _inFlight;
    private Guid _inFlightRecordId;
    private Stopwatch? _inFlightWatch;

    private DateTime? _lastWrittenAt;
    private double? _lastWriteMilliseconds;
    private string? _lastFailure;

    /// <summary>
    /// A failure not yet shown. Shown once, on the transition into failing or when
    /// the message changes, and not while an agent holds the editor — a card under
    /// the dimmer is a card nobody can answer.
    /// </summary>
    private string? _failureToReport;

    public AutosavePresenter(
        AutosaveStore store,
        CommandContext context,
        EditorSession session,
        ControlArbiter arbiter,
        IDialogService dialogs,
        IEditorPreferences preferences)
    {
        _store = store;
        _context = context;
        _session = session;
        _arbiter = arbiter;
        _dialogs = dialogs;
        _preferences = preferences;
    }

    /// <summary>The set of pending snapshots may have changed — bind, unbind, delete, recover, offer.</summary>
    public event Action? PendingChanged;

    /// <summary>The status line changed: a write landed or failed, a deferral began or ended.</summary>
    public event Action? StatusChanged;

    public string Directory => _store.Directory;

    /// <summary>Where a snapshot's document lives — the store's own naming, asked rather than restated.</summary>
    public string SnapshotPath(Guid id) => _store.SnapshotPath(id);

    /// <summary>
    /// True between a recovery's <c>SetProject</c> and the <see cref="Bind"/> that
    /// consumes it. The shell passes it into the document presenter it builds, so
    /// a recovered document is born modified.
    /// </summary>
    public bool HasPendingRecovery => _pendingRecovery is not null;

    /// <summary>How many snapshots nobody holds are waiting to be offered.</summary>
    public int PendingCount
    {
        get
        {
            var count = 0;

            foreach (AutosaveEntry entry in Scan())
            {
                if (entry.IsRecoverable)
                    count++;
            }

            return count;
        }
    }

    public void Bind(DocumentPresenter document)
    {
        _document = document;
        _lease = null;
        _hasSnapshot = false;
        _redirect = null;
        _id = Guid.NewGuid();

        // Facts about the previous document's writes do not describe this one.
        _lastWrittenAt = null;
        _lastWriteMilliseconds = null;
        _lastFailure = null;

        if (_pendingRecovery is { } recovered)
        {
            _pendingRecovery = null;
            AdoptRecovered(recovered);
        }
        else if (_store.TryGetSnapshotId(_context.Path, out Guid opened)
            && _store.TryGetEntry(opened, out AutosaveEntry entry)
            && entry.IsRecoverable)
        {
            // One frame later rather than here: a recovery replaces the document,
            // and this runs inside the change that has just installed it.
            _redirect = opened;
        }

        _originalPath = _context.Path;
        _snapshotRecordId = RecordIdentity.Current(document.Project.History);
        _elapsed = 0.0;

        PendingChanged?.Invoke();
        StatusChanged?.Invoke();
    }

    /// <summary>
    /// The document is being replaced or the shell is closing. Unsaved work that
    /// moved since the last snapshot is written on the way out — there is no
    /// later chance — and the pair, if any, becomes an orphan of this session.
    /// </summary>
    public void Unbind()
    {
        if (_document is not { } document)
            return;

        FinishInFlight();

        if (_redirect is null
            && _preferences.AutosaveEnabled
            && document.IsModified
            && HasRecordMoved(document))
        {
            TryWriteNow(document);
        }

        bool orphan = _lease?.Exists == true;

        _lease?.Dispose();
        _lease = null;

        if (orphan)
            _sessionOrphansHashSet.Add(_id);

        _document = null;
        _redirect = null;

        PendingChanged?.Invoke();
        StatusChanged?.Invoke();
    }

    public void Tick(double delta)
    {
        if (_redirect is { } redirect)
        {
            _redirect = null;
            RecoverDirectlyOpened(redirect);

            return;
        }

        if (_failureToReport is { } failure && _arbiter.Status != ControlStatus.Granted)
        {
            _failureToReport = null;
            _dialogs.ShowError("Cannot keep a recovery snapshot", $"{Directory}\n\n{failure}");
        }

        if (_document is not { } document)
            return;

        _elapsed += delta;

        if (_inFlight is { } inFlight)
        {
            if (!inFlight.IsCompleted)
                return;

            CompleteInFlight();
        }

        double interval = _preferences.AutosaveIntervalSeconds;
        bool deferredBefore = IsDeferred(document, interval);

        AutosaveAction action = AutosaveRule.Decide(
            _preferences.AutosaveEnabled,
            due: _elapsed >= interval,
            isModified: document.IsModified,
            recordMoved: HasRecordMoved(document),
            blocked: IsBlocked(),
            overdue: _elapsed >= interval * AutosaveRule.OverdueIntervals,
            hasSnapshot: _hasSnapshot);

        switch (action)
        {
            case AutosaveAction.Rearm:
                _elapsed = 0.0;
                break;

            case AutosaveAction.Drop:
                DropSnapshot(document);
                _elapsed = 0.0;
                StatusChanged?.Invoke();
                break;

            case AutosaveAction.Write:
                BeginWrite(document);
                _elapsed = 0.0;
                StatusChanged?.Invoke();
                break;

            default:
                if (deferredBefore != IsDeferred(document, interval))
                    StatusChanged?.Invoke();

                break;
        }
    }

    /// <summary>
    /// <c>autosave-now</c>: a snapshot at once, on the frame that asked, so an
    /// agent can see the feature work in seconds rather than minutes. Refuses by
    /// name when there is nothing to write.
    /// </summary>
    public bool TryWriteNow(out string reason)
    {
        reason = string.Empty;

        if (_document is not { } document)
        {
            reason = "no document is open";

            return false;
        }

        if (!_preferences.AutosaveEnabled)
        {
            reason = "recovery snapshots are switched off in preferences";

            return false;
        }

        FinishInFlight();

        if (!document.IsModified)
        {
            reason = "the document matches its file — nothing to snapshot";

            return false;
        }

        if (!HasRecordMoved(document))
        {
            reason = "nothing has changed since the last snapshot";

            return false;
        }

        if (!TryWriteNow(document))
        {
            reason = _lastFailure ?? "the snapshot could not be written";

            return false;
        }

        _elapsed = 0.0;
        StatusChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// A save landed, by any route: the snapshot goes, the snapshot point is the
    /// record just saved, and the original path is re-captured — a Save As moves
    /// where the next snapshot says it came from.
    /// </summary>
    public void OnSaved()
    {
        if (_document is not { } document)
            return;

        FinishInFlight();
        DropSnapshot(document);
        _originalPath = _context.Path;
        _elapsed = 0.0;

        StatusChanged?.Invoke();
    }

    /// <summary>
    /// The user's own Discard: the snapshot goes at the moment it is said. If the
    /// operation is then cancelled at a picker, the document stays open and is
    /// snapshotted again only once the record moves — a decision made stands.
    /// </summary>
    public void Forget()
    {
        if (_document is not { } document)
            return;

        FinishInFlight();
        DropSnapshot(document);
        _elapsed = 0.0;

        StatusChanged?.Invoke();
    }

    /// <summary>
    /// The launch flow, and File → Recover unsaved work…: one card per pending
    /// snapshot, newest first, Recover / Discard / Later. Later loses nothing and
    /// stops the run; Recover ends it, one document being open at a time; a
    /// document changing under a card — an agent's <c>new</c> — reads as Later.
    /// </summary>
    /// <param name="confirmDiscardAsync">
    /// The shell's own unsaved-changes question, asked before a recovery replaces
    /// a document with work in it — the same question every other replacement
    /// asks.
    /// </param>
    public async Task OfferRecoveryAsync(Func<Task<bool>> confirmDiscardAsync)
    {
        IReadOnlyList<AutosaveEntry> pending = RecoveryOffer.Order(Scan());

        if (pending.Count == 0)
            return;

        if (RecoveryOffer.NeedsSummary(pending.Count))
        {
            ConfirmResult summary = await _dialogs.AskConfirmAsync(
                AutosaveLabelHelper.DescribeSummary(pending.Count));

            if (!IsAlive)
                return;

            switch (summary)
            {
                case ConfirmResult.Deny:
                    DiscardAll(pending);
                    return;

                case ConfirmResult.Cancel:
                    return;
            }
        }

        for (var index = 0; index < pending.Count; index++)
        {
            AutosaveEntry entry = pending[index];
            DocumentPresenter? before = _document;

            ConfirmResult answer = await _dialogs.AskConfirmAsync(
                AutosaveLabelHelper.DescribeOffer(entry, index + 1, pending.Count));

            if (!IsAlive || !ReferenceEquals(before, _document))
                return;

            switch (answer)
            {
                case ConfirmResult.Confirm:
                    if (!await confirmDiscardAsync() || !IsAlive)
                        return;

                    if (TryRecover(entry.Id, out string error, out _))
                        return;

                    _dialogs.ShowError("Cannot recover", $"{entry.SnapshotPath}\n\n{error}");
                    break;

                case ConfirmResult.Deny:
                    if (!_store.TryDelete(entry.Id, out string deleteError))
                        _dialogs.ShowError("Cannot discard the snapshot", deleteError);

                    _sessionOrphansHashSet.Remove(entry.Id);
                    PendingChanged?.Invoke();
                    break;

                default:
                    return;
            }
        }
    }

    /// <summary>
    /// Opens a snapshot as the document it came from: unsaved, under the original
    /// path unless the file on disk is newer than the snapshot, in which case it
    /// opens untitled. The document it replaces keeps a snapshot of its own work.
    /// </summary>
    public bool TryRecover(Guid id, out string error, out string note)
    {
        error = string.Empty;
        note = string.Empty;

        if (_document is not null && id == _id)
        {
            error = "That is the open document's own snapshot; 'save' is what deletes it.";

            return false;
        }

        if (!_store.TryGetEntry(id, out AutosaveEntry entry))
        {
            error = $"No snapshot with id {id:N}.";

            return false;
        }

        switch (entry.State)
        {
            case AutosaveEntryState.InUse:
                error = $"Snapshot {id:N} is held by a running editor.";
                return false;

            case AutosaveEntryState.Unreadable:
                error = $"Snapshot {id:N} cannot be recovered: {entry.Problem}.";
                return false;
        }

        IProject project;

        try
        {
            project = ModelFactory.LoadProject(entry.SnapshotPath);
        }
        catch (Exception exception)
        {
            // The model's own message — a schema from a newer build, a file that
            // will not parse — and the files left alone for a build that can.
            error = exception.Message;

            return false;
        }

        note = AutosaveLabelHelper.DescribeRecoveryNote(entry.Original);
        _sessionOrphansHashSet.Remove(id);

        Adopt(
            entry,
            project,
            entry.Original == RecoveryOriginal.ChangedSinceSnapshot ? null : entry.OriginalPath);

        PendingChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// Installs <paramref name="project"/> as the document with <paramref name="entry"/>
    /// waiting for the bind that follows. The wait is cleared whether or not that
    /// bind happens: a change that threw part-way must not leave the next,
    /// unrelated document adopting somebody's snapshot.
    /// </summary>
    private void Adopt(AutosaveEntry entry, IProject project, string? path)
    {
        _pendingRecovery = entry;

        try
        {
            _context.SetProject(project, path);
        }
        finally
        {
            _pendingRecovery = null;
        }
    }

    /// <summary>
    /// Deletes a snapshot this session's own commands left behind. Everything else
    /// is refused by name: earlier sessions' snapshots are the user's, and the open
    /// document's own goes with a save.
    /// </summary>
    public bool TryDiscard(Guid id, out string error)
    {
        error = string.Empty;

        if (_document is not null && id == _id)
        {
            error = "That is the open document's own snapshot; 'save' deletes it.";

            return false;
        }

        if (!_sessionOrphansHashSet.Contains(id))
        {
            error = "That snapshot was left by an earlier session and belongs to the user — it is "
                + "offered at launch and under File → Recover unsaved work….";

            return false;
        }

        if (!_store.TryDelete(id, out error))
            return false;

        _sessionOrphansHashSet.Remove(id);
        PendingChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// Deletes every snapshot this session's commands left behind and answers
    /// which. One already gone — another editor, or a hand — is forgotten with
    /// the rest and not reported as deleted.
    /// </summary>
    public IReadOnlyList<Guid> DiscardSessionOrphans()
    {
        var deletedList = new List<Guid>();
        var forgottenList = new List<Guid>();

        foreach (Guid id in _sessionOrphansHashSet)
        {
            bool existed = File.Exists(_store.SnapshotPath(id));

            if (!_store.TryDelete(id, out _))
                continue;

            forgottenList.Add(id);

            if (existed)
                deletedList.Add(id);
        }

        foreach (Guid id in forgottenList)
            _sessionOrphansHashSet.Remove(id);

        if (forgottenList.Count > 0)
            PendingChanged?.Invoke();

        return deletedList;
    }

    public bool IsSessionOrphan(Guid id) => _sessionOrphansHashSet.Contains(id);

    /// <summary>Every snapshot on disk but the open document's own.</summary>
    public IReadOnlyList<AutosaveEntry> Scan()
    {
        var entriesList = new List<AutosaveEntry>();

        foreach (AutosaveEntry entry in _store.Scan())
        {
            if (_document is null || entry.Id != _id)
                entriesList.Add(entry);
        }

        return entriesList;
    }

    public AutosaveStatus Describe()
    {
        double interval = _preferences.AutosaveIntervalSeconds;
        DocumentPresenter? document = _document;

        return new AutosaveStatus(
            _preferences.AutosaveEnabled,
            interval,
            Directory,
            document is null
                ? string.Empty
                : _originalPath is { } path ? Path.GetFileName(path) : WindowTitleHelper.UntitledName,
            document is null ? null : _originalPath,
            document is null ? null : _id,
            _hasSnapshot,
            document is not null && document.IsModified && HasRecordMoved(document),
            document is not null && IsDeferred(document, interval),
            // Until the tick books it, not until the worker finishes: a write the
            // worker has finished and nobody has booked reported neither in
            // flight nor a snapshot, and a caller waiting on the first saw the
            // second still false.
            _inFlight is not null,
            Math.Max(0.0, interval - _elapsed),
            _lastWrittenAt,
            _lastWriteMilliseconds,
            _lastFailure,
            _sessionOrphansHashSet.Count);
    }

    public string DescribeStatusLine() =>
        AutosaveLabelHelper.DescribeStatus(Describe(), PendingCount);

    protected override void OnDispose() => Unbind();

    private void AdoptRecovered(AutosaveEntry recovered)
    {
        try
        {
            _id = recovered.Id;
            _lease = _store.Open(_id);
            _hasSnapshot = _lease.Exists;

            // The snapshot on disk is the one that was recovered, written then;
            // without this the status line would count down to a write that the
            // unmoved record never asks for.
            _lastWrittenAt = recovered.WrittenAt;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Another editor took the lock between the scan and now. A fresh id
            // rather than a shared one; the old pair stays an orphan of theirs.
            _id = Guid.NewGuid();
            _lease = null;
            _hasSnapshot = false;
            RecordFailure(exception.Message);
        }
    }

    /// <summary>
    /// A snapshot that <c>open</c> loaded as if it were a document is re-bound as
    /// a recovery: the project already in memory, under the original path.
    /// Without this a save would go into the snapshot directory and the pair
    /// would stay an orphan offered at the next launch although it was "saved".
    /// </summary>
    private void RecoverDirectlyOpened(Guid id)
    {
        if (!_store.TryGetEntry(id, out AutosaveEntry entry) || !entry.IsRecoverable)
            return;

        _sessionOrphansHashSet.Remove(id);

        Adopt(
            entry,
            _context.Project,
            entry.Original == RecoveryOriginal.ChangedSinceSnapshot ? null : entry.OriginalPath);
    }

    private bool HasRecordMoved(DocumentPresenter document) =>
        RecordIdentity.Current(document.Project.History) != _snapshotRecordId;

    private bool IsBlocked() => _session.HasGesture || _arbiter.IsUserBusy;

    private bool IsDeferred(DocumentPresenter document, double interval) =>
        _preferences.AutosaveEnabled
        && _elapsed >= interval
        && document.IsModified
        && HasRecordMoved(document)
        && IsBlocked()
        && _elapsed < interval * AutosaveRule.OverdueIntervals;

    /// <summary>Capture here, write on a worker; the answer is picked up by a later tick.</summary>
    private void BeginWrite(DocumentPresenter document)
    {
        if (!TryPrepare(document, out PreparedWrite prepared))
            return;

        AutosaveStore.Lease lease = _lease!;

        _inFlightRecordId = prepared.RecordId;
        _inFlightWatch = Stopwatch.StartNew();
        _inFlight = Task.Run(() => lease.Write(
            prepared.Snapshot, prepared.OriginalPath, prepared.OriginalModifiedAt, prepared.Records));
    }

    /// <summary>The same write, finished before returning — for the paths that cannot wait a tick.</summary>
    private bool TryWriteNow(DocumentPresenter document)
    {
        if (!TryPrepare(document, out PreparedWrite prepared))
            return false;

        var watch = Stopwatch.StartNew();

        try
        {
            _lease!.Write(
                prepared.Snapshot, prepared.OriginalPath, prepared.OriginalModifiedAt, prepared.Records);
        }
        catch (Exception exception)
        {
            RecordFailure(exception.Message);

            return false;
        }

        RecordSuccess(prepared.RecordId, watch.Elapsed.TotalMilliseconds);

        return true;
    }

    /// <summary>
    /// The main-thread half of a write: the lease taken if it is not yet, the
    /// document captured, and the original's stamp read now — a save that lands
    /// while the worker runs must not be what the snapshot claims to be taken
    /// over. Failure here is recorded like a failed write.
    /// </summary>
    private bool TryPrepare(DocumentPresenter document, out PreparedWrite prepared)
    {
        prepared = default;

        try
        {
            _lease ??= _store.Open(_id);

            string? originalPath = _originalPath;
            DateTime? originalModifiedAt = originalPath is not null && File.Exists(originalPath)
                ? File.GetLastWriteTimeUtc(originalPath)
                : null;

            prepared = new PreparedWrite(
                document.Project.Capture(),
                originalPath,
                originalModifiedAt,
                document.Project.History.CurrentIndex + 1,
                RecordIdentity.Current(document.Project.History));

            return true;
        }
        catch (Exception exception)
        {
            RecordFailure(exception.Message);

            return false;
        }
    }

    /// <summary>Waits for a worker write to land, then books it — for anything about to touch the files.</summary>
    private void FinishInFlight()
    {
        if (_inFlight is null)
            return;

        try
        {
            _inFlight.Wait();
        }
        catch (AggregateException)
        {
            // Booked below, from the task's own state.
        }

        CompleteInFlight();
    }

    private void CompleteInFlight()
    {
        Task? task = _inFlight;

        if (task is null)
            return;

        _inFlight = null;

        double milliseconds = _inFlightWatch?.Elapsed.TotalMilliseconds ?? 0.0;
        _inFlightWatch = null;

        if (task.IsFaulted)
        {
            Exception? failure = task.Exception?.InnerException ?? task.Exception;
            RecordFailure(failure?.Message ?? "The write failed.");
        }
        else
            RecordSuccess(_inFlightRecordId, milliseconds);

        StatusChanged?.Invoke();
    }

    private void RecordSuccess(Guid recordId, double milliseconds)
    {
        _hasSnapshot = true;
        _snapshotRecordId = recordId;
        _lastWrittenAt = DateTime.UtcNow;
        _lastWriteMilliseconds = milliseconds;
        _lastFailure = null;
    }

    /// <summary>
    /// Kept as state and shown once: on the way into failing, or when the message
    /// changes. A success clears it, so a later failure is reported again. What is
    /// on disk after a failure is asked of the disk — a write that landed the
    /// document and not its sidecar has left a snapshot, whatever this presenter
    /// believed a moment ago.
    /// </summary>
    private void RecordFailure(string message)
    {
        if (!string.Equals(_lastFailure, message, StringComparison.Ordinal))
            _failureToReport = message;

        _lastFailure = message;
        _hasSnapshot = _lease?.Exists == true;
    }

    /// <summary>
    /// Deletes whatever the lease holds on disk — not only what this presenter
    /// remembers writing: the delete is idempotent, and a half-landed write is
    /// exactly the thing a save must not leave behind.
    /// </summary>
    private void DropSnapshot(DocumentPresenter document)
    {
        if (_lease is { } lease)
        {
            try
            {
                lease.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                RecordFailure(exception.Message);

                return;
            }
        }

        _hasSnapshot = false;
        _snapshotRecordId = RecordIdentity.Current(document.Project.History);
    }

    /// <summary>Everything the main thread decided about one write, handed to whichever thread performs it.</summary>
    private readonly record struct PreparedWrite(
        IProjectSnapshot Snapshot,
        string? OriginalPath,
        DateTime? OriginalModifiedAt,
        int Records,
        Guid RecordId);

    private void DiscardAll(IReadOnlyList<AutosaveEntry> entries)
    {
        string? firstError = null;

        foreach (AutosaveEntry entry in entries)
        {
            if (_store.TryDelete(entry.Id, out string error))
                _sessionOrphansHashSet.Remove(entry.Id);
            else
                firstError ??= error;
        }

        PendingChanged?.Invoke();

        if (firstError is not null)
            _dialogs.ShowError("Cannot discard the snapshots", firstError);
    }
}
