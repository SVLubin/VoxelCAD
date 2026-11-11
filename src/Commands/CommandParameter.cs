namespace VoxelCAD.Commands;

/// <summary>
/// One argument of a command, described well enough for a console to print help
/// and for an MCP transport to publish a tool schema from the same source.
/// </summary>
public readonly record struct CommandParameter(
    string Name,
    string Summary,
    bool Required);
