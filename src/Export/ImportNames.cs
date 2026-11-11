using System.Collections.Generic;

namespace VoxelCAD.Export;

/// <summary>
/// Names for what an import creates, kept clear of the names already in the
/// document.
/// </summary>
/// <remarks>
/// A name that is free is kept exactly as the file gave it, so a model called
/// Wheel arrives called Wheel; one that is taken grows the next free number from
/// its own trailing one, which is the rule the model itself uses when a paste
/// meets a name it cannot have. It matters more here than for a paste: two
/// materials sharing a name make either of them unaddressable by name for the
/// life of the document, and a file with several unnamed parts would give them
/// all the same one.
/// <para>
/// Which is also why it remembers where it got to per stem. Unnamed parts all
/// fall back to the file's own name, and a MagicaVoxel scene can hold hundreds of
/// them — searching from the beginning each time makes composing N of one name
/// cost N² probes and N² strings, for an answer the previous call already knew.
/// </para>
/// </remarks>
internal sealed class ImportNames
{
    private readonly HashSet<string> _takenHashSet = new();
    private readonly Dictionary<string, int> _nextByStemDictionary = new();

    /// <summary>Marks a name as spoken for without composing anything.</summary>
    public void Reserve(string name) => _takenHashSet.Add(name);

    public string Compose(string name)
    {
        var candidate = string.IsNullOrWhiteSpace(name) ? "Model" : name.Trim();

        if (_takenHashSet.Add(candidate))
            return candidate;

        (string stem, int number) = Split(candidate);

        // Where this stem got to last time, never below the number the name it was
        // asked about already carried.
        if (_nextByStemDictionary.TryGetValue(stem, out int next) && next > number)
            number = next;

        string numbered;

        do
        {
            numbered = $"{stem} {++number}";
        }
        while (!_takenHashSet.Add(numbered));

        _nextByStemDictionary[stem] = number;

        return numbered;
    }

    /// <summary>
    /// A name split into what it is called and the number it already ends with, so
    /// that a second "Wheel 2" becomes "Wheel 3" rather than "Wheel 2 2".
    /// </summary>
    private static (string Stem, int Number) Split(string name)
    {
        var at = name.Length;

        while (at > 0 && char.IsDigit(name[at - 1]))
            at--;

        if (at == name.Length || at == 0 || name[at - 1] != ' ')
            return (name, 1);

        return int.TryParse(name[at..], out int number)
            ? (name[..(at - 1)], number)
            : (name, 1);
    }
}
