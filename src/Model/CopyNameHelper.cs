using System.Collections.Generic;

namespace VoxelCAD.Model;

/// <summary>
/// What a copy is called where it lands. One rule serves both readings of a paste:
/// a name nobody is using is kept, and a name that is taken grows the next free
/// number.
/// </summary>
/// <remarks>
/// That is not two behaviours but one. Duplicating "Model" finds the original
/// standing there and gives "Model 2"; pasting after a <em>cut</em> finds the name
/// free and puts it back exactly as it was, which is what a cut and paste is
/// supposed to be. Counting from the name's own trailing number is what keeps a
/// copy of "Model 2" from being called "Model 2 2".
/// </remarks>
internal static class CopyNameHelper
{
    public static string Compose(string name, HashSet<string> takenHashSet)
    {
        if (!takenHashSet.Contains(name))
            return name;

        string stem = SplitStem(name, out int index);

        for (var next = index + 1; ; next++)
        {
            var candidate = $"{stem} {next}";

            if (!takenHashSet.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// The name without its trailing number, and the number itself — 1 for a name
    /// that has none, so the first copy of "Model" is "Model 2" either way.
    /// </summary>
    private static string SplitStem(string name, out int index)
    {
        index = 1;

        var space = name.LastIndexOf(' ');

        // Nothing before the space is not a stem: " 2" would otherwise produce a
        // string of bare numbers.
        if (space <= 0 || !int.TryParse(name[(space + 1)..], out int parsed) || parsed < 1)
            return name;

        index = parsed;

        return name[..space];
    }
}
