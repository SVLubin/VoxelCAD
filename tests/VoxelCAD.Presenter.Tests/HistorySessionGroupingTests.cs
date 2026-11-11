using System;
using System.Linq;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// What an agent did while it held the editor is folded into one group in the
/// history panel. The fold itself is the view's, and cannot be built without an
/// engine; what is decided here — which records belong to which session, and
/// what the group's row says — is the presenter's, and is checked without one.
/// </summary>
public sealed class HistorySessionGroupingTests
{
    private const string Client = "history-client";

    private readonly ControlArbiter _arbiter = new();
    private readonly FakeControlView _controlView = new();
    private readonly FakeSceneView _sceneView = new();
    private readonly FakePreferences _preferences = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly FakeHistoryView _view = new();
    private readonly ControlPresenter _control;
    private readonly HistoryPresenter _history;
    private readonly IScene _scene;

    public HistorySessionGroupingTests()
    {
        _scene = _project.Scenes[0];
        _control = new ControlPresenter(
            _arbiter, _controlView, _sceneView, _project.History, _preferences);
        _history = new HistoryPresenter(_project.History, _view);
    }

    [Fact]
    public void TheUsersOwnEditsAreNotGrouped()
    {
        Create("Widget");

        Assert.Equal(new[] { 0 }, _view.Rows.Select(row => row.GroupId));
        Assert.All(_view.Rows, row => Assert.Null(row.GroupLabel));
    }

    [Fact]
    public void EverythingAnAgentDoesWhileItHoldsControlSharesOneGroup()
    {
        Create("Before");
        Grant("Claude");
        Create("First");
        Create("Second");

        Assert.Equal(0, _view.Rows[0].GroupId);
        Assert.NotEqual(0, _view.Rows[1].GroupId);
        Assert.Equal(_view.Rows[1].GroupId, _view.Rows[2].GroupId);
    }

    [Fact]
    public void WorkAfterTheAgentLeavesIsUngroupedAgain()
    {
        Grant("Claude");
        Create("Agents");
        _arbiter.Release(Client);
        Create("Users");

        Assert.NotEqual(0, _view.Rows[0].GroupId);
        Assert.Equal(0, _view.Rows[1].GroupId);
    }

    /// <summary>
    /// The reason a session carries a number at all: the same agent reconnecting
    /// is a second visit, and folding both into one row would say it never left.
    /// </summary>
    [Fact]
    public void TwoVisitsByTheSameAgentAreTwoGroups()
    {
        Grant("Claude");
        Create("First");
        _arbiter.Release(Client);

        Grant("Claude");
        Create("Second");

        Assert.NotEqual(_view.Rows[0].GroupId, _view.Rows[1].GroupId);
    }

    /// <summary>A countdown the user cancels is not a session and consumes no number.</summary>
    [Fact]
    public void ARefusedRequestDoesNotConsumeASession()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Deny();

        Grant("Claude");
        Create("First");

        Assert.Equal(1, _view.Rows[0].GroupId);
    }

    [Fact]
    public void TheGroupRowNamesTheAgentAndCountsWhatItDid()
    {
        Grant("Claude");
        Create("First");

        Assert.Equal("\"Claude\" — 1 action", _view.Rows[0].GroupLabel);

        Create("Second");

        Assert.Equal("\"Claude\" — 2 actions", _view.Rows[0].GroupLabel);
        Assert.Equal(_view.Rows[0].GroupLabel, _view.Rows[1].GroupLabel);
    }

    /// <summary>
    /// Grouping is appearance: the records underneath are ordinary entries, so a
    /// click on one still walks the document to exactly that point.
    /// </summary>
    [Fact]
    public void AGroupedRecordIsStillAPointTheHistoryWalksTo()
    {
        Grant("Claude");
        Create("First");
        Create("Second");

        _view.RaiseRecordClicked(0);

        Assert.Equal(0, _project.History.CurrentIndex);
        Assert.True(_project.History.CanRedo);
    }

    private void Grant(string agent)
    {
        _arbiter.Request(Client, agent, string.Empty);
        _control.Tick(_preferences.CountdownSeconds + 1.0);
    }

    private Guid Create(string name) =>
        _scene.CreateElement(_scene.Id, ComponentKind.None, name);

    /// <summary>An anonymous session reads "An agent", not a placeholder in quotes.</summary>
    [Fact]
    public void AnAnonymousSessionIsWordedWithoutQuotes()
    {
        Grant(string.Empty);
        Create("First");

        Assert.Equal("An agent — 1 action", _view.Rows[0].GroupLabel);
    }
}
