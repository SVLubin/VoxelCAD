using System;
using VoxelCAD.Preferences;

namespace VoxelCAD.Agent;

/// <summary>
/// The agent endpoint as the application runs it: an HTTP host whose port, token
/// and very existence come from preferences, plus the client config file that
/// tells an agent where to find it.
/// </summary>
public interface IMcpEndpoint : IDisposable
{
    /// <summary>
    /// A listener that was up has gone down — switched off, moved to another port,
    /// or torn down as the editor closes. The transport an agent was on is dead, so
    /// whoever listens releases any control it held.
    /// </summary>
    /// <remarks>
    /// Raised only when something was actually listening, so an ordinary preference
    /// change that leaves the socket alone says nothing, and a restart onto the same
    /// terms does not disturb a live agent. Fired on the main thread, the only one
    /// that stops a listener.
    /// </remarks>
    event Action ListenerStopped;

    /// <summary>Whether the last <see cref="Apply"/> left something listening.</summary>
    bool IsListening { get; }

    /// <summary>
    /// Runs the requests that arrived on listener threads. They touch the live
    /// model, so they wait for the main thread rather than racing the editor.
    /// </summary>
    void Pump();

    /// <summary>
    /// Brings the endpoint into line with <paramref name="preferences"/> and
    /// reports, in one line meant for a person, what actually happened.
    /// </summary>
    string Apply(IEditorPreferences preferences);
}
