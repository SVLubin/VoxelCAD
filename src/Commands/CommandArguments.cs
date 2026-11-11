using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using VoxelCAD.Model;

namespace VoxelCAD.Commands;

/// <summary>
/// Typed access to what a caller sent, with failures phrased for whoever sent it.
/// Values arrive as text because the console is text-native and JSON converts to
/// it without loss for the shapes commands accept.
/// </summary>
public sealed class CommandArguments
{
    private readonly IReadOnlyDictionary<string, string> _valuesByNameDictionary;

    public CommandArguments(IReadOnlyDictionary<string, string> valuesByNameDictionary)
    {
        _valuesByNameDictionary = valuesByNameDictionary;
    }

    public bool Has(string name) => _valuesByNameDictionary.ContainsKey(name);

    public string String(string name) =>
        _valuesByNameDictionary.TryGetValue(name, out string? value)
            ? value
            : throw new CommandException($"Missing required argument '{name}'.");

    public string String(string name, string fallback) =>
        _valuesByNameDictionary.TryGetValue(name, out string? value) ? value : fallback;

    public int Int(string name)
    {
        var text = String(name);

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new CommandException($"Argument '{name}' must be a whole number, got '{text}'.");
    }

    public float Float(string name)
    {
        var text = String(name);

        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : throw new CommandException($"Argument '{name}' must be a number, got '{text}'.");
    }

    /// <summary>The whole number, or the fallback where it was not given.</summary>
    public int Int(string name, int fallback) => Has(name) ? Int(name) : fallback;

    /// <summary>
    /// The number, or what it already was. For a command that tunes one value of
    /// several: what is omitted must be left alone, not reset.
    /// </summary>
    public float Float(string name, float fallback) => Has(name) ? Float(name) : fallback;

    public bool Bool(string name, bool fallback)
    {
        if (!Has(name))
            return fallback;

        var text = String(name);

        return text.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => true,
            "false" or "0" or "no" => false,
            _ => throw new CommandException($"Argument '{name}' must be true or false, got '{text}'.")
        };
    }

    public Vector3 Vector(string name)
    {
        var text = String(name);
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != 3)
            throw new CommandException($"Argument '{name}' must be 'x,y,z', got '{text}'.");

        var values = new float[3];

        for (var i = 0; i < 3; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                throw new CommandException($"Argument '{name}' must be 'x,y,z', got '{text}'.");
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    public Vector3I VectorI(string name)
    {
        Vector3 value = Vector(name);

        return new Vector3I((int)value.X, (int)value.Y, (int)value.Z);
    }

    public T Enum<T>(string name) where T : struct, Enum
    {
        var text = String(name);

        if (System.Enum.TryParse(text, ignoreCase: true, out T value))
            return value;

        throw new CommandException(
            $"Argument '{name}' must be one of {string.Join(", ", System.Enum.GetNames<T>())}, got '{text}'.");
    }

    public T Enum<T>(string name, T fallback) where T : struct, Enum =>
        Has(name) ? Enum<T>(name) : fallback;

    /// <summary>"r,g,b" or "r,g,b,a", each channel from zero to one.</summary>
    public Color Color(string name)
    {
        var text = String(name);
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length is not (3 or 4))
            throw new CommandException($"Argument '{name}' must be 'r,g,b' or 'r,g,b,a', got '{text}'.");

        var channels = new float[4];
        channels[3] = 1f;

        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out channels[i]))
                throw new CommandException($"Argument '{name}' must be numbers, got '{text}'.");
        }

        return new Color(channels[0], channels[1], channels[2], channels[3]);
    }

    /// <summary>Cells as "x,y,z;x,y,z" — one string keeps a whole stroke in a single command.</summary>
    public IReadOnlyList<Vector3I> Cells(string name)
    {
        var cellsList = new List<Vector3I>();

        foreach (string cell in String(name).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = cell.Split(',', StringSplitOptions.TrimEntries);

            if (parts.Length != 3
                || !int.TryParse(parts[0], out int x)
                || !int.TryParse(parts[1], out int y)
                || !int.TryParse(parts[2], out int z))
            {
                throw new CommandException(
                    $"Argument '{name}' must be 'x,y,z;x,y,z', got '{cell}'.");
            }

            cellsList.Add(new Vector3I(x, y, z));
        }

        return cellsList;
    }
}
