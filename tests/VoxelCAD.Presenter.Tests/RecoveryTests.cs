using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Presenter;
using Xunit;
using static VoxelCAD.Presenter.Tests.AutosaveTestKit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The launch: two shells over one directory, the first leaving work behind and
/// the second being asked about it. What Recover, Discard and Later each mean.
/// </summary>
public sealed class RecoveryTests : IDisposable
{
    private readonly string _directory = TempDirectory("recovery");
    private readonly string _projectDirectory = TempDirectory("project");
    private readonly List<ShellPresenter> _shellsList = new();
    private readonly Dictionary<FakeShellView, ShellPresenter> _shellsByViewDictionary = new();

    public RecoveryTests()
    {
        Directory.CreateDirectory(_projectDirectory);
    }

    public void Dispose()
    {
        foreach (ShellPresenter shell in _shellsList)
            shell.Dispose();

        Delete(_directory);
        Delete(_projectDirectory);
    }

    [Fact]
    public void NothingIsOfferedWhenTheStoreIsEmpty()
    {
        FakeShellView view = Launch();

        Assert.Equal(0, view.ConfirmCount);
        Assert.False(view.RecoverEnabled);
    }

    [Fact]
    public void TheOfferComesOnTheFirstTickNotInTheConstructor()
    {
        LeaveUntitledWork();
        var view = new FakeShellView();
        ShellPresenter shell = Open(view);

        Assert.Equal(0, view.ConfirmCount);

        shell.Tick(0.0);

        Assert.Equal(1, view.ConfirmCount);
    }

    [Fact]
    public void APendingSnapshotIsOfferedNewestFirstWithRecoverDiscardLater()
    {
        LeaveWork("older.vcad");
        LeaveWork("newer.vcad");
        var seenList = new List<ConfirmRequest>();

        Launch(request =>
        {
            seenList.Add(request);

            return ConfirmResult.Cancel;
        });

        ConfirmRequest first = Assert.Single(seenList);
        Assert.StartsWith("Unsaved work found", first.Title);
        Assert.Contains("newer.vcad", first.Message);
        Assert.Equal("Recover", first.ConfirmLabel);
        Assert.Equal("Discard", first.DenyLabel);
        Assert.Equal("Later", first.CancelLabel);
    }

    [Fact]
    public void RecoverOpensItModifiedUnderItsOriginalPath()
    {
        string original = LeaveWork("part.vcad");
        FakeShellView view = Launch(_ => ConfirmResult.Confirm);
        ICommandDispatcher agent = Agent(view);

        Assert.Equal("• part.vcad — VoxelCAD", view.WindowTitle);
        Assert.Contains("Model", view.HierarchyView.Items.Values);
        Assert.False(view.CanUndo);
        Assert.Equal(original, Document(agent)["originalPath"]);
        Assert.Equal(true, Document(agent)["hasSnapshot"]);
        Assert.Single(Documents(_directory));
        Assert.Single(Locks(_directory));
        Assert.Empty(Snapshots(agent));

        // The line says when that snapshot was written, not when the next will be:
        // the record has not moved, so no write is coming until it does.
        Assert.StartsWith("Last snapshot at ", (string)Status(agent)["status"]!);
    }

    [Fact]
    public void ARecoveredDocumentStaysModifiedAfterUndoingEverything()
    {
        LeaveWork("part.vcad");
        FakeShellView view = Launch(_ => ConfirmResult.Confirm);

        view.RaiseCreateGroup();
        view.RaiseUndo();

        Assert.StartsWith("•", view.WindowTitle);
    }

    [Fact]
    public void SaveAfterRecoveryWritesTheOriginalPathAndDeletesTheSnapshot()
    {
        string original = LeaveWork("part.vcad");
        FakeShellView view = Launch(_ => ConfirmResult.Confirm);
        var asked = 0;
        view.OnAskPath = _ =>
        {
            asked++;

            return Task.FromResult<string?>(null);
        };

        view.RaiseSave();

        Assert.Equal(0, asked);
        Assert.Equal("part.vcad — VoxelCAD", view.WindowTitle);
        Assert.Contains("Model", File.ReadAllText(original));
        Assert.Empty(Documents(_directory));
    }

    [Fact]
    public void RecoverAnUntitledSnapshotAsksWhereOnSave()
    {
        LeaveUntitledWork();
        FakeShellView view = Launch(_ => ConfirmResult.Confirm);
        string path = Path.Combine(_projectDirectory, "chosen.vcad");
        view.OnAskPath = _ => Task.FromResult<string?>(path);

        Assert.Equal("• Untitled — VoxelCAD", view.WindowTitle);

        view.RaiseSave();

        Assert.True(File.Exists(path));
        Assert.Empty(Documents(_directory));
    }

    /// <summary>
    /// The file was saved after the snapshot — another instance, another tool —
    /// so the snapshot opens untitled and a save has to ask where.
    /// </summary>
    [Fact]
    public void RecoverOfAChangedOriginalOpensUntitledAndSaysWhy()
    {
        string original = LeaveWork("part.vcad");
        File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddMinutes(5));
        var seenList = new List<ConfirmRequest>();

        FakeShellView view = Launch(request =>
        {
            seenList.Add(request);

            return ConfirmResult.Confirm;
        });

        Assert.Contains("has been written since", Assert.Single(seenList).Message);
        Assert.Equal("• Untitled — VoxelCAD", view.WindowTitle);
        Assert.Null(Document(Agent(view))["originalPath"]);
    }

    [Fact]
    public void RecoverOfAMissingOriginalKeepsThePath()
    {
        string original = LeaveWork("part.vcad");
        File.Delete(original);
        var seenList = new List<ConfirmRequest>();

        FakeShellView view = Launch(request =>
        {
            seenList.Add(request);

            return ConfirmResult.Confirm;
        });

        Assert.Contains($"no longer at {original}", Assert.Single(seenList).Message);
        Assert.Equal("• part.vcad — VoxelCAD", view.WindowTitle);
        Assert.Equal(original, Document(Agent(view))["originalPath"]);
    }

    [Fact]
    public void DiscardAtLaunchDeletesIt()
    {
        LeaveWork("part.vcad");

        FakeShellView view = Launch(_ => ConfirmResult.Deny);

        Assert.Empty(Documents(_directory));
        Assert.Empty(Sidecars(_directory));
        Assert.Equal("Untitled — VoxelCAD", view.WindowTitle);
        Assert.False(view.RecoverEnabled);
    }

    [Fact]
    public void LaterLeavesTheSnapshotAndStopsAsking()
    {
        LeaveWork("one.vcad");
        LeaveWork("two.vcad");

        FakeShellView view = Launch(_ => ConfirmResult.Cancel);

        Assert.Equal(1, view.ConfirmCount);
        Assert.Equal(2, Documents(_directory).Length);
        Assert.True(view.RecoverEnabled);
        Assert.Equal(2, Snapshots(Agent(view)).Count);
    }

    [Fact]
    public void LaterAsksAgainAtTheNextLaunch()
    {
        LeaveWork("part.vcad");
        Launch(_ => ConfirmResult.Cancel);

        FakeShellView again = Launch(_ => ConfirmResult.Cancel);

        Assert.Equal(1, again.ConfirmCount);
    }

    [Fact]
    public void ARecoverEndsTheOfferAndTheRestStayPending()
    {
        LeaveWork("older.vcad");
        LeaveWork("newer.vcad");

        FakeShellView view = Launch(_ => ConfirmResult.Confirm);

        Assert.Equal(1, view.ConfirmCount);
        Assert.Equal("• newer.vcad — VoxelCAD", view.WindowTitle);
        Assert.Single(Snapshots(Agent(view)));
        Assert.True(view.RecoverEnabled);
    }

    [Fact]
    public void MoreThanThreeAskASummaryFirstAndReviewGoesOneByOne()
    {
        for (var index = 0; index < 4; index++)
            LeaveWork($"part{index}.vcad");

        var seenList = new List<ConfirmRequest>();
        Launch(request =>
        {
            seenList.Add(request);

            return request.ConfirmLabel == "Review" ? ConfirmResult.Confirm : ConfirmResult.Cancel;
        });

        Assert.Equal(2, seenList.Count);
        Assert.Equal("Review", seenList[0].ConfirmLabel);
        Assert.Equal("Discard all", seenList[0].DenyLabel);
        Assert.Contains("4 recovery snapshots", seenList[0].Message);
        Assert.Equal("Unsaved work found (1 of 4)", seenList[1].Title);
    }

    [Fact]
    public void DiscardAllDeletesEveryRecoverableEntry()
    {
        for (var index = 0; index < 4; index++)
            LeaveWork($"part{index}.vcad");

        FakeShellView view = Launch(_ => ConfirmResult.Deny);

        Assert.Equal(1, view.ConfirmCount);
        Assert.Empty(Documents(_directory));
        Assert.False(view.RecoverEnabled);
    }

    [Fact]
    public void ACorruptSnapshotIsReportedAndLeftAlone()
    {
        LeaveWork("part.vcad");
        string snapshot = Documents(_directory)[0];
        string sidecar = Sidecars(_directory)[0];
        string garbage = "version: 5\nscenes: [\n";
        File.WriteAllText(snapshot, garbage);
        File.WriteAllText(
            sidecar,
            System.Text.RegularExpressions.Regex.Replace(
                File.ReadAllText(sidecar),
                "\"documentBytes\": \\d+",
                $"\"documentBytes\": {new FileInfo(snapshot).Length}"));

        FakeShellView view = Launch(_ => ConfirmResult.Confirm);

        (string Title, string Message) error = Assert.Single(view.Errors);
        Assert.Equal("Cannot recover", error.Title);
        Assert.Equal("Untitled — VoxelCAD", view.WindowTitle);
        Assert.True(File.Exists(snapshot));
    }

    [Fact]
    public void AnUnreadableSnapshotIsNeverOffered()
    {
        LeaveWork("part.vcad");
        File.WriteAllText(Sidecars(_directory)[0], "{ not json");

        FakeShellView view = Launch(_ => ConfirmResult.Confirm);

        Assert.Equal(0, view.ConfirmCount);
        Assert.False(view.RecoverEnabled);
        Assert.Equal("unreadable", Assert.Single(Snapshots(Agent(view)))["state"]);
    }

    [Fact]
    public void AHeldSnapshotIsNotOffered()
    {
        var first = new FakeShellView();
        ShellPresenter running = Open(first);
        running.Tick(0.0);
        first.RaiseCreateGroup();
        running.Tick(120.0);
        Settle(running, Agent(first));

        FakeShellView second = Launch(_ => ConfirmResult.Confirm);

        Assert.Equal(0, second.ConfirmCount);
        Assert.Equal("inUse", Assert.Single(Snapshots(Agent(second)))["state"]);
    }

    /// <summary>
    /// An agent granted the editor while the card is up may replace the document
    /// under it. Whatever the user then answers is Later: nothing deleted, nothing
    /// recovered over the agent's work.
    /// </summary>
    [Fact]
    public void AnAnswerAfterTheDocumentChangedUnderTheCardIsLater()
    {
        LeaveWork("part.vcad");
        var view = new FakeShellView();
        var answer = new TaskCompletionSource<ConfirmResult>();
        view.OnAskConfirm = _ => answer.Task;
        ShellPresenter shell = Open(view);
        shell.Tick(0.0);
        ICommandDispatcher agent = Agent(view);
        agent.Execute("take-control", new Dictionary<string, string> { ["name"] = "Claude" });
        shell.Tick(60.0);

        agent.Execute("new", new Dictionary<string, string>());
        answer.SetResult(ConfirmResult.Deny);

        Assert.Single(Documents(_directory));
        Assert.Equal("Untitled — VoxelCAD", view.WindowTitle);
    }

    [Fact]
    public void RecoverWithUnsavedWorkAsksFirstAndCancelChangesNothing()
    {
        LeaveWork("part.vcad");
        var seenList = new List<ConfirmRequest>();
        FakeShellView view = Launch(_ => ConfirmResult.Cancel);
        view.RaiseCreateGroup();
        view.OnAskConfirm = request =>
        {
            seenList.Add(request);

            return Task.FromResult(request.Title == "Unsaved changes" ? ConfirmResult.Cancel : ConfirmResult.Confirm);
        };

        view.RaiseRecover();

        Assert.Equal(2, seenList.Count);
        Assert.Equal("Unsaved changes", seenList[1].Title);
        Assert.Equal("• Untitled — VoxelCAD", view.WindowTitle);
        Assert.Contains("Group", view.HierarchyView.Items.Values);
        Assert.Single(Documents(_directory));
    }

    [Fact]
    public void FileRecoverReRunsTheOffer()
    {
        LeaveWork("part.vcad");
        FakeShellView view = Launch(_ => ConfirmResult.Cancel);
        view.OnAskConfirm = _ => Task.FromResult(ConfirmResult.Confirm);

        view.RaiseRecover();

        Assert.Equal("• part.vcad — VoxelCAD", view.WindowTitle);
    }

    /// <summary>A shell that has just been asked; the launch's own first tick.</summary>
    private FakeShellView Launch(Func<ConfirmRequest, ConfirmResult>? answer = null)
    {
        var view = new FakeShellView();

        if (answer is not null)
            view.OnAskConfirm = request => Task.FromResult(answer(request));

        ShellPresenter shell = Open(view);
        shell.Tick(0.0);

        return view;
    }

    private ShellPresenter Open(FakeShellView view)
    {
        var shell = new ShellPresenter(view, PreferencesFor(_directory));
        _shellsList.Add(shell);
        _shellsByViewDictionary[view] = shell;

        return shell;
    }

    private ICommandDispatcher Agent(FakeShellView view) =>
        _shellsByViewDictionary[view].CreateAgentDispatchers().ForClient($"client-{_shellsByViewDictionary.Count}");

    /// <summary>
    /// A previous session: saved to <paramref name="fileName"/>, edited, snapshotted
    /// at the interval, then gone without a save — a kill, a crash. Answers the path.
    /// </summary>
    private string LeaveWork(string fileName)
    {
        string path = Path.Combine(_projectDirectory, fileName);
        var view = new FakeShellView { OnAskPath = _ => Task.FromResult<string?>(path) };
        var shell = new ShellPresenter(view, PreferencesFor(_directory));
        shell.Tick(0.0);

        view.RaiseCreateGroup();
        view.RaiseSave();
        view.RaiseCreateVoxelModel();
        shell.Tick(120.0);
        Settle(shell, shell.CreateAgentDispatchers().ForClient("previous"));

        // A crash releases the lock without the unbind that would delete anything.
        Crash(shell);

        return path;
    }

    private void LeaveUntitledWork()
    {
        var view = new FakeShellView();
        var shell = new ShellPresenter(view, PreferencesFor(_directory));
        shell.Tick(0.0);

        view.RaiseCreateGroup();
        shell.Tick(120.0);
        Settle(shell, shell.CreateAgentDispatchers().ForClient("previous"));

        Crash(shell);
    }

    /// <summary>
    /// What a killed process leaves: the pair on disk and a lock file nobody
    /// holds. Disposing releases the lock and deletes its file, so the file is
    /// put back — a crash does not get to tidy up. The write on the way out
    /// changes nothing here: the snapshot already holds the last record.
    /// </summary>
    private void Crash(ShellPresenter shell)
    {
        shell.Dispose();

        foreach (string document in Documents(_directory))
        {
            string lockPath = Path.ChangeExtension(document, AutosaveStore.LockExtension);

            if (!File.Exists(lockPath))
                File.WriteAllText(lockPath, string.Empty);
        }
    }
}
