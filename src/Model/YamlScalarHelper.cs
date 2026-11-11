using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VoxelCAD.Model;

/// <summary>
/// Decides whether a string has to be quoted to survive a YAML round trip.
/// </summary>
/// <remarks>
/// Element names come from the user, and plenty of them mean something else in
/// YAML: <c>NO</c> is false, <c>1.10</c> is a number, <c>~</c> is null,
/// <c>12:30</c> is sexagesimal. Quoting everything would also quote the keys and
/// cost the readability the format was chosen for, so only ambiguous scalars get
/// quotes.
/// </remarks>
internal static class YamlScalarHelper
{
    private const string IndicatorCharacters = "-?:,[]{}#&*!|>'\"%@`";

    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "y", "Y", "yes", "Yes", "YES", "n", "N", "no", "No", "NO",
        "true", "True", "TRUE", "false", "False", "FALSE",
        "on", "On", "ON", "off", "Off", "OFF",
        "null", "Null", "NULL", "~"
    };

    public static bool NeedsQuoting(string value)
    {
        if (value.Length == 0)
            return true;

        if (ReservedWords.Contains(value))
            return true;

        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
            return true;

        if (IndicatorCharacters.Contains(value[0]))
            return true;

        return LooksNumeric(value);
    }

    private static bool LooksNumeric(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return true;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return true;

        if (value.StartsWith("0x", StringComparison.Ordinal)
            || value.StartsWith("0o", StringComparison.Ordinal)
            || value.StartsWith("0b", StringComparison.Ordinal))
        {
            return true;
        }

        if (value.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".nan", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Sexagesimal: 12:30 is a number in YAML 1.1, which YamlDotNet follows.
        if (value.Contains(':')
            && value.Split(':').All(part => part.Length > 0 && part.All(char.IsDigit)))
        {
            return true;
        }

        // A timestamp in YAML 1.1. Our own round trip survives it — the target
        // property is a string — but any other reader would see a date.
        return value.Length >= 8
            && value.Count(character => character == '-') >= 2
            && value.Split('-').All(part => part.Length > 0 && part.All(char.IsDigit));
    }
}
