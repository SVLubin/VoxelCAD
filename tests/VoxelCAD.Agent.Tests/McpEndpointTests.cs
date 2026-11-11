using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace VoxelCAD.Agent.Tests;

/// <summary>
/// The decisions the endpoint holds, exercised over real loopback sockets: when a
/// listener is restarted rather than left alone, when the client config is
/// written at all, and that a listener going down says so — the signal the shell
/// turns into releasing an agent's control.
/// </summary>
public sealed class McpEndpointTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"voxelcad-agent-{Guid.NewGuid():N}");

    private readonly FakeEditorPreferences _preferences;
    private readonly IMcpEndpoint _endpoint;

    private int _stops;

    public McpEndpointTests()
    {
        Directory.CreateDirectory(_directory);

        _preferences = new FakeEditorPreferences
        {
            McpPort = FreePort(),
            ClientConfigPath = Path.Combine(_directory, "mcp.json"),
        };

        // Lifecycle only: no request ever reaches the server in these tests, so a
        // dispatcher being asked for is itself a failure worth hearing about.
        _endpoint = AgentFactory.CreateEndpoint(
            _ => throw new InvalidOperationException("No request expected."),
            () => false);

        _endpoint.ListenerStopped += OnListenerStopped;
    }

    public void Dispose()
    {
        _endpoint.ListenerStopped -= OnListenerStopped;
        _endpoint.Dispose();

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void DisabledEndpointSaysOffAndListensToNothing()
    {
        _preferences.McpEnabled = false;

        var report = _endpoint.Apply(_preferences);

        Assert.False(_endpoint.IsListening);
        Assert.Contains("off", report);

        // Nothing was up, so nothing went down: the event is for a lost transport.
        Assert.Equal(0, _stops);
    }

    [Fact]
    public void ApplyBringsTheListenerUpAndWritesTheConfig()
    {
        var report = _endpoint.Apply(_preferences);

        Assert.True(_endpoint.IsListening);
        Assert.StartsWith("Listening", report);

        // The config is the connection: it carries the token an agent presents.
        var config = File.ReadAllText(_preferences.ClientConfigPath);

        Assert.Contains(_preferences.McpToken, config);
    }

    [Fact]
    public void SameTermsLeaveTheListenerAlone()
    {
        _endpoint.Apply(_preferences);
        _endpoint.Apply(_preferences);

        // A restart would have dropped whatever an agent had in flight, to arrive
        // back at the same socket — so the second pass must not have stopped it.
        Assert.True(_endpoint.IsListening);
        Assert.Equal(0, _stops);
    }

    /// <summary>
    /// And leave the config alone with it. Every preference reaching disk arrives
    /// here, and the settings form applies as it is set — so switching the theme
    /// would otherwise rewrite a file this editor does not own, merged into
    /// whatever else is in it, for no reason at all.
    /// </summary>
    /// <remarks>
    /// The property is <see cref="McpClientConfigWriter"/>'s, which compares the
    /// content it is about to write and stops if it is already there. It is
    /// asserted from out here because this is where the cost is paid and where
    /// the reason lives; the endpoint deliberately keeps no memo of its own,
    /// which would skip the read as well and, with it, the one case that memo
    /// could not see — somebody else having overwritten the file meanwhile.
    /// </remarks>
    [Fact]
    public void SameTermsLeaveTheClientConfigAlone()
    {
        _endpoint.Apply(_preferences);

        DateTime written = File.GetLastWriteTimeUtc(_preferences.ClientConfigPath);

        // Stamped back rather than waited on: a second pass inside the file
        // system's own resolution would read as untouched however it behaved.
        File.SetLastWriteTimeUtc(
            _preferences.ClientConfigPath, written - TimeSpan.FromMinutes(1));

        _endpoint.Apply(_preferences);

        Assert.Equal(
            written - TimeSpan.FromMinutes(1),
            File.GetLastWriteTimeUtc(_preferences.ClientConfigPath));
    }

    /// <summary>
    /// The path is a preference too, so moving it moves the file — which is the
    /// half the narrowing above must not have taken with it.
    /// </summary>
    [Fact]
    public void AMovedConfigPathIsWrittenAgain()
    {
        _endpoint.Apply(_preferences);

        _preferences.ClientConfigPath = Path.Combine(_directory, "moved.json");

        var report = _endpoint.Apply(_preferences);

        Assert.StartsWith("Listening", report);
        Assert.Contains(_preferences.McpToken, File.ReadAllText(_preferences.ClientConfigPath));

        // And the listener was left alone: the path says nothing about the socket.
        Assert.Equal(0, _stops);
    }

    [Fact]
    public void ChangedPortRestartsAndSaysTheOldListenerStopped()
    {
        _endpoint.Apply(_preferences);

        _preferences.McpPort = FreePort();

        var report = _endpoint.Apply(_preferences);

        Assert.True(_endpoint.IsListening);
        Assert.StartsWith("Listening", report);

        // The transport an agent was on is dead even though a new one is up.
        Assert.Equal(1, _stops);
    }

    [Fact]
    public void ChangedTokenRestartsToo()
    {
        _endpoint.Apply(_preferences);

        _preferences.McpToken = "token-two";

        _endpoint.Apply(_preferences);

        Assert.True(_endpoint.IsListening);
        Assert.Equal(1, _stops);
    }

    [Fact]
    public void DisablingStopsTheListenerAndRaisesTheEvent()
    {
        _endpoint.Apply(_preferences);

        _preferences.McpEnabled = false;

        var report = _endpoint.Apply(_preferences);

        Assert.False(_endpoint.IsListening);
        Assert.Contains("off", report);
        Assert.Equal(1, _stops);
    }

    [Fact]
    public void TakenPortIsReportedAndNoConfigIsWritten()
    {
        // Another endpoint of our own holds the port, so the failure is the real
        // one a second editor instance would produce. It writes its config
        // elsewhere — this test's question is whether OURS gets written.
        var otherPreferences = new FakeEditorPreferences
        {
            McpPort = _preferences.McpPort,
            ClientConfigPath = Path.Combine(_directory, "other.json"),
        };

        using IMcpEndpoint other = AgentFactory.CreateEndpoint(
            _ => throw new InvalidOperationException("No request expected."),
            () => false);

        other.Apply(otherPreferences);

        var report = _endpoint.Apply(_preferences);

        Assert.False(_endpoint.IsListening);
        Assert.Contains($"Port {_preferences.McpPort} could not be opened", report);

        // A config naming a port nothing (of ours) is listening on would turn a
        // visible failure into a silent one.
        Assert.False(File.Exists(_preferences.ClientConfigPath));
    }

    [Fact]
    public void DisposeStopsTheListenerAndRaisesTheEvent()
    {
        _endpoint.Apply(_preferences);

        // This is why the composition root unsubscribes before disposing: at
        // shutdown the interface the event would release is on its way out too.
        _endpoint.Dispose();

        Assert.False(_endpoint.IsListening);
        Assert.Equal(1, _stops);
    }

    private void OnListenerStopped() => _stops++;

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        listener.Stop();

        return port;
    }
}
