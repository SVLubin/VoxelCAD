using System;
using VoxelCAD.Commands;

namespace VoxelCAD.Agent;

/// <summary>The agent assembly's only public entry point.</summary>
public static class AgentFactory
{
    /// <inheritdoc cref="McpEndpoint(Func{string, ICommandDispatcher}, Func{bool})"/>
    public static IMcpEndpoint CreateEndpoint(
        Func<string, ICommandDispatcher> dispatcherForClient,
        Func<bool> stepWorkInFlight) =>
        new McpEndpoint(dispatcherForClient, stepWorkInFlight);
}
