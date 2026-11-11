using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Presenter;
using Xunit;
using static VoxelCAD.Presenter.Tests.AutosaveTestKit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The recovery snapshot over a whole shell: when one is written, when it goes,
/// what it says, and what an agent's replacements leave behind.
/// </summary>
public sealed class AutosaveTests : IDisposable
{
    private readonly string _directory = TempDirectory("autosave");
    private readonly string _projectDirectory = TempDirectory("project");
    private readonly FakeShellView _view = new();
    private readonly FakePreferences _preferences;
    private readonly ShellPresenter _shell;
    private readonly ICommandDispatcher _agent;

    public AutosaveTests()
    {
        _preferences = PreferencesFor(_directory);
        _shell = new ShellPresenter(_view, _preferences);
        _agent = _shell.CreateAgentDispatchers().ForClient("autosave-client");
        Directory.CreateDirectory(_projectDirectory);

        // The first tick is the launch; nothing is pending, so it asks nothing.
        _shell.Tick(0.0);
    }

    public void Dispose()
    {
        _shell.Dispose();
        Delete(_directory);
        Delete(_projectDirectory);
    }

    [Fact]
    public void NothingIsWrittenForAnUneditedDocument()
    {
        _shell.Tick(500.0);

        Assert.False(Directory.Exists(_directory));
        Assert.Equal(false, Document(_agent)["hasSnapshot"]);
    }

    /// <summary>
    /// A write the worker has finished is still in flight until a tick books it,
    /// so the two fields change together. They used to part in between: the
    /// status asked whether the worker's task had completed, the snapshot was
    /// booked a tick later, and a caller waiting for the write to stop being in
    /// flight could read <c>hasSnapshot</c> while it was still false — the
    /// intermittent failure <see cref="Settle"/>'s callers had.
    /// </summary>
    [Fact]
    public void AFinishedWriteStaysInFlightUntilATickBooksIt()
    {
        _view.RaiseCreateGroup();
        _shell.Tick(120.0);

        // The worker lands the sidecar last, then returns; give it the time to
        // return without a tick to book what it did.
        for (var attempt = 0; attempt < 1000 && Sidecars(_directory).Length == 0; attempt++)
            System.Threading.Thread.Sleep(2);

        System.Threading.Thread.Sleep(200);

        Assert.Equal(true, Document(_agent)["writeInFlight"]);
        Assert.Equal(false, Document(_agent)["hasSnapshot"]);

        Settle(_shell, _agent);

        Assert.Equal(false, Document(_agent)["writeInFlight"]);
        Assert.Equal(true, Document(_agent)["hasSnapshot"]);
    }

    [Fact]
    public void AnEditIsSnapshottedWhenTheIntervalElapses()
    {
        _view.RaiseCreateGroup();

        _shell.Tick(119.0);
        Assert.Empty(Documents(_directory));
        Assert.Equal(true, Document(_agent)["pendingChanges"]);

        _shell.Tick(1.0);
        Settle(_shell, _agent);

        Assert.Single(Documents(_directory));
        Assert.Single(Sidecars(_directory));
        Assert.Single(Locks(_directory));
        Assert.Equal(true, Document(_agent)["hasSnapshot"]);
        Assert.Equal(false, Document(_agent)["pendingChanges"]);
        Assert.Null(SidecarOriginalPath(_directory));
        Assert.NotNull(Document(_agent)["lastWriteMilliseconds"]);
    }

    [Fact]
    public void NothingMovedNothingWritten()
    {
        _view.RaiseCreateGroup();
        _shell.Tick(120.0);
        Settle(_shell, _agent);
        DateTime firstWrite = File.GetLastWriteTimeUtc(Documents(_directory)[0]);

        _shell.Tick(120.0);
        Settle(_shell, _agent);

        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(Documents(_directory)[0]));
    }

    [Fact]
    public void TheSidecarCarriesThePathAfterASave()
    {
        string path = Path.Combine(_projectDirectory, "part.vcad");
        _view.OnAskPath = _ => Task.FromResult<string?>(path);
        _view.RaiseCreateGroup();
        _view.RaiseSave();

        _view.RaiseCreateVoxelModel();
        _shell.Tick(120.0);
        Settle(_shell, _agent);

        Assert.Equal(path, SidecarOriginalPath(_directory));
        Assert.Equal(path, Document(_agent)["originalPath"]);
    }

    [Fact]
    public void ASaveDeletesTheSnapshot()
    {
        _view.OnAskPath = _ => Task.FromResult<string?>(Path.Combine(_projectDirectory, "part.vcad"));
        _view.RaiseCreateGroup();
        _shell.Tick(120.0);
        Settle(_shell, _agent);
        Assert.Single(Documents(_directory));

        _view.RaiseSave();

        Assert.Empty(Documents(_directory));
        Assert.Empty(Sidecars(_directory));
        Assert.Single(Locks(_directory));
        Assert.Equal(false, Document(_agent)["hasSnapshot"]);
    }

    [Fact]
    public void AnAgentsSaveDeletesItToo()
    {
        _view.RaiseCreateGroup();
        _shell.Tick(120.0);
        Settle(_shell, _agent);

        TakeControl();
        CommandResult result = _agent.Execute(
            "save",
            new Dictionary<string, string> { ["path"] = Path.Combine(_projectDirectory, "part.vcad") });

        Assert.True(result.Ok, result.Error);
        Assert.Empty(Documents(_directory));
    }

    [Fact]
    public void SaveAsMovesTheOriginalPath()
    {
        string first = Path.Combine(_projectDirectory, "first.vcad");
        string second = Path.Combine(_projectDirectory, "second.vcad");
        _view.OnAskPath = _ => Task.FromResult<string?>(first);
        _view.RaiseCreateGroup();
        _view.RaiseSave();

        _view.OnAskPath = _ => Task.FromResult<string?>(second);
        _view.RaiseCreateVoxelModel();
        _view.RaiseSaveAs();

        _view.RaiseCreateGroup();
        _shell.Tick(120.0);
        Settle(_shell, _agent);

        Assert.Equal(second, SidecarOriginalPath(_directory));
    }

    /// <summary>
    /// The worker's own failure — the document could not be replaced — is booked
    /// by the tick that finds the task faulted: one card, the previous snapshot
    /// intact, a retry that clears it. Distinct from a failure to take the lease,
    /// which never reaches the worker at all.
    /// </summary>
    [Fact]
    public void AWorkerWriteFailureIsBookedReportedOnceAndRetried()
    {
        _view.RaiseCreateGroup();
        _shell.Tick(120.0);
        Settle(_shell, _agent);
        string snapshot = Documents(_directory)[0];
        byte[] first = File.ReadAllBytes(snapshot);

        // A directory where the new document is written first: the worker fails
        // after the capture, inside the write, and the previous pair stays whole.
        string writing = Path.ChangeExtension(snapshot, AutosaveStore.StagingSuffix) + AutosaveStore.WritingSuffix;
        Directory.CreateDirectory(writing);
        _view.RaiseCreateVoxelModel();
        _shell.Tick(120.0);
        Settle(_shell, _agent);
        _shell.Tick(0.0);

        Assert.NotNull(Document(_agent)["lastFailure"]);
        Assert.Single(_view.Errors);
        Assert.Equal(first, File.ReadAllBytes(snapshot));

        _view.RaiseCreateGroup();
        _shell.Tick(120.0);
        Settle(_shell, _agent);
        Assert.Single(_view.Errors);

        Directory.Delete(writing);
        _view.RaiseCreateGroup();
        _shell.Tick(120.0);
        Settle(_shell, _agent);

        Assert.Null(Document(_agent)["lastFailure"]);
        Assert.NotEqual(first, File.ReadAllBytes(snapshot));
    }

    /// <summary>
    /// The half-landed write: the document went, the sidecar did not. A save must
    /// still leave nothing behind — what is on disk is deleted, not what the
    /// presenter remembers writing.
    /// </summary>
    [Fact]
    public void ASaveDeletesADocumentWhoseSidecarFailedToLand()
    {
        _view.RaiseCreateGroup();
        TakeControl();
        Directory.CreateDirectory(_directory);
        string id = (string)Document(_agent)["snapshotId"]!;
        Directory.CreateDirectory(Path.Combine(_directory, id + ".json.writing"));

        IReadOnlyDictionary<string, object?> answer =
            (IReadOnlyDictionary<string, object?>)_agent.Execute("autosave-now", new Dictionary<string, string>()).Value!;

        Assert.Equal(false, answer["written"]);
        Assert.Single(Documents(_directory));
        Assert.Equal(true, Document(_agent)["hasSnapshot"]);

        _agent.Execute("save", new Dictionary<string, string> { ["path"] = Path.Combine(_projectDirectory, "part.vcad") });

        Assert.Empty(Documents(_directory));
    }

    /// <summary>
    /// The canon's "a stale write can never land after a delete", held: a save
    /// issued while the worker is still writing waits for it, and the delete
    /// that follows takes what the worker landed.
    /// </summary>
    [Fact]
    public void ASaveWaitsForAWriteInFlightAndThenDeletesIt()
    {
        var gate = new System.Threading.ManualResetEventSlim(false);
        var view = new FakeShellView();
        var shell = new ShellPresenter(
            view,
            PreferencesFor(_directory),
            new AutosaveStore(_directory, null, () => gate.Wait()));
        ICommandDispatcher agent = shell.CreateAgentDispatchers().ForClient("gate-client");
        string path = Path.Combine(_projectDirectory, "part.vcad");
        view.OnAskPath = _ => Task.FromResult<string?>(path);

        try
        {
            shell.Tick(0.0);
            view.RaiseCreateGroup();
            shell.Tick(120.0);
            Assert.Equal(true, Document(agent)["writeInFlight"]);

            Task save = Task.Run(view.RaiseSave);

            Assert.False(save.Wait(300));
            Assert.True(File.Exists(path));

            gate.Set();
            Assert.True(save.Wait(10000));

            Assert.Empty(Documents(_directory));
            Assert.Empty(Sidecars(_directory));
            Assert.Equal(false, Document(agent)["hasSnapshot"]);
        }
        finally
        {
            gate.Set();
            shell.Dispose();
        }
    }

    [Fact]
    public void DiscardDeletesTheSnapshotAtTheMomentItIsSaid()
    {
        _view.RaiseCreateGroup();
        _shell.Tick(120.0);
        Settle(_shell, _agent);
        Assert.Single(Documents(_directory));

        _view.OnAskConfirm = _ => Task.FromResult(ConfirmResult.Deny);
        _view.RaiseNew();

        Assert.Empty(Documents(_directory));
        Assert.Empty(Sidecars(_directory));
        Assert.Equal("Untitled — VoxelCAD", _view.WindowTitle);
    }

    /// <summary>
    /// Discard, then cancel at the picker the discard was for: the decision
    /// stands, and the document is snapshotted again only once it moves.
    /// </summary>
    [Fact]
    public void ADiscardedDocumentIsSnapshottedAgainOnceItMoves()
    {
        _view.RaiseCreateGroup();
        _shell.Tick(120.0);
        Settle(_shell, _agent);

        _view.OnAskConfirm = _ => Task.FromResult(ConfirmResult.Deny);
        _view.OnAskPath = _ => Task.FromResult<string?>(null);
        _view.RaiseOpen();

        Assert.Empty(Documents(_directory));
        Assert.StartsWith("•", _view.WindowTitle);

        _shell.Tick(120.0);
        Settle(_shell, _agent);
        Assert.Empty(Documents(_directory));

        _view.RaiseCreateVoxelModel();
        _shell.Tick(120.0);
        Settle(_shell, _agent);

        Assert.Single(Documents(_directory));
    }

    [Fact]
    public void AGestureInFlightDefersTheWriteUntilItEnds()
    {
        _view.RaiseCreateGroup();
        _view.HierarchyView.RaiseItemClicked(Material(_view));
        _view.InspectorView.RaiseColorEditBegan();

        _shell.Tick(120.0);
        Settle(_shell, _agent);

        Assert.Empty(Documents(_directory));
        Assert.Equal(true, Document(_agent)["deferred"]);

        _view.InspectorView.RaiseColorEditEnded();
        _shell.Tick(0.016);
        Settle(_shell, _agent);

        Assert.Single(Documents(_directory));
        Assert.Equal(false, Document(_agent)["deferred"]);
    }

    [Fact]
    public void ABusyUserDefersTheWrite()
    {
        _view.RaiseCreateGroup();
        _view.RaiseInteractionBegan();

        _shell.Tick(120.0);
        Settle(_shell, _agent);
        Assert.Empty(Documents(_directory));

        _view.RaiseInteractionEnded();
        _shell.Tick(0.016);
        Settle(_shell, _agent);

        Assert.Single(Documents(_directory));
    }

    /// <summary>The deferral is bounded: past one further interval the write goes through.</summary>
    [Fact]
    public void AnOverdueWriteGoesThroughAnOpenGesture()
    {
        _view.RaiseCreateGroup();
        _view.HierarchyView.RaiseItemClicked(Material(_view));
        _view.InspectorView.RaiseColorEditBegan();

        _shell.Tick(120.0);
        _shell.Tick(120.0);
        Settle(_shell, _agent);

        Assert.Single(Documents(_directory));

        // The gesture is still the interface's: ending it files whatever it held.
        _view.InspectorView.RaiseColorEditEnded();
    }

    [Fact]
    public void UndoingBackToTheSavedPointDropsTheSnapshot()
    {
        _view.OnAskPath = _ => Task.FromResult<string?>(Path.Combine(_projectDirectory, "part.vcad"));
        _view.RaiseCreateGroup();
        _view.RaiseSave();

        _view.RaiseCreateVoxelModel();
        _shell.Tick(120.0);
        Settle(_shell, _agent);
        Assert.Single(Documents(_directory));

        _view.RaiseUndo();
        _shell.Tick(120.0);

        Assert.Empty(Documents(_directory));
        Assert.Equal(false, Document(_agent)["hasSnapshot"]);
    }

    [Fact]
    public void AChangedIntervalAppliesWithoutARestart()
    {
        _view.RaiseCreateGroup();
        _preferences.Apply(_preferences.GetValues() with { AutosaveIntervalSeconds = 30.0 });

        _shell.Tick(30.0);
        Settle(_shell, _agent);

        Assert.Single(Documents(_directory));
        Assert.Equal(30.0, Status(_agent)["intervalSeconds"]);
    }

    [Fact]
    public void TheSwitchOffWritesNothingAndDeletesNothing()
    {
        _view.RaiseCreateGroup();
        _shell.Tick(120.0);
        Settle(_shell, _agent);
        Assert.Single(Documents(_directory));

        _preferences.Apply(_preferences.GetValues() with { AutosaveEnabled = false });
        _view.RaiseCreateVoxelModel();
        _shell.Tick(120.0);
        Settle(_shell, _agent);

        Assert.Single(Documents(_directory));
        Assert.Equal(false, Status(_agent)["enabled"]);
        Assert.StartsWith("Off.", (string)Status(_agent)["status"]!);
    }

    [Fact]
    public void TheWriteRunsWhileAnAgentHoldsControl()
    {
        TakeControl();
        _agent.Execute("create-element", new Dictionary<string, string> { ["name"] = "Widget" });

        _shell.Tick(120.0);
        Settle(_shell, _agent);

        Assert.Single(Documents(_directory));
    }

    /// <summary>
    /// An agent's 'new' replaces the document without asking. The work is written
    /// on the way out and kept — listed at once, offered at the next launch — and
    /// it is this session's to discard.
    /// </summary>
    [Fact]
    public void AnAgentsNewOverADirtyDocumentKeepsItsWorkAsAnOrphan()
    {
        _view.RaiseCreateGroup();
        TakeControl();

        _agent.Execute("new", new Dictionary<string, string>());

        Assert.Single(Documents(_directory));
        Assert.Empty(Locks(_directory));

        IReadOnlyDictionary<string, object?> orphan = Assert.Single(Snapshots(_agent));
        Assert.Equal("recoverable", orphan["state"]);
        Assert.Equal(true, orphan["sessionOrphan"]);
        Assert.Equal("untitled", orphan["original"]);
        Assert.Equal(1, orphan["records"]);
        Assert.True(_view.RecoverEnabled);
    }

    [Fact]
    public void DisposingWithUnsavedWorkLeavesASnapshot()
    {
        var view = new FakeShellView();
        var shell = new ShellPresenter(view, PreferencesFor(_directory));
        view.RaiseCreateGroup();

        shell.Dispose();

        Assert.Single(Documents(_directory));
        Assert.Empty(Locks(_directory));
    }

    [Fact]
    public void DisposingAfterASaveLeavesNothing()
    {
        var view = new FakeShellView();
        var shell = new ShellPresenter(view, PreferencesFor(_directory));
        view.OnAskPath = _ => Task.FromResult<string?>(Path.Combine(_projectDirectory, "part.vcad"));
        view.RaiseCreateGroup();
        view.RaiseSave();

        shell.Dispose();

        Assert.Empty(Documents(_directory));
        Assert.Empty(Locks(_directory));
    }

    /// <summary>
    /// One card on the way into failing, none on the repeats, and a success
    /// clears it so a later failure is reported again.
    /// </summary>
    [Fact]
    public void AWriteFailureIsReportedOnceAndRetried()
    {
        File.WriteAllText(_directory, "a file where the directory should be");
        _view.RaiseCreateGroup();

        _shell.Tick(120.0);
        _shell.Tick(0.0);
        (string Title, string Message) error = Assert.Single(_view.Errors);
        Assert.Equal("Cannot keep a recovery snapshot", error.Title);
        Assert.Contains(_directory, error.Message);
        Assert.NotNull(Document(_agent)["lastFailure"]);

        _view.RaiseCreateVoxelModel();
        _shell.Tick(120.0);
        _shell.Tick(0.0);
        Assert.Single(_view.Errors);

        File.Delete(_directory);
        _view.RaiseCreateGroup();
        _shell.Tick(120.0);
        Settle(_shell, _agent);

        Assert.Single(Documents(_directory));
        Assert.Null(Document(_agent)["lastFailure"]);
    }

    [Fact]
    public void AFailureCardWaitsWhileAnAgentHoldsTheEditor()
    {
        File.WriteAllText(_directory, "a file where the directory should be");
        TakeControl();
        _agent.Execute("create-element", new Dictionary<string, string> { ["name"] = "Widget" });

        // In steps, with a command between them: an agent silent for the whole
        // interval would lose the editor to the silence rule before the write.
        for (var step = 0; step < 12; step++)
        {
            _shell.Tick(10.0);
            _agent.Execute("control-status", new Dictionary<string, string>());
        }

        _shell.Tick(0.0);
        Assert.Empty(_view.Errors);
        Assert.NotNull(Document(_agent)["lastFailure"]);

        _agent.Execute("release-control", new Dictionary<string, string>());
        _shell.Tick(0.0);

        Assert.Single(_view.Errors);
    }

    /// <summary>
    /// A snapshot opened as if it were a document would be saved into the
    /// snapshot directory and offered again next launch; it is a recovery
    /// instead, one tick later.
    /// </summary>
    [Fact]
    public void OpeningASnapshotFileDirectlyBecomesARecoveryOnTheNextTick()
    {
        string original = Path.Combine(_projectDirectory, "part.vcad");
        _view.OnAskPath = _ => Task.FromResult<string?>(original);
        _view.RaiseCreateGroup();
        _view.RaiseSave();
        _view.RaiseCreateVoxelModel();
        TakeControl();
        _agent.Execute("new", new Dictionary<string, string>());
        string snapshot = Documents(_directory)[0];
        string id = Path.GetFileNameWithoutExtension(snapshot);

        CommandResult result = _agent.Execute("open", new Dictionary<string, string> { ["path"] = snapshot });
        Assert.True(result.Ok, result.Error);

        _shell.Tick(0.0);

        Assert.Equal("• part.vcad — VoxelCAD", _view.WindowTitle);
        Assert.Equal(id, Document(_agent)["snapshotId"]);
        Assert.Equal(original, Document(_agent)["originalPath"]);
        Assert.Equal(true, Document(_agent)["hasSnapshot"]);
        Assert.Contains("Model", _view.HierarchyView.Items.Values);
    }

    [Fact]
    public void TheRecoverMenuItemFollowsThePendingCount()
    {
        Assert.False(_view.RecoverEnabled);

        _view.RaiseCreateGroup();
        TakeControl();
        _agent.Execute("new", new Dictionary<string, string>());
        Assert.True(_view.RecoverEnabled);

        _agent.Execute("autosave-discard", new Dictionary<string, string> { ["session"] = "true" });

        Assert.False(_view.RecoverEnabled);
    }

    private void TakeControl()
    {
        _agent.Execute("take-control", new Dictionary<string, string> { ["name"] = "Claude" });
        _shell.Tick(10.0);
    }
}
