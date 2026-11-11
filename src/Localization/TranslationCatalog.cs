using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Localization;

/// <summary>
/// One language's translations, keyed by the English text they replace.
/// </summary>
/// <remarks>
/// The key is the English source text rather than a symbolic name, which is the
/// gettext convention and is chosen for the reason the format was: a translator
/// working in Poedit or Weblate sees the sentence they are translating, not an
/// identifier they have to look up. The cost is that editing the English text
/// orphans its translation — paid for by
/// <c>TranslationCatalogTests</c>, which walks the source for every call and
/// refuses a template that has drifted from it.
/// </remarks>
public sealed class TranslationCatalog
{
    private readonly IReadOnlyDictionary<string, string> _singularsDictionary;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _pluralsDictionary;

    internal TranslationCatalog(
        EditorLanguage language,
        IReadOnlyDictionary<string, string> singulars,
        IReadOnlyDictionary<string, IReadOnlyList<string>> plurals)
    {
        Language = language;
        _singularsDictionary = singulars;
        _pluralsDictionary = plurals;
    }

    public EditorLanguage Language { get; }

    /// <summary>
    /// An empty catalogue for <paramref name="language"/> — every lookup misses,
    /// so every caller falls back to the English it passed in.
    /// </summary>
    public static TranslationCatalog CreateEmpty(EditorLanguage language) =>
        new(
            language,
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>
    /// The translation of <paramref name="text"/>, or null when this catalogue
    /// has none — which is a message added since the catalogue was last brought
    /// up to date, and the caller shows the English.
    /// </summary>
    public string? Find(string text) =>
        _singularsDictionary.TryGetValue(text, out var translation) ? translation : null;

    /// <summary>
    /// The <paramref name="formIndex"/>th form of the plural message keyed by
    /// <paramref name="singular"/>, or null when this catalogue has no such
    /// message or has fewer forms than asked for.
    /// </summary>
    /// <remarks>
    /// A catalogue with too few forms is a translation whose header disagrees
    /// with <see cref="PluralRules"/> — hand-edited, or written against an older
    /// rule. Answering null rather than throwing is deliberate: the editor shows
    /// English for that one message instead of failing to draw the panel it was
    /// on.
    /// </remarks>
    public string? FindPlural(string singular, int formIndex)
    {
        if (formIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(formIndex), formIndex, null);

        if (!_pluralsDictionary.TryGetValue(singular, out var forms))
            return null;

        return formIndex < forms.Count ? forms[formIndex] : null;
    }
}
