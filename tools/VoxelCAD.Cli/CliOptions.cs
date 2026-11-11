using System;
using System.Collections.Generic;

namespace VoxelCAD.Cli;

/// <summary>
/// What the process was asked to do. <c>--in</c> and <c>--out</c> are sugar over
/// the <c>open</c> and <c>save</c> commands rather than a second mechanism, so a
/// one-shot invocation and a script speak the same language.
/// </summary>
internal sealed class CliOptions
{
    public string? InputPath { get; private set; }

    public string? OutputPath { get; private set; }

    public string? ScriptPath { get; private set; }

    public bool ReadStandardInput { get; private set; }

    public bool ShowHelp { get; private set; }

    /// <summary>Speak MCP over standard input and output instead of running commands.</summary>
    public bool Mcp { get; private set; }

    public IReadOnlyList<string> CommandWords { get; private set; } = Array.Empty<string>();

    public static CliOptions Parse(string[] arguments)
    {
        var options = new CliOptions();
        var wordsList = new List<string>();

        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "--in":
                    options.InputPath = Next(arguments, ref i, "--in");
                    break;

                case "--out":
                    options.OutputPath = Next(arguments, ref i, "--out");
                    break;

                case "--script":
                    options.ScriptPath = Next(arguments, ref i, "--script");
                    break;

                case "--mcp":
                    options.Mcp = true;
                    break;

                case "--stdin" or "-":
                    options.ReadStandardInput = true;
                    break;

                case "--help" or "-h" or "help":
                    options.ShowHelp = true;
                    break;

                default:
                    wordsList.Add(arguments[i]);
                    break;
            }
        }

        options.CommandWords = wordsList;

        return options;
    }

    private static string Next(string[] arguments, ref int index, string option)
    {
        if (++index >= arguments.Length)
            throw new ArgumentException($"{option} needs a value.");

        return arguments[index];
    }
}
