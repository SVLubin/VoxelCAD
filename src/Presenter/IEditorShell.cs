using System;

namespace VoxelCAD.Presenter;

/// <summary>
/// The presenter tree, as the composition root sees it: something to create once,
/// tick each frame, and dispose on the way out.
/// </summary>
public interface IEditorShell : IDisposable
{
    /// <summary>
    /// The gated command surface for an agent transport, one dispatcher per client.
    /// Their edits share the user's project, history and undo, and are refused
    /// unless that client is the agent holding control.
    /// </summary>
    IAgentDispatcherProvider CreateAgentDispatchers();

    /// <summary>Advances time-based work — the handover countdown. Once per frame.</summary>
    void Tick(double delta);

    /// <summary>
    /// The agent transport is gone — the endpoint stopped, or the editor is closing
    /// — so any handover is dropped and the user has the interface back. Otherwise a
    /// crashed agent, or the endpoint being switched off mid-session, would leave
    /// the interface dimmed with nobody to release it.
    /// </summary>
    void ReleaseAgentControl();

    /// <summary>
    /// What became of the agent endpoint under the current preferences — listening
    /// where, switched off, or refused the port. The composition root owns the
    /// endpoint, so only it knows; the settings form is where the answer belongs.
    /// </summary>
    void ReportAgentEndpoint(string status);
}
