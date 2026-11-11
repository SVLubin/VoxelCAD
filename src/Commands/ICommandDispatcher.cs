using System.Collections.Generic;

namespace VoxelCAD.Commands;

/// <summary>
/// The one command surface over the model. A console and an in-editor MCP server
/// are transports onto this, not separate vocabularies — which is what keeps them
/// from drifting apart.
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>Enough to print help, or to publish a tool schema.</summary>
    IReadOnlyList<CommandDescriptor> Commands { get; }

    /// <summary>Never throws: a refused command comes back as a failed result.</summary>
    CommandResult Execute(string name, IReadOnlyDictionary<string, string> arguments);
}
