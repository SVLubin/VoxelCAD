using System.Collections.Generic;
using System.Linq;

namespace VoxelCAD.Commands;

/// <summary>The command assembly's only public entry point.</summary>
public static class CommandFactory
{
    /// <summary>
    /// Builds a dispatcher over the base catalog, plus any host commands — the
    /// editor adds control and camera commands that only make sense with a user
    /// and a window present.
    /// </summary>
    public static ICommandDispatcher CreateDispatcher(
        CommandContext context,
        IEnumerable<CommandDefinition>? hostCommands = null)
    {
        IReadOnlyList<CommandDefinition> definitionsList = hostCommands == null
            ? CommandCatalog.Build()
            : CommandCatalog.Build().Concat(hostCommands).ToList();

        return new CommandDispatcher(context, definitionsList);
    }
}
