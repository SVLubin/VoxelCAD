using System;
using System.Collections.Generic;
using System.Text;

namespace VoxelCAD.Commands;

/// <summary>
/// Turns <c>create-element name="My bracket" kind=voxelmodel</c> into a command
/// name and named arguments. Used by text transports; MCP already arrives
/// structured and skips this entirely.
/// </summary>
public static class CommandLineParser
{
    public static bool TryParse(
        string line,
        out string name,
        out IReadOnlyDictionary<string, string> arguments,
        out string? error)
    {
        name = string.Empty;
        arguments = new Dictionary<string, string>();
        error = null;

        List<string> tokensList = Tokenize(line);

        if (tokensList.Count == 0)
        {
            error = "Empty command.";
            return false;
        }

        name = tokensList[0];
        var valuesByNameDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < tokensList.Count; i++)
        {
            var separator = tokensList[i].IndexOf('=');

            if (separator <= 0)
            {
                error = $"Expected 'name=value', got '{tokensList[i]}'.";
                return false;
            }

            valuesByNameDictionary[tokensList[i][..separator]] = tokensList[i][(separator + 1)..];
        }

        arguments = valuesByNameDictionary;

        return true;
    }

    /// <summary>Splits on whitespace, except inside double quotes, so names may contain spaces.</summary>
    private static List<string> Tokenize(string line)
    {
        var tokensList = new List<string>();
        var token = new StringBuilder();
        var quoted = false;

        foreach (char character in line)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(character))
            {
                if (token.Length > 0)
                {
                    tokensList.Add(token.ToString());
                    token.Clear();
                }

                continue;
            }

            token.Append(character);
        }

        if (token.Length > 0)
            tokensList.Add(token.ToString());

        return tokensList;
    }
}
