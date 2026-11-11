using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// What a picker calls each material it offers. A name and nothing else is not
/// enough for either of the two cases the registry actually reaches.
/// </summary>
/// <remarks>
/// A name may be <b>blank</b> — nothing in the model refuses one — and a blank
/// label is how a context menu spells a separator, so such a row would be drawn
/// as a line and could not be chosen at all. And names are <b>not unique</b>:
/// two materials called "Red" give two identical rows, and whichever is pressed
/// repaints every cell in one of them with nothing on the card saying which. So
/// a blank name is named as blank, and a name shared with another choice carries
/// the head of its id — the same disambiguation <c>ResolveMaterial</c> falls back
/// to when a name matches more than one material.
/// </remarks>
internal static class MaterialChoiceLabels
{
    /// <summary>What a nameless material is called, since a blank row is a separator.</summary>
    public const string Unnamed = "(unnamed)";

    /// <summary>How much of an id is enough to tell two rows apart on a card.</summary>
    public const int IdLength = 8;

    /// <summary>
    /// One label per choice, in the order they were given — the order a picker's
    /// own references are numbered in, so the two cannot come apart.
    /// </summary>
    public static IReadOnlyList<string> Compose(IReadOnlyList<MaterialChoice> choices)
    {
        var countsByNameDictionary = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (MaterialChoice choice in choices)
        {
            countsByNameDictionary.TryGetValue(choice.Name, out int seen);
            countsByNameDictionary[choice.Name] = seen + 1;
        }

        var labelsList = new List<string>(choices.Count);

        foreach (MaterialChoice choice in choices)
        {
            string name = string.IsNullOrWhiteSpace(choice.Name) ? Unnamed : choice.Name;

            labelsList.Add(countsByNameDictionary[choice.Name] > 1
                ? $"{name} · {Head(choice.Id)}"
                : name);
        }

        return labelsList;
    }

    private static string Head(Guid id) => id.ToString("N")[..IdLength];
}
