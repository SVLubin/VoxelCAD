using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using VoxelCAD.Commands;

namespace VoxelCAD.Cli;

/// <summary>
/// Runs command lines and reports each on its own line of JSON, so output stays
/// parseable while a whole session streams.
/// </summary>
/// <remarks>
/// A failing command stops the run. An agent building on a broken state is worse
/// than an agent told early that it went wrong.
/// </remarks>
internal sealed class ConsoleSession
{
    // Relaxed escaping because this goes to a terminal or a file, never into
    // HTML: an apostrophe in an error message should read as one, not \u0027.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ICommandDispatcher _dispatcher;
    private readonly TextWriter _output;

    public ConsoleSession(ICommandDispatcher dispatcher, TextWriter output)
    {
        _dispatcher = dispatcher;
        _output = output;
    }

    public bool Run(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            var trimmed = line.Trim();

            // Blank lines and comments, so a generated script can explain itself.
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            if (!RunLine(trimmed))
                return false;
        }

        return true;
    }

    public bool RunLine(string line)
    {
        if (!CommandLineParser.TryParse(
                line,
                out string name,
                out IReadOnlyDictionary<string, string> arguments,
                out string? error))
        {
            Report(line, CommandResult.Failure(error!));
            return false;
        }

        CommandResult result = _dispatcher.Execute(name, arguments);
        Report(name, result);

        return result.Ok;
    }

    public void ReportCommands()
    {
        foreach (CommandDescriptor command in _dispatcher.Commands)
        {
            _output.WriteLine(JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["command"] = command.Name,
                    ["summary"] = command.Summary,
                    ["parameters"] = ToParameterList(command)
                },
                JsonOptions));
        }
    }

    private static List<object?> ToParameterList(CommandDescriptor command)
    {
        var parametersList = new List<object?>();

        foreach (CommandParameter parameter in command.Parameters)
        {
            parametersList.Add(new Dictionary<string, object?>
            {
                ["name"] = parameter.Name,
                ["summary"] = parameter.Summary,
                ["required"] = parameter.Required
            });
        }

        return parametersList;
    }

    private void Report(string command, CommandResult result)
    {
        var payload = new Dictionary<string, object?>
        {
            ["ok"] = result.Ok,
            ["command"] = command
        };

        if (result.Error != null)
            payload["error"] = result.Error;

        if (result.Value != null)
            payload["value"] = result.Value;

        _output.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
