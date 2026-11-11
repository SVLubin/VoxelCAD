using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Localization;

/// <summary>
/// What a language is called in a file: the code stored in the preferences and
/// the name of the catalogue it loads.
/// </summary>
/// <remarks>
/// One fact rather than two. The preferences store a language as a word, on the
/// standing rule that the file is meant to stay worth opening in an editor — and
/// the catalogue is named after that same word, so a code written down in the
/// preferences layer and a filename written down here would agree by coincidence
/// rather than by construction. This is the reason the preferences layer
/// references this one at all; it is a leaf, so the arrow costs nothing.
/// <para>
/// The codes are BCP 47, which is not decoration: they are what Poedit and
/// Weblate expect a catalogue to be called, and getting a community translation
/// back is the whole reason for the format.
/// </para>
/// </remarks>
public static class LanguageCodes
{
    /// <summary>
    /// Every language the editor ships, in the order a settings form should
    /// offer them: the source language first, then by code.
    /// </summary>
    public static readonly IReadOnlyList<EditorLanguage> All = new[]
    {
        EditorLanguage.English,
        EditorLanguage.German,
        EditorLanguage.Spanish,
        EditorLanguage.French,
        EditorLanguage.BrazilianPortuguese,
        EditorLanguage.Russian
    };

    public static string ToCode(EditorLanguage language) =>
        language switch
        {
            EditorLanguage.English => "en",
            EditorLanguage.Russian => "ru",
            EditorLanguage.German => "de",
            EditorLanguage.French => "fr",
            EditorLanguage.Spanish => "es",
            EditorLanguage.BrazilianPortuguese => "pt-BR",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };

    /// <summary>
    /// The language a stored code names, or null when it names none — which is a
    /// damaged or hand-edited file, and the caller falls back to
    /// <see cref="EditorLanguage.English"/> rather than refusing to start.
    /// </summary>
    public static EditorLanguage? TryParse(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmed = code.Trim();

        foreach (var language in All)
        {
            if (string.Equals(ToCode(language), trimmed, StringComparison.OrdinalIgnoreCase))
                return language;
        }

        return null;
    }

    /// <summary>
    /// What to call <paramref name="language"/> on a settings form. Each is
    /// written in itself, which is the one convention a list of languages has:
    /// somebody looking for their own language is looking for the word they would
    /// use for it, not for our translation of it.
    /// </summary>
    public static string GetEndonym(EditorLanguage language) =>
        language switch
        {
            EditorLanguage.English => "English",
            EditorLanguage.Russian => "Русский",
            EditorLanguage.German => "Deutsch",
            EditorLanguage.French => "Français",
            EditorLanguage.Spanish => "Español",
            EditorLanguage.BrazilianPortuguese => "Português (Brasil)",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
}
