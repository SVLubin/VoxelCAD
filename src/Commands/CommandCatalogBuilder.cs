using System;
using System.Collections.Generic;

namespace VoxelCAD.Commands;

/// <summary>
/// Collects the catalog's definitions as the domain classes add theirs, and holds
/// the two parameter shorthands every domain speaks in.
/// </summary>
internal sealed class CommandCatalogBuilder
{
    private readonly List<CommandDefinition> _definitionsList = new();

    public IReadOnlyList<CommandDefinition> Definitions => _definitionsList;

    public void Add(
        string name,
        string summary,
        Func<CommandContext, CommandArguments, CommandResult> handler,
        params CommandParameter[] parameters)
    {
        _definitionsList.Add(new CommandDefinition(
            new CommandDescriptor(name, summary, parameters),
            handler));
    }

    /// <summary>
    /// A command that only reads. Looking is free: an agent may call these without
    /// holding control.
    /// </summary>
    public void AddQuery(
        string name,
        string summary,
        Func<CommandContext, CommandArguments, CommandResult> handler,
        params CommandParameter[] parameters)
    {
        _definitionsList.Add(new CommandDefinition(
            new CommandDescriptor(name, summary, parameters, mutates: false),
            handler));
    }

    public static CommandParameter Required(string name, string summary) =>
        new(name, summary, true);

    public static CommandParameter Optional(string name, string summary) =>
        new(name, summary, false);
}
