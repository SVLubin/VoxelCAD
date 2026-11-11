using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Commands;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The read-only view an agent has onto the endpoint and the handover's terms.
/// Reading is free, like a camera read or a slice, so it is allowed without control;
/// there is nothing to write here on purpose.
/// </summary>
public sealed class PreferencesCommandsTests
{
    private const string Client = "preferences-client";

    private readonly ControlArbiter _arbiter = new();
    private readonly FakePreferences _preferences = new();
    private readonly ICommandDispatcher _dispatcher;

    public PreferencesCommandsTests()
    {
        var hostCommands = new List<CommandDefinition>();
        hostCommands.AddRange(PreferencesCommands.Create(_preferences));

        ICommandDispatcher inner = CommandFactory.CreateDispatcher(
            new CommandContext(ModelFactory.CreateProject()),
            hostCommands);

        _dispatcher = new ControlGatedDispatcher(inner, _arbiter, new EditorSession(), Client);
    }

    [Fact]
    public void ReadingPreferencesIsAllowedWithoutControl()
    {
        Assert.True(Run().Ok);
    }

    [Fact]
    public void EndpointReportsWhereAnAgentConnects()
    {
        IReadOnlyDictionary<string, object?> endpoint = Group("endpoint");

        Assert.Equal(true, endpoint["enabled"]);
        Assert.Equal("http://127.0.0.1:8971/mcp", endpoint["url"]);
        Assert.Equal(8971, endpoint["port"]);
        Assert.Equal("/tmp/voxelcad/mcp.json", endpoint["clientConfigPath"]);
    }

    /// <summary>The token is a secret: presence is reported, its value never is.</summary>
    [Fact]
    public void EndpointReportsThatATokenIsSetWithoutEchoingIt()
    {
        IReadOnlyDictionary<string, object?> endpoint = Group("endpoint");

        Assert.Equal(true, endpoint["tokenSet"]);
        Assert.DoesNotContain("token-one", endpoint.Values.Select(value => value?.ToString()));
    }

    [Fact]
    public void HandoverReportsTheTermsAnAgentHoldsControlUnder()
    {
        IReadOnlyDictionary<string, object?> handover = Group("handover");

        Assert.Equal(true, handover["countdownEnabled"]);
        Assert.Equal(3.0, handover["countdownSeconds"]);
        Assert.Equal(30.0, handover["maximumWaitSeconds"]);
        Assert.Equal(60.0, handover["silenceWarningSeconds"]);
        Assert.Equal(30.0, handover["silenceGraceSeconds"]);
    }

    /// <summary>
    /// Reading is free, writing is not offered: an agent can see where the widget
    /// sits and is pointed at <c>view</c> rather than at clicking it.
    /// </summary>
    [Fact]
    public void ViewCubeReportsItsCornerAndNamesTheCommandToUseInstead()
    {
        IReadOnlyDictionary<string, object?> viewCube = Group("viewCube");

        Assert.Equal("TopRight", viewCube["corner"]);
        Assert.Contains("'view'", viewCube["note"]?.ToString());
    }

    /// <summary>
    /// The snapshot's terms are read like every other preference; what has been
    /// written is 'autosave-status', and the note points there.
    /// </summary>
    [Fact]
    public void AutosaveReportsTheSwitchTheIntervalAndTheDirectory()
    {
        IReadOnlyDictionary<string, object?> autosave = Group("autosave");

        Assert.Equal(false, autosave["enabled"]);
        Assert.Equal(120.0, autosave["intervalSeconds"]);
        Assert.Equal(_preferences.AutosaveDirectory, autosave["directory"]);
        Assert.Contains("'autosave-status'", autosave["note"]?.ToString());
    }

    private IReadOnlyDictionary<string, object?> Group(string key)
    {
        var value = (IReadOnlyDictionary<string, object?>)Run().Value!;

        return (IReadOnlyDictionary<string, object?>)value[key]!;
    }

    private CommandResult Run() =>
        _dispatcher.Execute("preferences", new Dictionary<string, string>());
}
