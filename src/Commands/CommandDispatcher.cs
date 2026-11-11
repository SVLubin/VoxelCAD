using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoxelCAD.Commands;

/// <inheritdoc cref="ICommandDispatcher"/>
internal sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly CommandContext _context;
    private readonly Dictionary<string, CommandDefinition> _definitionsByNameDictionary;

    public CommandDispatcher(CommandContext context, IReadOnlyList<CommandDefinition> definitionsList)
    {
        _context = context;
        _definitionsByNameDictionary = definitionsList.ToDictionary(
            definition => definition.Descriptor.Name,
            StringComparer.OrdinalIgnoreCase);

        Commands = definitionsList.Select(definition => definition.Descriptor).ToList();
    }

    public IReadOnlyList<CommandDescriptor> Commands { get; }

    public CommandResult Execute(string name, IReadOnlyDictionary<string, string> arguments)
    {
        if (!_definitionsByNameDictionary.TryGetValue(name, out CommandDefinition? definition))
            return CommandResult.Failure($"Unknown command '{name}'. Try 'help'.");

        if (UnknownArgument(definition.Descriptor, arguments) is { } unknown)
            return CommandResult.Failure(unknown);

        try
        {
            return definition.Handler(_context, new CommandArguments(arguments));
        }
        catch (CommandException exception)
        {
            return CommandResult.Failure(exception.Message);
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException
                or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            // The model refuses illegal edits by throwing. A transport reports
            // that; it does not fall over because a caller asked for something
            // the document does not allow.
            //
            // UnauthorizedAccessException is here because the file system refuses
            // the same way: it does not derive from IOException, so a file the
            // process may not read used to escape this and take the caller down
            // with a stack trace. Every command that names a path meets it —
            // open, save, export, import.
            return CommandResult.Failure(exception.Message);
        }
    }

    /// <summary>
    /// The refusal for the first argument the command does not take, or null when
    /// every one of them is named in its descriptor.
    /// </summary>
    /// <remarks>
    /// An argument a command does not know used to be dropped in silence, which
    /// turns a typo into a different action rather than into a message: 'focus
    /// point=16,1,8' framed the whole scene, 'aim-camera from=... at=...' aimed
    /// without moving, and 'ui contains=relief' answered with the entire control
    /// tree. Each looks exactly like the command being broken, and none of them
    /// says anything. The names it does take are listed, because the whole cost
    /// of the mistake is not knowing what to write instead.
    /// </remarks>
    private static string? UnknownArgument(
        CommandDescriptor descriptor,
        IReadOnlyDictionary<string, string> arguments)
    {
        foreach (string name in arguments.Keys)
        {
            if (descriptor.Parameters.Any(parameter =>
                string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            string known = descriptor.Parameters.Count == 0
                ? "it takes none"
                : "it takes " + string.Join(", ", descriptor.Parameters.Select(p => $"'{p.Name}'"));

            return $"'{descriptor.Name}' has no argument '{name}' — {known}.";
        }

        return null;
    }
}
