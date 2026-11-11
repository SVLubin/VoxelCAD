using System.Collections.Generic;
using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// Drives the dispatcher the way a text transport does, so the tests read like
/// the session a person or a model would actually have.
/// </summary>
internal static class DispatcherExtensions
{
    public static CommandResult Run(this ICommandDispatcher dispatcher, string line)
    {
        Assert.True(
            CommandLineParser.TryParse(line, out string name, out IReadOnlyDictionary<string, string> arguments, out string? error),
            error);

        return dispatcher.Execute(name, arguments);
    }

    public static CommandResult RunOk(this ICommandDispatcher dispatcher, string line)
    {
        CommandResult result = dispatcher.Run(line);

        Assert.True(result.Ok, $"'{line}' failed: {result.Error}");

        return result;
    }

    public static string RunFails(this ICommandDispatcher dispatcher, string line)
    {
        CommandResult result = dispatcher.Run(line);

        Assert.False(result.Ok, $"'{line}' unexpectedly succeeded");

        return result.Error!;
    }

    public static string Id(this CommandResult result) => (string)result.Field("id")!;

    public static object? Field(this CommandResult result, string name) =>
        ((IReadOnlyDictionary<string, object?>)result.Value!)[name];
}
