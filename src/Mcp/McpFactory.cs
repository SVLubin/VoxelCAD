using System;
using VoxelCAD.Commands;

namespace VoxelCAD.Mcp;

/// <summary>The MCP assembly's only public entry point.</summary>
public static class McpFactory
{
    /// <param name="dispatcherForClient">
    /// The command surface for one connected agent, by client id. Handed out per
    /// client rather than shared, because control of the editor belongs to one agent
    /// at a time and the commands that negotiate it have to know which one is
    /// calling.
    /// </param>
    public static IMcpServer CreateServer(
        Func<string, ICommandDispatcher> dispatcherForClient,
        string name = "voxelcad",
        string version = "0.1.0") =>
        new McpServer(dispatcherForClient, name, version);

    /// <summary>
    /// One surface for every caller — the shape a transport that carries a single
    /// client wants. The console's stdio pump is that transport: one process, one
    /// agent, nobody to tell apart.
    /// </summary>
    public static IMcpServer CreateServer(
        ICommandDispatcher dispatcher,
        string name = "voxelcad",
        string version = "0.1.0") =>
        new McpServer(_ => dispatcher, name, version);
}
