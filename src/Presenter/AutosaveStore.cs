using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VoxelCAD.Model;

namespace VoxelCAD.Presenter;

/// <summary>
/// The recovery snapshots on disk: one directory, three files per snapshot, and
/// the rules for naming, locking, listing and removing them. Engine-free, and it
/// knows nothing about presenters or documents — it is handed a captured project
/// to write and answers with what it finds.
/// </summary>
/// <remarks>
/// Per snapshot, named by an id: <c>&lt;id&gt;.vcad</c> is the document exactly as a
/// save writes it, <c>&lt;id&gt;.json</c> is a sidecar naming where it came from and
/// when, and <c>&lt;id&gt;.lock</c> is held open with <see cref="FileShare.None"/> by
/// the editor that owns the snapshot. Being held is the whole of "a running editor
/// owns this": a crash releases it, so the next launch finds an orphan and offers
/// it; no heartbeat, no instance id.
/// <para>
/// Order matters twice. A write lands the document before the sidecar, so a
/// sidecar never names a document that is not there; a delete removes the sidecar
/// before the document, so a crash mid-delete leaves a lone document, which is
/// offered untitled — the safe reading of work of unknown provenance. Both files
/// are replaced by a rename, never rewritten in place.
/// </para>
/// <para>
/// <see cref="Scan"/> never deletes anything. A directory with every kind of
/// residue in it scans identically twice; what is removed is removed by somebody
/// asking.
/// </para>
/// </remarks>
internal sealed class AutosaveStore
{
    public const string DocumentExtension = ProjectFormat.Extension;
    public const string SidecarExtension = ".json";
    public const string LockExtension = ".lock";
    public const string WritingSuffix = ".writing";
    public const string StagingSuffix = ".next" + DocumentExtension;

    private const int SidecarVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly Func<DateTime> _utcNow;

    /// <summary>
    /// A test's hand on the worker: run once the document is in place and before
    /// its sidecar is, so a test can hold a write open there and watch what a
    /// save does meanwhile, or stop it dead where a crash would. Null in the
    /// editor.
    /// </summary>
    private readonly Action? _beforeSidecarWrite;

    /// <summary>
    /// The lock streams this process holds, so a second lease on one id inside
    /// one process is refused the way a second process is — the file system alone
    /// does not always say so for the process that already holds the handle.
    /// </summary>
    private readonly HashSet<Guid> _heldHashSet = new();

    public AutosaveStore(
        string directory,
        Func<DateTime>? utcNow = null,
        Action? beforeSidecarWrite = null)
    {
        Directory = directory;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _beforeSidecarWrite = beforeSidecarWrite;
    }

    public string Directory { get; }

    public string SnapshotPath(Guid id) => Path.Combine(Directory, id.ToString("N") + DocumentExtension);

    public string SidecarPath(Guid id) => Path.Combine(Directory, id.ToString("N") + SidecarExtension);

    public string LockPath(Guid id) => Path.Combine(Directory, id.ToString("N") + LockExtension);

    /// <summary>
    /// Where a new document is written before it replaces the old one. Named so
    /// that the scan does not read it as a snapshot: its stem is not a bare id.
    /// </summary>
    public string StagingPath(Guid id) => Path.Combine(Directory, id.ToString("N") + StagingSuffix);

    /// <summary>
    /// True when <paramref name="path"/> names one of this store's own documents —
    /// a snapshot somebody opened as if it were a project.
    /// </summary>
    public bool TryGetSnapshotId(string? path, out Guid id)
    {
        id = Guid.Empty;

        if (string.IsNullOrEmpty(path) || Directory.Length == 0)
            return false;

        string full;
        string directory;

        try
        {
            full = Path.GetFullPath(path);
            directory = Path.GetFullPath(Directory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }

        if (!string.Equals(
                Path.GetDirectoryName(full),
                directory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.Ordinal))
        {
            return false;
        }

        return TryParseName(Path.GetFileName(full), out id);
    }

    /// <summary>
    /// Every snapshot in the directory, classified. A missing directory is empty,
    /// and so is a file that vanished between being listed and being read — the
    /// directory is shared with any other editor on the machine by design, and
    /// one of them deleting a snapshot is not an error here.
    /// </summary>
    public IReadOnlyList<AutosaveEntry> Scan()
    {
        var entriesList = new List<AutosaveEntry>();
        string[] paths;

        try
        {
            if (!System.IO.Directory.Exists(Directory))
                return entriesList;

            paths = System.IO.Directory.GetFiles(Directory, "*" + DocumentExtension);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return entriesList;
        }

        foreach (string path in paths)
        {
            if (!TryParseName(Path.GetFileName(path), out Guid id))
                continue;

            try
            {
                entriesList.Add(Read(id, path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Gone, or not ours to read right now: not listed rather than thrown
                // into a frame.
            }
        }

        entriesList.Sort((left, right) => right.WrittenAt.CompareTo(left.WrittenAt));

        return entriesList;
    }

    public bool TryGetEntry(Guid id, out AutosaveEntry entry)
    {
        string path = SnapshotPath(id);
        entry = default;

        if (!File.Exists(path))
            return false;

        try
        {
            entry = Read(id, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Takes the lock on <paramref name="id"/>, creating the directory on the way.
    /// Throws <see cref="IOException"/> when another editor — or another lease in
    /// this one — holds it; a caller never opens a held id on purpose.
    /// </summary>
    public Lease Open(Guid id)
    {
        if (_heldHashSet.Contains(id))
            throw new IOException($"Snapshot {id:N} is already held by this editor.");

        System.IO.Directory.CreateDirectory(Directory);

        var stream = new FileStream(
            LockPath(id), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        _heldHashSet.Add(id);

        return new Lease(this, id, stream);
    }

    /// <summary>
    /// Removes a snapshot nobody holds: sidecar, document, then lock. A held one
    /// is refused by name rather than pulled out from under a running editor.
    /// </summary>
    public bool TryDelete(Guid id, out string error)
    {
        error = string.Empty;

        if (IsHeld(id))
        {
            error = $"Snapshot {id:N} is held by a running editor.";

            return false;
        }

        try
        {
            DeleteIfExists(SidecarPath(id));
            DeleteIfExists(SnapshotPath(id));
            DeleteIfExists(StagingPath(id));
            DeleteIfExists(LockPath(id));

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception.Message;

            return false;
        }
    }

    private AutosaveEntry Read(Guid id, string documentPath)
    {
        string sidecarPath = SidecarPath(id);
        long documentBytes = new FileInfo(documentPath).Length;
        AutosaveEntryState state = IsHeld(id)
            ? AutosaveEntryState.InUse
            : AutosaveEntryState.Recoverable;

        if (!File.Exists(sidecarPath))
        {
            // The crash window between the document landing and its sidecar, or a
            // crash mid-delete: work of unknown provenance, offered untitled so a
            // save has to ask where.
            DateTime modified = File.GetLastWriteTimeUtc(documentPath);

            return new AutosaveEntry(
                id, documentPath, null, modified, 0, RecoveryOriginal.Untitled, state, string.Empty);
        }

        Sidecar? sidecar;

        try
        {
            sidecar = JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(sidecarPath), JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException
            or UnauthorizedAccessException)
        {
            return Unreadable(id, documentPath, $"the sidecar cannot be read: {exception.Message}");
        }

        if (sidecar is null || sidecar.Version != SidecarVersion)
        {
            return Unreadable(
                id, documentPath, $"the sidecar is version {sidecar?.Version}; this build reads {SidecarVersion}");
        }

        // The document is written whole or not at all, so a length the sidecar did
        // not record is never a truncated document: it is a sidecar older than the
        // document beside it — a crash between the document landing and its sidecar
        // on a second write. The document is complete and the sidecar's facts are
        // the previous snapshot's, so it is offered as the lone document above is:
        // untitled, from its own time, with nothing claimed about the original.
        if (sidecar.DocumentBytes != documentBytes)
        {
            return new AutosaveEntry(
                id,
                documentPath,
                null,
                File.GetLastWriteTimeUtc(documentPath),
                0,
                RecoveryOriginal.Untitled,
                state,
                string.Empty);
        }

        string? originalPath = string.IsNullOrEmpty(sidecar.OriginalPath) ? null : sidecar.OriginalPath;
        bool exists = originalPath is not null && File.Exists(originalPath);
        DateTime? originalModified = exists ? File.GetLastWriteTimeUtc(originalPath!) : null;

        return new AutosaveEntry(
            id,
            documentPath,
            originalPath,
            DateTime.SpecifyKind(sidecar.WrittenAt, DateTimeKind.Utc),
            sidecar.Records,
            RecoveryOffer.ClassifyOriginal(
                originalPath, exists, originalModified, sidecar.OriginalModifiedAt),
            state,
            string.Empty);
    }

    private AutosaveEntry Unreadable(Guid id, string documentPath, string problem) =>
        new(
            id,
            documentPath,
            null,
            File.GetLastWriteTimeUtc(documentPath),
            0,
            RecoveryOriginal.Untitled,
            AutosaveEntryState.Unreadable,
            problem);

    /// <summary>
    /// Whether anybody holds the lock: this process, by its own list, or another,
    /// found by trying to take it. A lock that cannot even be probed is reported
    /// held — the conservative answer for a file that will not be deleted unasked.
    /// </summary>
    private bool IsHeld(Guid id)
    {
        if (_heldHashSet.Contains(id))
            return true;

        string path = LockPath(id);

        if (!File.Exists(path))
            return false;

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private void Release(Guid id, FileStream stream)
    {
        stream.Dispose();
        _heldHashSet.Remove(id);

        try
        {
            DeleteIfExists(LockPath(id));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A lock file left behind is an orphan by construction — nobody holds it.
        }
    }

    private static bool TryParseName(string fileName, out Guid id)
    {
        id = Guid.Empty;

        string stem = Path.GetFileNameWithoutExtension(fileName);

        return stem.Length == 32 && Guid.TryParseExact(stem, "N", out id);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Written whole or not at all: a sibling, flushed, then a rename over the target.</summary>
    private static void WriteAtomically(string path, string content)
    {
        string writing = path + WritingSuffix;

        try
        {
            using (var stream = new FileStream(writing, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(writing, path, overwrite: true);
        }
        catch
        {
            try
            {
                DeleteIfExists(writing);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The failure being reported is the one that matters.
            }

            throw;
        }
    }

    /// <summary>
    /// One document's hold on one snapshot id, alive for as long as the document
    /// is bound. The lock file is held open throughout and deleted on dispose.
    /// </summary>
    internal sealed class Lease : IDisposable
    {
        private readonly AutosaveStore _store;
        private readonly FileStream _stream;

        private bool _disposed;

        internal Lease(AutosaveStore store, Guid id, FileStream stream)
        {
            _store = store;
            _stream = stream;
            Id = id;
        }

        public Guid Id { get; }

        /// <summary>Whether the document half is on disk right now.</summary>
        public bool Exists => File.Exists(_store.SnapshotPath(Id));

        /// <summary>
        /// The new document is written whole beside the old pair, then the old
        /// sidecar goes, the document is renamed into place, and the new sidecar
        /// is written over it. Safe to run on any thread: the snapshot owes
        /// nothing to the model, and the files are this lease's own.
        /// </summary>
        /// <remarks>
        /// The order is what a failure at each step leaves. The document is
        /// written first, elsewhere, so a disk that is full leaves the previous
        /// pair whole — sidecar included, which is what keeps its path. The old
        /// sidecar goes before the rename, so a crash in the two-syscall window
        /// leaves the previous document alone or the new one alone, both offered
        /// untitled; left standing, an old sidecar beside a new document would
        /// name the wrong facts about it, and the scan reads that as stale too.
        /// </remarks>
        /// <param name="originalModifiedAt">
        /// The original file's stamp as read on the main thread at the capture,
        /// not here: a save landing while this runs on a worker must not be
        /// recorded as the state this snapshot was taken over.
        /// </param>
        public void Write(
            IProjectSnapshot snapshot,
            string? originalPath,
            DateTime? originalModifiedAt,
            int records)
        {
            EnsureLive();

            string documentPath = _store.SnapshotPath(Id);
            string stagingPath = _store.StagingPath(Id);

            snapshot.Write(stagingPath);
            DeleteIfExists(_store.SidecarPath(Id));
            File.Move(stagingPath, documentPath, overwrite: true);
            _store._beforeSidecarWrite?.Invoke();

            var sidecar = new Sidecar
            {
                Version = SidecarVersion,
                Id = Id.ToString("N"),
                OriginalPath = originalPath,
                WrittenAt = _store._utcNow(),
                OriginalModifiedAt = originalModifiedAt,
                Records = records,
                DocumentBytes = new FileInfo(documentPath).Length
            };

            WriteAtomically(_store.SidecarPath(Id), JsonSerializer.Serialize(sidecar, JsonOptions));
        }

        /// <summary>Sidecar first, then the document; the lock stays held.</summary>
        public void Delete()
        {
            EnsureLive();

            DeleteIfExists(_store.SidecarPath(Id));
            DeleteIfExists(_store.SnapshotPath(Id));
            DeleteIfExists(_store.StagingPath(Id));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _store.Release(Id, _stream);
        }

        private void EnsureLive()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Lease));
        }
    }

    /// <summary>The sidecar, camelCase on disk like the preferences file.</summary>
    private sealed class Sidecar
    {
        public int Version { get; set; }

        public string Id { get; set; } = string.Empty;

        public string? OriginalPath { get; set; }

        public DateTime WrittenAt { get; set; }

        /// <summary>The original file's modification time at the write; null when it did not exist.</summary>
        public DateTime? OriginalModifiedAt { get; set; }

        public int Records { get; set; }

        public long DocumentBytes { get; set; }
    }
}
