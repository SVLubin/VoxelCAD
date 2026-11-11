using System;
using System.Collections.Generic;
using System.IO;
using VoxelCAD.Cli;
using VoxelCAD.Commands;
using VoxelCAD.Model;
using VoxelCAD.Mcp;

CliOptions options;

try
{
    options = CliOptions.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

// The library the editor ships with, so a file whose cells name one resolves
// here too. Installed rather than implicit — see StandardMaterials.
StandardMaterials.Install();

var context = new CommandContext();
ICommandDispatcher dispatcher = CommandFactory.CreateDispatcher(context);
var session = new ConsoleSession(dispatcher, Console.Out);

if (options.Mcp)
{
    // Standard output carries MCP messages and nothing else, so --in cannot
    // report through it; open the project quietly before the pump starts.
    if (options.InputPath != null)
        context.SetProject(ModelFactory.LoadProject(options.InputPath), options.InputPath);

    new StdioMcpPump(McpFactory.CreateServer(dispatcher), Console.In, Console.Out).Run();

    return 0;
}

if (options.ShowHelp)
{
    Console.Error.WriteLine(
        """
        voxelcad — drives a VoxelCAD project without an editor.

          voxelcad --in part.vcad tree
          voxelcad --in part.vcad --out part.vcad rename-element element=Bracket name=Strut
          voxelcad --script build.txt --out part.vcad
          cat build.txt | voxelcad --stdin --out part.vcad
          voxelcad --help

        Options:
          --in PATH      open this project first
          --out PATH     save to this path when everything succeeded
          --script FILE  run one command per line; # starts a comment
          --stdin        read commands from standard input
          --mcp          speak MCP over standard input and output

        Results are one JSON object per line. The commands themselves follow.
        """);

    session.ReportCommands();

    return 0;
}

// --in and --out are sugar over the ordinary open and save commands, so a
// one-shot invocation and a script speak exactly the same language.
if (options.InputPath != null && !session.RunLine($"open path={Quote(options.InputPath)}"))
    return 1;

var completed = true;

if (options.ScriptPath != null)
    completed = session.Run(File.ReadLines(options.ScriptPath));
else if (options.ReadStandardInput)
    completed = session.Run(ReadLines(Console.In));
else if (options.CommandWords.Count > 0)
    completed = session.RunLine(string.Join(' ', options.CommandWords));
else
    completed = false;

if (!completed)
    return 1;

if (options.OutputPath != null && !session.RunLine($"save path={Quote(options.OutputPath)}"))
    return 1;

return 0;

static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

static IEnumerable<string> ReadLines(TextReader reader)
{
    while (reader.ReadLine() is { } line)
        yield return line;
}
