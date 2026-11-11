using System.Threading.Tasks;

namespace VoxelCAD.Mcp;

/// <summary>
/// The MCP protocol over the command dispatcher, with no transport of its own:
/// it takes one JSON-RPC message and gives back the reply. stdio pumps it from
/// standard input, the editor pumps it from an HTTP request, and neither needs
/// its own copy of the protocol.
/// </summary>
public interface IMcpServer
{
    /// <summary>
    /// Returns the response to send, or null when the message was a notification
    /// and the protocol expects no reply.
    /// </summary>
    /// <param name="clientId">
    /// Which connected agent this message came from. The command surface is handed
    /// out per client, because control of the editor is exclusive to one agent and
    /// the editor has to be able to tell them apart. A transport that carries one
    /// client at a time — stdio — passes the same value every time.
    /// </param>
    /// <remarks>
    /// Asynchronous for one reason: an agent may ask to be told when the editor is
    /// free, and that answer arrives when it arrives. Everything else completes
    /// before the returned task does — a command touches the model on the main
    /// thread and is finished — so a caller that blocks on this is only ever
    /// blocking on a wait it asked for.
    /// </remarks>
    Task<string?> HandleMessageAsync(string json, string clientId);
}
