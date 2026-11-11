using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Presenter;
using Xunit;
using static VoxelCAD.Presenter.Tests.AutosaveTestKit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The agent's view of the recovery snapshots: a status to read without control,
/// a write on demand, a recovery, and a discard held to what this session left.
/// </summary>
public sealed class AutosaveCommandsTests : IDisposable
{
    private readonly string _directory = TempDirectory("commands");
    private readonly string _projectDirectory = TempDirectory("project");
    private readonly FakeShellView _view = new();
    private readonly FakePreferences _preferences;
    private readonly ShellPresenter _shell;
    private readonly ICommandDispatcher _agent;

    public AutosaveCommandsTests()
    {
        _preferences = PreferencesFor(_directory);
        _shell = new ShellPresenter(_view, _preferences);
        _agent = _shell.CreateAgentDispatchers().ForClient("commands-client");
        Directory.CreateDirectory(_projectDirectory);
        _shell.Tick(0.0);
    }

    public void Dispose()
    {
        _shell.Dispose();
        Delete(_directory);
        Delete(_projectDirectory);
    }

    [Fact]
    public void StatusIsReadableWithoutControl()
    {
        IReadOnlyDictionary<string, object?> status = Status(_agent);

        Assert.Equal(true, status["enabled"]);
        Assert.Equal(120.0, status["intervalSeconds"]);
        Assert.Equal(_directory, status["directory"]);
        Assert.Equal("Untitled", Document(_agent)["title"]);
        Assert.Contains("'save'", (string)status["note"]!);
    }

    [Fact]
    public void NowIsRefusedWithoutControl()
    {
        CommandResult result = _agent.Execute("autosave-now", new Dictionary<string, string>());

        Assert.False(result.Ok);
        Assert.Contains("take-control", result.Error);
    }

    [Fact]
    public void NowWritesAndReportsThePathAndTheMilliseconds()
    {
        TakeControl();
        _agent.Execute("create-element", new Dictionary<string, string> { ["name"] = "Widget" });

        IReadOnlyDictionary<string, object?> answer = Ok("autosave-now");

        Assert.Equal(true, answer["written"]);
        Assert.True(File.Exists((string)answer["path"]!));
        Assert.Equal(Path.GetFileNameWithoutExtension((string)answer["path"]!), answer["id"]);
        Assert.IsType<double>(answer["milliseconds"]);
        Assert.Equal(true, Document(_agent)["hasSnapshot"]);
    }

    [Fact]
    public void NowReportsWhyNothingWasWritten()
    {
        TakeControl();

        Assert.Contains("matches its file", (string)Ok("autosave-now")["reason"]!);

        _agent.Execute("create-element", new Dictionary<string, string> { ["name"] = "Widget" });
        Ok("autosave-now");
        Assert.Contains("nothing has changed", (string)Ok("autosave-now")["reason"]!);

        _preferences.Apply(_preferences.GetValues() with { AutosaveEnabled = false });
        Assert.Contains("switched off", (string)Ok("autosave-now")["reason"]!);
    }

    /// <summary>
    /// A mutating command ends the gesture the interface left open, like every
    /// other, so what the snapshot holds is a filed record and not half a drag:
    /// the colour edit is in the history before the snapshot is taken.
    /// </summary>
    [Fact]
    public void NowEndsAGestureTheInterfaceLeftOpen()
    {
        _view.HierarchyView.RaiseItemClicked(Material(_view));
        _view.InspectorView.RaiseColorEditBegan();
        _view.InspectorView.RaiseColorEdited(new Godot.Color(0.9f, 0.3f, 0.2f));
        Assert.Empty(_view.HistoryView.Labels);
        TakeControl();

        Assert.Equal(true, Ok("autosave-now")["written"]);
        Assert.Equal(new[] { "Change material Default" }, _view.HistoryView.Labels);
    }

    [Fact]
    public void RecoverOpensTheSnapshotModifiedUnderItsPath()
    {
        string original = Path.Combine(_projectDirectory, "part.vcad");
        string id = Orphan(original);

        IReadOnlyDictionary<string, object?> answer = Ok("autosave-recover", ("id", id));

        Assert.Equal(id, answer["recovered"]);
        Assert.Equal(original, answer["path"]);
        Assert.Equal("part.vcad", answer["title"]);
        Assert.Equal(true, answer["openedModified"]);
        Assert.Contains("original path", (string)answer["note"]!);
        Assert.Equal("• part.vcad — VoxelCAD", _view.WindowTitle);
        Assert.Contains("Model", _view.HierarchyView.Items.Values);
    }

    [Fact]
    public void RecoverOfAChangedOriginalAnswersANullPathAndANote()
    {
        string original = Path.Combine(_projectDirectory, "part.vcad");
        string id = Orphan(original);
        File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddMinutes(5));

        IReadOnlyDictionary<string, object?> answer = Ok("autosave-recover", ("id", id));

        Assert.Null(answer["path"]);
        Assert.Equal("Untitled", answer["title"]);
        Assert.Contains("moved on", (string)answer["note"]!);
        Assert.Equal("• Untitled — VoxelCAD", _view.WindowTitle);
    }

    [Fact]
    public void RecoverRefusesUnknownHeldAndUnreadableIdsByName()
    {
        TakeControl();
        Assert.Contains("No snapshot", Fails("autosave-recover", ("id", Guid.NewGuid().ToString("N"))));
        Assert.Contains("not a snapshot id", Fails("autosave-recover", ("id", "nonsense")));

        _agent.Execute("create-element", new Dictionary<string, string> { ["name"] = "Widget" });
        string own = (string)Ok("autosave-now")["id"]!;
        Assert.Contains("own snapshot", Fails("autosave-recover", ("id", own)));

        string orphan = Orphan(Path.Combine(_projectDirectory, "part.vcad"));
        using (new AutosaveStore(_directory).Open(Guid.Parse(orphan)))
            Assert.Contains("held by a running editor", Fails("autosave-recover", ("id", orphan)));

        File.WriteAllText(Path.Combine(_directory, orphan + ".json"), "{ broken");
        Assert.Contains("cannot be recovered", Fails("autosave-recover", ("id", orphan)));
    }

    [Fact]
    public void DiscardDeletesASessionOrphan()
    {
        string id = Orphan(Path.Combine(_projectDirectory, "part.vcad"));

        IReadOnlyDictionary<string, object?> answer = Ok("autosave-discard", ("id", id));

        Assert.Equal(new[] { id }, (IEnumerable<string>)answer["deleted"]!);
        Assert.Empty(Documents(_directory));
    }

    [Fact]
    public void DiscardSessionDeletesEveryOrphanThisSessionLeft()
    {
        Orphan(Path.Combine(_projectDirectory, "one.vcad"));
        Orphan(Path.Combine(_projectDirectory, "two.vcad"));

        IReadOnlyDictionary<string, object?> answer = Ok("autosave-discard", ("session", "true"));

        Assert.Equal(2, ((IEnumerable<string>)answer["deleted"]!).Count());
        Assert.Empty(Documents(_directory));
    }

    /// <summary>
    /// An orphan somebody else removed — another editor sharing the directory, a
    /// hand — is forgotten rather than reported deleted, and nothing throws.
    /// </summary>
    [Fact]
    public void DiscardSessionForgetsAnOrphanThatIsAlreadyGone()
    {
        string kept = Orphan(Path.Combine(_projectDirectory, "kept.vcad"));
        string gone = Orphan(Path.Combine(_projectDirectory, "gone.vcad"));
        File.Delete(Path.Combine(_directory, gone + ".vcad"));
        File.Delete(Path.Combine(_directory, gone + ".json"));

        IReadOnlyDictionary<string, object?> answer = Ok("autosave-discard", ("session", "true"));

        Assert.Equal(new[] { kept }, (IEnumerable<string>)answer["deleted"]!);
        Assert.Empty(Documents(_directory));
        Assert.Empty(Snapshots(_agent));
    }

    [Fact]
    public void DiscardRefusesTheOpenDocumentsOwnSnapshot()
    {
        TakeControl();
        _agent.Execute("create-element", new Dictionary<string, string> { ["name"] = "Widget" });
        string own = (string)Ok("autosave-now")["id"]!;

        Assert.Contains("'save' deletes it", Fails("autosave-discard", ("id", own)));
        Assert.Single(Documents(_directory));
    }

    /// <summary>
    /// A snapshot from an earlier session is the user's: offered at launch and
    /// under the menu, and not an agent's to delete.
    /// </summary>
    [Fact]
    public void DiscardRefusesAnEarlierSessionsSnapshot()
    {
        var previous = new FakeShellView();
        var earlier = new ShellPresenter(previous, PreferencesFor(_directory));
        previous.RaiseCreateGroup();
        earlier.Dispose();
        string id = Path.GetFileNameWithoutExtension(Documents(_directory)[0]);
        TakeControl();

        Assert.Contains("earlier session", Fails("autosave-discard", ("id", id)));
        Assert.Contains("exactly one", Fails("autosave-discard"));
        Assert.Single(Documents(_directory));
        Assert.Equal(false, Assert.Single(Snapshots(_agent))["sessionOrphan"]);
    }

    /// <summary>
    /// Leaves an orphan the way an agent does: a saved document, edited, replaced
    /// by 'new'. Answers its id.
    /// </summary>
    private string Orphan(string original)
    {
        TakeControl();
        _agent.Execute("create-element", new Dictionary<string, string> { ["name"] = "Group" });
        _agent.Execute("save", new Dictionary<string, string> { ["path"] = original });
        _agent.Execute(
            "create-element",
            new Dictionary<string, string> { ["name"] = "Model", ["kind"] = "VoxelModel" });
        _agent.Execute("new", new Dictionary<string, string>());

        foreach (IReadOnlyDictionary<string, object?> row in Snapshots(_agent))
        {
            if (original.Equals(row["originalPath"]))
                return (string)row["id"]!;
        }

        throw new InvalidOperationException("No orphan was left.");
    }

    private void TakeControl()
    {
        _agent.Execute("take-control", new Dictionary<string, string> { ["name"] = "Claude" });
        _shell.Tick(10.0);
    }

    private IReadOnlyDictionary<string, object?> Ok(string name, params (string Key, string Value)[] arguments)
    {
        CommandResult result = _agent.Execute(name, ToDictionary(arguments));

        Assert.True(result.Ok, result.Error);

        return (IReadOnlyDictionary<string, object?>)result.Value!;
    }

    private string Fails(string name, params (string Key, string Value)[] arguments)
    {
        CommandResult result = _agent.Execute(name, ToDictionary(arguments));

        Assert.False(result.Ok, $"'{name}' unexpectedly succeeded");

        return result.Error!;
    }

    private static Dictionary<string, string> ToDictionary((string Key, string Value)[] arguments)
    {
        var dictionary = new Dictionary<string, string>();

        foreach ((string key, string value) in arguments)
            dictionary[key] = value;

        return dictionary;
    }
}
