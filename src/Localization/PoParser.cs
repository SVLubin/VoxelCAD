using System;
using System.Collections.Generic;
using System.Text;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Localization;

/// <summary>
/// Reads a gettext <c>.po</c> catalogue.
/// </summary>
/// <remarks>
/// Ours rather than the engine's, because half the text the editor shows is
/// composed in presenters — which are tested without Godot, so a lookup that
/// needed one could not serve them. It is also what lets the catalogues be
/// embedded in the assembly instead of going through Godot's importer, which
/// would turn each into a <c>.translation</c> resource this layer has no use for.
/// <para>
/// What it deliberately does not do: evaluate the <c>Plural-Forms</c> expression
/// in the header (<see cref="PluralRules"/> says why), or resolve <c>#~</c>
/// obsolete entries, which are a translator's own scratch space and mean nothing
/// to a running editor.
/// </para>
/// </remarks>
public static class PoParser
{
    /// <summary>
    /// Separates a context from its message, as gettext keys the pair. Nothing
    /// here writes one; it is honoured so that a catalogue arriving from a tool
    /// that does cannot have its contextual entry mistaken for the plain one.
    /// </summary>
    private const char ContextSeparator = '\u0004';

    /// <summary>
    /// Parses <paramref name="text"/> into a catalogue for
    /// <paramref name="language"/>.
    /// </summary>
    /// <remarks>
    /// A malformed line is skipped rather than thrown on. A catalogue is data
    /// that arrives from outside — a community translation, edited in some tool
    /// or by hand — and the honest failure for one bad entry is that one message
    /// shows in English, not that the editor will not start. The same bargain
    /// the preferences layer makes with a damaged file.
    /// </remarks>
    public static TranslationCatalog Parse(EditorLanguage language, string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));

        var singulars = new Dictionary<string, string>(StringComparer.Ordinal);
        var plurals = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var entry = new Entry();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\t');

            if (line.Length == 0)
            {
                Flush(entry, singulars, plurals);
                entry = new Entry();
                continue;
            }

            // A translator's own notes, the extractor's references, and the
            // flags. Only "fuzzy" changes what the editor does with the entry.
            if (line[0] == '#')
            {
                if (line.StartsWith("#,", StringComparison.Ordinal)
                    && line.Contains("fuzzy", StringComparison.Ordinal))
                    entry.IsFuzzy = true;

                continue;
            }

            if (TryReadKeyword(line, "msgctxt", out var context))
            {
                Flush(entry, singulars, plurals);
                entry = new Entry { Context = context, Target = EntryField.Context };
                continue;
            }

            if (TryReadKeyword(line, "msgid_plural", out var pluralId))
            {
                entry.PluralId = pluralId;
                entry.Target = EntryField.PluralId;
                continue;
            }

            if (TryReadKeyword(line, "msgid", out var id))
            {
                // A msgid with no blank line before it still starts an entry;
                // catalogues written by hand are not reliably spaced.
                if (entry.Target is EntryField.Singular or EntryField.Plural)
                {
                    Flush(entry, singulars, plurals);
                    entry = new Entry();
                }

                entry.Id = id;
                entry.Target = EntryField.Id;
                continue;
            }

            if (TryReadIndexedKeyword(line, out var index, out var pluralForm))
            {
                entry.SetPluralForm(index, pluralForm);
                entry.Target = EntryField.Plural;
                entry.PluralIndex = index;
                continue;
            }

            if (TryReadKeyword(line, "msgstr", out var translation))
            {
                entry.Translation = translation;
                entry.Target = EntryField.Singular;
                continue;
            }

            // A bare quoted line continues whatever field came last, which is how
            // the format wraps anything longer than a line.
            if (line[0] == '"')
                entry.Append(Unescape(StripQuotes(line)));
        }

        Flush(entry, singulars, plurals);

        return new TranslationCatalog(language, singulars, plurals);
    }

    private static void Flush(
        Entry entry,
        Dictionary<string, string> singulars,
        Dictionary<string, IReadOnlyList<string>> plurals)
    {
        // The header is the entry with an empty msgid. Nothing here reads it:
        // the charset is decided by how the file was loaded, and the plural rule
        // is PluralRules'.
        if (string.IsNullOrEmpty(entry.Id))
            return;

        // Fuzzy means a translator has not confirmed it — gettext's own tools
        // withhold such an entry, and showing English is better than showing a
        // guess somebody has already flagged as one.
        if (entry.IsFuzzy)
            return;

        var key = entry.Context is null ? entry.Id : entry.Context + ContextSeparator + entry.Id;

        if (entry.PluralId is not null)
        {
            var forms = entry.GetPluralForms();

            if (forms.Count > 0)
                plurals[key] = forms;

            return;
        }

        if (!string.IsNullOrEmpty(entry.Translation))
            singulars[key] = entry.Translation;
    }

    private static bool TryReadKeyword(string line, string keyword, out string value)
    {
        value = string.Empty;

        if (!line.StartsWith(keyword, StringComparison.Ordinal))
            return false;

        var rest = line[keyword.Length..].TrimStart();

        if (rest.Length == 0 || rest[0] != '"')
            return false;

        value = Unescape(StripQuotes(rest));

        return true;
    }

    private static bool TryReadIndexedKeyword(string line, out int index, out string value)
    {
        const int prefixLength = 7;

        index = 0;
        value = string.Empty;

        if (!line.StartsWith("msgstr[", StringComparison.Ordinal))
            return false;

        var close = line.IndexOf(']');

        if (close < 0 || !int.TryParse(line[prefixLength..close], out index) || index < 0)
            return false;

        var rest = line[(close + 1)..].TrimStart();

        if (rest.Length == 0 || rest[0] != '"')
            return false;

        value = Unescape(StripQuotes(rest));

        return true;
    }

    /// <summary>
    /// The text between the first and last quote of a line, which is where the
    /// format puts it. Anything after the closing quote is a trailing comment
    /// and is dropped.
    /// </summary>
    private static string StripQuotes(string quoted)
    {
        var first = quoted.IndexOf('"');
        var last = quoted.LastIndexOf('"');

        return last > first ? quoted[(first + 1)..last] : string.Empty;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
            return value;

        var builder = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                builder.Append(value[i]);
                continue;
            }

            i++;

            builder.Append(value[i] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '"' => '"',
                '\\' => '\\',
                'a' => '\a',
                'b' => '\b',
                'f' => '\f',
                'v' => '\v',
                _ => value[i]
            });
        }

        return builder.ToString();
    }

    private enum EntryField
    {
        None,
        Context,
        Id,
        PluralId,
        Singular,
        Plural
    }

    private sealed class Entry
    {
        private readonly Dictionary<int, string> _pluralFormsDictionary = new();

        public string? Context { get; set; }
        public string? Id { get; set; }
        public string? PluralId { get; set; }
        public string Translation { get; set; } = string.Empty;
        public bool IsFuzzy { get; set; }
        public EntryField Target { get; set; } = EntryField.None;
        public int PluralIndex { get; set; }

        public void SetPluralForm(int index, string value) => _pluralFormsDictionary[index] = value;

        public void Append(string value)
        {
            switch (Target)
            {
                case EntryField.Context:
                    Context += value;
                    break;

                case EntryField.Id:
                    Id += value;
                    break;

                case EntryField.PluralId:
                    PluralId += value;
                    break;

                case EntryField.Singular:
                    Translation += value;
                    break;

                case EntryField.Plural:
                    _pluralFormsDictionary[PluralIndex] =
                        _pluralFormsDictionary.GetValueOrDefault(PluralIndex, string.Empty) + value;
                    break;
            }
        }

        /// <summary>
        /// The plural forms in order, or an empty list when any of them is
        /// missing or blank. A partly filled plural is dropped whole: otherwise
        /// whichever count reached the gap would show an empty string, which
        /// reads as a missing word rather than as untranslated English.
        /// </summary>
        public IReadOnlyList<string> GetPluralForms()
        {
            if (_pluralFormsDictionary.Count == 0)
                return Array.Empty<string>();

            var count = 0;

            foreach (var index in _pluralFormsDictionary.Keys)
                count = Math.Max(count, index + 1);

            var forms = new string[count];

            for (var i = 0; i < count; i++)
            {
                if (!_pluralFormsDictionary.TryGetValue(i, out var form) || form.Length == 0)
                    return Array.Empty<string>();

                forms[i] = form;
            }

            return forms;
        }
    }
}
