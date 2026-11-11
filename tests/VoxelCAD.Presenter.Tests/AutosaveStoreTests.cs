using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The snapshot directory as a file system: what a write leaves, what a scan
/// finds, what a lock means, and what is never deleted unasked.
/// </summary>
public sealed class AutosaveStoreTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"voxelcad-autosave-{Guid.NewGuid():N}");

    private readonly string _projectDirectory = Path.Combine(
        Path.GetTempPath(),
        $"voxelcad-project-{Guid.NewGuid():N}");

    private readonly List<AutosaveStore.Lease> _leasesList = new();

    private DateTime _now = Noon;

    public AutosaveStoreTests()
    {
        StandardMaterials.Install();
        Directory.CreateDirectory(_projectDirectory);
    }

    public void Dispose()
    {
        foreach (AutosaveStore.Lease lease in _leasesList)
            lease.Dispose();

        foreach (string directory in new[] { _directory, _projectDirectory })
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AWriteLandsBothHalvesAndLeavesNoWritingFile()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());

        lease.Write(Project(1).Capture(), "/models/part.vcad", null, 1);

        Assert.True(File.Exists(store.SnapshotPath(lease.Id)));
        Assert.True(File.Exists(store.SidecarPath(lease.Id)));
        Assert.True(File.Exists(store.LockPath(lease.Id)));
        Assert.Empty(Directory.GetFiles(_directory, "*.writing"));
        Assert.True(lease.Exists);
    }

    [Fact]
    public void TheSidecarCarriesThePathTheRecordsAndTheBytes()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());

        lease.Write(Project(3).Capture(), "/models/part.vcad", null, 3);
        lease.Dispose();

        string sidecar = File.ReadAllText(store.SidecarPath(lease.Id));
        AutosaveEntry entry = Assert.Single(store.Scan());

        Assert.Contains("\"originalPath\": \"/models/part.vcad\"", sidecar);
        Assert.Equal("/models/part.vcad", entry.OriginalPath);
        Assert.Equal(3, entry.Records);
        Assert.Equal(Noon, entry.WrittenAt);
        Assert.Equal(lease.Id, entry.Id);
        Assert.Contains($"\"documentBytes\": {new FileInfo(store.SnapshotPath(lease.Id)).Length}", sidecar);
    }

    [Fact]
    public void AFailedWriteLeavesThePreviousSnapshotIntact()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());

        lease.Write(Project(1).Capture(), null, null, 1);
        byte[] before = File.ReadAllBytes(store.SnapshotPath(lease.Id));

        // A directory where the new document is written first makes the next
        // write fail before it can replace anything — the previous pair keeps
        // its sidecar and with it its path and its count.
        string writing = store.StagingPath(lease.Id) + AutosaveStore.WritingSuffix;
        Directory.CreateDirectory(writing);

        try
        {
            Assert.ThrowsAny<Exception>(() => lease.Write(Project(2).Capture(), null, null, 2));
        }
        finally
        {
            Directory.Delete(writing);
        }

        Assert.Equal(before, File.ReadAllBytes(store.SnapshotPath(lease.Id)));
        Assert.Equal(1, Assert.Single(store.Scan().Where(entry => entry.Id == lease.Id)).Records);
    }

    [Fact]
    public void ScanListsOrphansNewestFirst()
    {
        AutosaveStore store = Store();

        AutosaveStore.Lease older = Open(store, Guid.NewGuid());
        older.Write(Project(1).Capture(), "/models/older.vcad", null, 1);
        older.Dispose();

        _now = Noon.AddMinutes(10);
        AutosaveStore.Lease newer = Open(store, Guid.NewGuid());
        newer.Write(Project(1).Capture(), "/models/newer.vcad", null, 1);
        newer.Dispose();

        Assert.Collection(
            store.Scan(),
            entry => Assert.Equal("/models/newer.vcad", entry.OriginalPath),
            entry => Assert.Equal("/models/older.vcad", entry.OriginalPath));
        Assert.All(store.Scan(), entry => Assert.Equal(AutosaveEntryState.Recoverable, entry.State));
    }

    /// <summary>A directory with every kind of residue in it scans identically twice.</summary>
    [Fact]
    public void ScanNeverDeletesAnything()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), null, null, 1);
        lease.Dispose();

        File.WriteAllText(Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".vcad"), "version: 5\n");
        File.WriteAllText(Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json"), "{}");
        File.WriteAllText(Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".vcad.writing"), "half");
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "somebody's file");

        string[] before = Directory.GetFileSystemEntries(_directory).OrderBy(name => name).ToArray();

        store.Scan();
        store.Scan();

        Assert.Equal(before, Directory.GetFileSystemEntries(_directory).OrderBy(name => name).ToArray());
    }

    [Fact]
    public void AHeldLeaseIsReportedInUseAndRefusedToOthers()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), null, null, 1);

        Assert.Throws<IOException>(() => store.Open(lease.Id));
        Assert.Throws<IOException>(() => Store().Open(lease.Id));
        Assert.Equal(AutosaveEntryState.InUse, Assert.Single(store.Scan()).State);
        Assert.Equal(AutosaveEntryState.InUse, Assert.Single(Store().Scan()).State);
        Assert.False(Store().TryDelete(lease.Id, out string error));
        Assert.Contains("held", error);
        Assert.True(File.Exists(store.SnapshotPath(lease.Id)));
    }

    [Fact]
    public void DisposingALeaseFreesItsLock()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), null, null, 1);

        lease.Dispose();

        Assert.False(File.Exists(store.LockPath(lease.Id)));
        Assert.Equal(AutosaveEntryState.Recoverable, Assert.Single(Store().Scan()).State);
        Open(Store(), lease.Id).Dispose();
    }

    /// <summary>
    /// The crash window between the document landing and its sidecar, or a crash
    /// mid-delete: work of unknown provenance, offered untitled.
    /// </summary>
    [Fact]
    public void ALoneDocumentIsOfferedUntitledFromItsMtime()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), "/models/part.vcad", null, 1);
        lease.Dispose();

        File.Delete(store.SidecarPath(lease.Id));
        File.SetLastWriteTimeUtc(store.SnapshotPath(lease.Id), Noon.AddHours(-1));

        AutosaveEntry entry = Assert.Single(store.Scan());

        Assert.True(entry.IsRecoverable);
        Assert.Null(entry.OriginalPath);
        Assert.Equal(RecoveryOriginal.Untitled, entry.Original);
        Assert.Equal(Noon.AddHours(-1), entry.WrittenAt);
        Assert.Equal(0, entry.Records);
    }

    /// <summary>
    /// A document is written whole or not at all, so a length the sidecar did not
    /// record means the sidecar is the previous snapshot's — a crash between the
    /// document landing and its sidecar. The document is complete and is offered
    /// untitled, on the lone document's own terms; calling it truncated would hide
    /// the newest work behind a word.
    /// </summary>
    [Fact]
    public void AStaleSidecarOffersTheDocumentUntitledRatherThanCallingItTruncated()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), "/models/part.vcad", null, 1);
        string firstSidecar = File.ReadAllText(store.SidecarPath(lease.Id));

        lease.Write(Project(5).Capture(), "/models/part.vcad", null, 5);
        lease.Dispose();

        File.WriteAllText(store.SidecarPath(lease.Id), firstSidecar);

        AutosaveEntry entry = Assert.Single(store.Scan());

        Assert.True(entry.IsRecoverable);
        Assert.Null(entry.OriginalPath);
        Assert.Equal(RecoveryOriginal.Untitled, entry.Original);
        Assert.Equal(0, entry.Records);
        Assert.Equal(5, ModelFactory.LoadProject(entry.SnapshotPath).Scenes[0].Children.Count);
    }

    /// <summary>
    /// The window itself: the worker is stopped once the new document is in
    /// place and before its sidecar. What is on disk is the new document and no
    /// sidecar at all — the old one went first — which the scan reads as the
    /// lone document: complete, and offered untitled.
    /// </summary>
    [Fact]
    public void ACrashBetweenTheDocumentAndTheSidecarLeavesAnUntitledOffer()
    {
        var stopBeforeSidecar = false;
        var store = new AutosaveStore(_directory, () => _now, () =>
        {
            if (stopBeforeSidecar)
                throw new IOException("the process died here");
        });
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), "/models/part.vcad", null, 1);

        stopBeforeSidecar = true;
        Assert.Throws<IOException>(() => lease.Write(Project(3).Capture(), "/models/part.vcad", null, 3));
        lease.Dispose();

        Assert.False(File.Exists(store.SidecarPath(lease.Id)));
        Assert.False(File.Exists(store.StagingPath(lease.Id)));
        AutosaveEntry entry = Assert.Single(store.Scan());
        Assert.True(entry.IsRecoverable);
        Assert.Equal(RecoveryOriginal.Untitled, entry.Original);
        Assert.Equal(3, ModelFactory.LoadProject(entry.SnapshotPath).Scenes[0].Children.Count);
    }

    /// <summary>A stray staging file is not a snapshot: the scan steps over it and a delete takes it.</summary>
    [Fact]
    public void AStagingFileIsNeverListedAndGoesWithADelete()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), null, null, 1);
        lease.Dispose();
        File.Copy(store.SnapshotPath(lease.Id), store.StagingPath(lease.Id));

        Assert.Single(store.Scan());
        Assert.True(store.TryDelete(lease.Id, out string error), error);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    /// <summary>
    /// What a killed process leaves: the pair and a lock file nobody holds. It is
    /// not held, it can be adopted, and a delete takes all three.
    /// </summary>
    [Fact]
    public void AStaleLockLeftByACrashIsNotHeld()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), null, null, 1);
        lease.Dispose();
        File.WriteAllText(store.LockPath(lease.Id), string.Empty);

        Assert.Equal(AutosaveEntryState.Recoverable, Assert.Single(Store().Scan()).State);

        AutosaveStore.Lease adopted = Open(Store(), lease.Id);
        Assert.True(adopted.Exists);
        adopted.Dispose();

        File.WriteAllText(store.LockPath(lease.Id), string.Empty);
        Assert.True(Store().TryDelete(lease.Id, out string error), error);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void AnUnparseableSidecarIsUnreadableAndNeverOffered()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), null, null, 1);
        lease.Dispose();

        File.WriteAllText(store.SidecarPath(lease.Id), "{ not json");

        AutosaveEntry entry = Assert.Single(store.Scan());

        Assert.Equal(AutosaveEntryState.Unreadable, entry.State);
        Assert.Contains("sidecar", entry.Problem);
        Assert.Empty(RecoveryOffer.Order(store.Scan()));
    }

    [Fact]
    public void ALoneSidecarIsNotListed()
    {
        AutosaveStore store = Store();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.SidecarPath(Guid.NewGuid()), "{ \"version\": 1 }");

        Assert.Empty(store.Scan());
    }

    [Fact]
    public void DeleteRemovesSidecarThenDocumentThenLock()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), null, null, 1);
        lease.Dispose();
        File.WriteAllText(store.LockPath(lease.Id), string.Empty);

        Assert.Equal(3, Directory.GetFiles(_directory).Length);
        Assert.True(store.TryDelete(lease.Id, out string error), error);

        Assert.Empty(Directory.GetFiles(_directory));
        Assert.False(store.TryGetEntry(lease.Id, out _));
    }

    [Fact]
    public void ALeaseCanDeleteItsOwnPairAndKeepTheLock()
    {
        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), null, null, 1);

        lease.Delete();

        Assert.False(lease.Exists);
        Assert.False(File.Exists(store.SidecarPath(lease.Id)));
        Assert.True(File.Exists(store.LockPath(lease.Id)));
        Assert.Empty(store.Scan());
    }

    /// <summary>
    /// The sidecar records the original's own stamp at the write; the scan
    /// compares against that, so an unchanged file is Present whatever the
    /// clocks did, and any write since — forward or back — is a change.
    /// </summary>
    [Fact]
    public void TheOriginalIsClassifiedAgainstTheStampTheSnapshotRecorded()
    {
        AutosaveStore store = Store();
        string original = Path.Combine(_projectDirectory, "part.vcad");
        File.WriteAllText(original, "version: 5\n");

        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(Project(1).Capture(), original, File.GetLastWriteTimeUtc(original), 1);
        lease.Dispose();

        Assert.Equal(RecoveryOriginal.Present, Assert.Single(store.Scan()).Original);
        Assert.Contains("\"originalModifiedAt\":", File.ReadAllText(store.SidecarPath(lease.Id)));

        File.SetLastWriteTimeUtc(original, Noon.AddMinutes(5));
        Assert.Equal(RecoveryOriginal.ChangedSinceSnapshot, Assert.Single(store.Scan()).Original);

        File.SetLastWriteTimeUtc(original, Noon.AddMinutes(-5));
        Assert.Equal(RecoveryOriginal.ChangedSinceSnapshot, Assert.Single(store.Scan()).Original);

        File.Delete(original);
        Assert.Equal(RecoveryOriginal.Missing, Assert.Single(store.Scan()).Original);
        Assert.Equal("part.vcad", Assert.Single(store.Scan()).Title);
    }

    [Fact]
    public void AMissingDirectoryScansEmpty()
    {
        AutosaveStore store = Store();

        Assert.Empty(store.Scan());
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void TryGetSnapshotIdRecognisesOnlyItsOwnFiles()
    {
        AutosaveStore store = Store();
        Guid id = Guid.NewGuid();

        Assert.True(store.TryGetSnapshotId(store.SnapshotPath(id), out Guid found));
        Assert.Equal(id, found);
        Assert.False(store.TryGetSnapshotId(Path.Combine(_directory, "part.vcad"), out _));
        Assert.False(store.TryGetSnapshotId(Path.Combine(_projectDirectory, id.ToString("N") + ".vcad"), out _));
        Assert.False(store.TryGetSnapshotId(null, out _));
        Assert.False(store.TryGetSnapshotId(string.Empty, out _));
    }

    /// <summary>
    /// A snapshot is an ordinary project file: what makes it recoverable is that
    /// everything a file carries survives it — a prefab instance's deterministic
    /// child ids and a reference's absolute path among them.
    /// </summary>
    [Fact]
    public void ARecoveredProjectKeepsItsIdsAndReferencePaths()
    {
        IProject project = ModelFactory.CreateProject();
        IScene scene = project.Scenes[0];
        Guid group = scene.CreateElement(scene.Id, ComponentKind.None, "Assembly");
        Guid model = scene.CreateElement(group, ComponentKind.VoxelModel, "Part");
        scene.SetVoxels(model, new[] { new VoxelEdit(new Vector3I(1, 2, 3), project.Materials[0].Id) });

        string asset = Path.Combine(_projectDirectory, "bust.glb");
        File.WriteAllBytes(asset, new byte[] { 1, 2, 3 });
        scene.CreateReference(scene.Id, "Bust", asset);

        Guid prefab = scene.CreatePrefab(group);
        Guid instance = scene.CreateInstance(scene.Id, prefab);
        Assert.True(scene.TryGetElement(instance, out IHierarchyElement instanceElement));
        Guid instanceChild = instanceElement.Children[0].Id;

        AutosaveStore store = Store();
        AutosaveStore.Lease lease = Open(store, Guid.NewGuid());
        lease.Write(project.Capture(), null, null, project.History.CurrentIndex + 1);

        IProject loaded = ModelFactory.LoadProject(store.SnapshotPath(lease.Id));
        IScene loadedScene = loaded.Scenes[0];

        Assert.True(loadedScene.TryGetElement(instanceChild, out IHierarchyElement child));
        Assert.Equal("Assembly", child.Name);

        IHierarchyElement bust = loadedScene.Children.First(element => element.Name == "Bust");
        Assert.Equal(asset, ((IReferenceModel)bust.Component!).Settings.Path);
    }

    private AutosaveStore Store() => new(_directory, () => _now);

    private AutosaveStore.Lease Open(AutosaveStore store, Guid id)
    {
        AutosaveStore.Lease lease = store.Open(id);
        _leasesList.Add(lease);

        return lease;
    }

    private static IProject Project(int edits)
    {
        IProject project = ModelFactory.CreateProject();
        IScene scene = project.Scenes[0];

        for (var index = 0; index < edits; index++)
            scene.CreateElement(scene.Id, ComponentKind.None, $"Group {index}");

        return project;
    }
}
