using System;
using System.IO;
using VoxelCAD.Mcp;

namespace VoxelCAD.Cli;

/// <summary>
/// Carries MCP messages between the client's pipes and the server.
/// </summary>
/// <remarks>
/// The transport rules are strict and worth stating: one JSON message per line
/// with no embedded newlines, nothing on standard output that is not a message,
/// and standard error free for logging. That last one is why this mode cannot
/// share the ordinary console reporting.
/// </remarks>
internal sealed class StdioMcpPump
{
    /// <summary>
    /// The one client this transport can ever have: a pipe pair belongs to the
    /// process on the other end of it, so there is nobody to tell apart.
    /// </summary>
    private const string ClientId = "stdio";

    private readonly IMcpServer _server;
    private readonly TextReader _input;
    private readonly TextWriter _output;

    public StdioMcpPump(IMcpServer server, TextReader input, TextWriter output)
    {
        _server = server;
        _input = input;
        _output = output;
    }

    public void Run()
    {
        while (_input.ReadLine() is { } line)
        {
            if (line.Trim().Length == 0)
                continue;

            // Blocking is honest here: the console has no handover, so nothing it
            // can call ever leaves an answer pending.
            string? response = _server.HandleMessageAsync(line, ClientId).GetAwaiter().GetResult();

            if (response == null)
                continue;

            _output.WriteLine(response);
            _output.Flush();
        }
    }
}
