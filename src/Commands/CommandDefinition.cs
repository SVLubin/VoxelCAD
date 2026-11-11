using System;

namespace VoxelCAD.Commands;

/// <summary>
/// A descriptor and the code behind it, kept together so neither drifts. Public
/// so a host can register commands the base catalog cannot express — ones that
/// reach the editor session or the running scene rather than the document.
/// </summary>
public sealed class CommandDefinition
{
    public CommandDefinition(
        CommandDescriptor descriptor,
        Func<CommandContext, CommandArguments, CommandResult> handler)
    {
        Descriptor = descriptor;
        Handler = handler;
    }

    public CommandDescriptor Descriptor { get; }

    public Func<CommandContext, CommandArguments, CommandResult> Handler { get; }
}
