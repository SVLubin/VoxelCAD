using System;
using VoxelCAD.Commands;
using VoxelCAD.Mcp;
using VoxelCAD.Preferences;

namespace VoxelCAD.Agent;

/// <inheritdoc cref="IMcpEndpoint"/>
/// <remarks>
/// This is the one place where the command surface, the transport and the
/// preferences meet — none of the three layers may reference the other two. It
/// sits in its own assembly rather than in the composition root so the decisions
/// it holds are testable: when a listener is restarted rather than left alone,
/// and when the client config is written at all.
/// <para>
/// The config file is written only once the listener is actually up. A config
/// naming a port nothing is listening on is worse than no config at all: it turns
/// a visible failure into a silent one.
/// </para>
/// </remarks>
internal sealed class McpEndpoint : IMcpEndpoint
{
    private readonly IMcpServer _server;
    private readonly MainThreadWorkQueue _workQueue;

    private McpHttpHost? _host;

    /// <param name="dispatcherForClient">
    /// The command surface per connected agent, by client id. The transport tells
    /// its clients apart, so control of the editor can belong to one of them rather
    /// than to whichever one asked last.
    /// </param>
    /// <param name="stepWorkInFlight">
    /// Advances anything a command left running across frames — a synthesized
    /// gesture is the only such thing — so its own answer waits for it to finish
    /// and nothing behind it overtakes it.
    /// </param>
    public McpEndpoint(
        Func<string, ICommandDispatcher> dispatcherForClient,
        Func<bool> stepWorkInFlight)
    {
        _server = McpFactory.CreateServer(dispatcherForClient);
        _workQueue = new MainThreadWorkQueue(stepWorkInFlight);
    }

    public event Action? ListenerStopped;

    public bool IsListening => _host != null;

    public void Pump() => _workQueue.Pump();

    /// <remarks>
    /// Every preference change arrives here, and most of them are none of the
    /// listener's business — the countdown is not. So a listener already serving
    /// the current port and token is left alone: restarting it would drop
    /// whatever an agent had in flight, to arrive back at the same socket.
    /// </remarks>
    public string Apply(IEditorPreferences preferences)
    {
        if (!preferences.McpEnabled)
        {
            Stop();

            return "The agent endpoint is off.";
        }

        if (!IsServing(preferences) && Restart(preferences) is { } failure)
            return failure;

        // Written on every pass rather than only on a restart: the path is a
        // preference too, and moving it should move the file.
        //
        // What is said about it is deliberately short. The url and the config
        // path are both fields a few rows up in the form this line sits under,
        // so spelling them out again wrapped one sentence over three lines and
        // pushed the buttons beside it out of shape. A status says what
        // happened; the form already says where.
        return McpClientConfigWriter.TryWrite(
            preferences.ClientConfigPath,
            preferences.McpUrl,
            preferences.McpToken,
            out string? error)
            ? "Listening, and the client config is written."
            : $"Listening, but the client config could not be written: {error}";
    }

    public void Dispose() => Stop();

    /// <summary>Whether what is listening is already what the preferences ask for.</summary>
    private bool IsServing(IEditorPreferences preferences) =>
        _host is { IsRunning: true } host
        && host.Port == preferences.McpPort
        && host.Token == preferences.McpToken;

    /// <summary>The failure to report, or null once it is listening.</summary>
    private string? Restart(IEditorPreferences preferences)
    {
        Stop();

        var host = new McpHttpHost(_server, _workQueue, preferences.McpPort, preferences.McpToken);

        try
        {
            host.Start();
        }
        catch (Exception exception)
        {
            // Almost always the port: taken by something else, or reserved. The
            // editor carries on without an endpoint rather than failing to open.
            host.Dispose();

            return $"Port {preferences.McpPort} could not be opened: {exception.Message}";
        }

        _host = host;

        return null;
    }

    private void Stop()
    {
        if (_host == null)
            return;

        _host.Dispose();
        _host = null;

        ListenerStopped?.Invoke();
    }
}
