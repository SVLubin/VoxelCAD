using System;

namespace VoxelCAD.Commands;

/// <summary>
/// A command was asked for something it cannot do. Carries a message meant to be
/// read by whoever sent the command — a person or a model — rather than a stack
/// trace.
/// </summary>
public sealed class CommandException : Exception
{
    public CommandException(string message) : base(message)
    {
    }
}
