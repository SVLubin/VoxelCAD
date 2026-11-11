using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Commands;

namespace VoxelCAD.Mcp;

/// <summary>
/// Turns a command descriptor into the JSON Schema a tool advertises. The schema
/// comes from the same descriptors the console prints help from, so the two can
/// never describe different commands.
/// </summary>
internal static class ToolSchemaHelper
{
    public static Dictionary<string, object?> ToTool(CommandDescriptor command) =>
        new()
        {
            ["name"] = command.Name,
            ["description"] = command.Summary,
            ["inputSchema"] = ToInputSchema(command)
        };

    private static Dictionary<string, object?> ToInputSchema(CommandDescriptor command)
    {
        var propertiesDictionary = new Dictionary<string, object?>();

        foreach (CommandParameter parameter in command.Parameters)
        {
            propertiesDictionary[parameter.Name] = new Dictionary<string, object?>
            {
                // Everything is a string: the formats are compact and documented in
                // the description, and one type means one parsing path shared with
                // the console.
                ["type"] = "string",
                ["description"] = parameter.Summary
            };
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = propertiesDictionary,
            ["required"] = command.Parameters
                .Where(parameter => parameter.Required)
                .Select(parameter => (object?)parameter.Name)
                .ToList()
        };
    }
}
