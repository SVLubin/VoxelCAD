using VoxelCAD.Commands;

namespace VoxelCAD.Presenter;

/// <summary>
/// Hands out the command surface for one agent client. A transport that can tell
/// its clients apart asks for one per client; that is what makes control exclusive
/// to an agent rather than merely to "an agent".
/// </summary>
public interface IAgentDispatcherProvider
{
    /// <param name="clientId">
    /// Whatever the transport uses to tell one connected agent from another — an
    /// MCP session for the editor's HTTP endpoint. It is opaque here: the only thing
    /// asked of it is that the same agent keeps the same one, and that two agents
    /// never share.
    /// </param>
    ICommandDispatcher ForClient(string clientId);
}
