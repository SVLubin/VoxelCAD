using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Localization;

/// <summary>
/// The editor's text, in the language it is currently set to. Reached through
/// <c>using static</c>, so a call site reads <c>Localized("Undo")</c>.
/// </summary>
/// <remarks>
/// This is the one piece of ambient mutable state in the codebase, and it is
/// deliberate rather than a shortcut. Every other cross-cutting answer here is
/// either pure (<see cref="PluralRules"/>, <c>EditorAxisColors</c>) or reached
/// through an object somebody was handed — but text is composed in some six
/// hundred places across a hundred and fifty view files and a dozen presenters,
/// most of them in constructors and field initialisers that no seam reaches.
/// Godot makes <c>tr()</c> a method on <c>Object</c> for the same reason, and
/// that hook does not extend to presenters, which are tested without an engine.
/// <para>
/// What keeps it honest is that the state is one field with one writer:
/// <see cref="Use"/> is called by the composition root as the preference is read
/// and whenever it changes, and by tests that want a catalogue of their own. The
/// default is an empty English catalogue, so anything that never calls
/// <see cref="Use"/> — every test that does not care, the headless CLI — shows
/// the English written at the call site.
/// </para>
/// <para>
/// <b>Numbers are not localized.</b> Formatting goes through the current culture
/// exactly as the interpolated strings it replaces did, so switching to Russian
/// changes the words and leaves <c>17.5</c> reading as <c>17.5</c>. Deliberate
/// for a first cut: a decimal comma has to agree with what the inspector's spin
/// boxes parse, which is a decision about input rather than about display.
/// </para>
/// </remarks>
public static class Translations
{
    private static volatile TranslationCatalog _catalog =
        TranslationCatalog.CreateEmpty(EditorLanguage.English);

    /// <summary>
    /// Raised once the catalogue has been swapped. What listens is chrome that
    /// has already drawn its text and has to draw it again — the same shape as a
    /// theme repaint, and with the same trap: a string read once into a field is
    /// exactly what this cannot reach.
    /// </summary>
    public static event Action? LanguageChanged;

    public static EditorLanguage Language => _catalog.Language;

    /// <summary>
    /// Puts <paramref name="catalog"/> in force and tells whoever is listening.
    /// </summary>
    public static void Use(TranslationCatalog catalog)
    {
        if (catalog is null)
            throw new ArgumentNullException(nameof(catalog));

        if (ReferenceEquals(_catalog, catalog))
            return;

        _catalog = catalog;
        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// <paramref name="text"/> in the current language, or <paramref name="text"/>
    /// itself where the catalogue has no entry for it — which is what makes a
    /// half-finished translation usable rather than full of holes.
    /// </summary>
    public static string Localized(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));

        return _catalog.Find(text) ?? text;
    }

    /// <summary>
    /// <paramref name="text"/> in the current language, with
    /// <paramref name="args"/> filled into its placeholders.
    /// </summary>
    /// <remarks>
    /// Placeholders are numbered (<c>{0}</c>) rather than positional, which is
    /// the whole reason this exists instead of interpolation at the call site: a
    /// translator has to be able to put the subject after the verb, and a string
    /// built by <c>$"..."</c> has already decided the order.
    /// </remarks>
    public static string Localized(string text, params object?[] args)
    {
        var template = Localized(text);

        return args is null || args.Length == 0 ? template : Format(template, args);
    }

    /// <summary>
    /// The form of a counted message that <paramref name="count"/> takes in the
    /// current language, with <paramref name="args"/> filled into it.
    /// </summary>
    /// <param name="singular">The English text for one, and the catalogue's key.</param>
    /// <param name="plural">The English text for more than one.</param>
    /// <remarks>
    /// English has two forms and Russian three, so the count decides an index
    /// rather than a boolean: the two English texts are what the source is
    /// written in, and how many forms the target has is
    /// <see cref="PluralRules"/>'. Where the catalogue has no entry — or has
    /// fewer forms than its language claims — the English is used, chosen by
    /// English's own rule so the fallback is a correct sentence rather than the
    /// singular with a foreign number in it.
    /// <para>
    /// The count is not passed to the format automatically. Every one of these
    /// messages names something as well as counting it, so the caller passes
    /// both, in the order its own template says.
    /// </para>
    /// </remarks>
    public static string LocalizedPlural(string singular, string plural, int count, params object?[] args)
    {
        if (singular is null)
            throw new ArgumentNullException(nameof(singular));

        if (plural is null)
            throw new ArgumentNullException(nameof(plural));

        var catalog = _catalog;
        var form = PluralRules.SelectForm(catalog.Language, count);
        var template = catalog.FindPlural(singular, form)
            ?? (PluralRules.SelectForm(EditorLanguage.English, count) == 0 ? singular : plural);

        return args is null || args.Length == 0 ? template : Format(template, args);
    }

    /// <summary>
    /// Fills a template, and answers with the template itself when it cannot.
    /// </summary>
    /// <remarks>
    /// A translation is data from outside, and a translator who drops a
    /// placeholder or writes <c>{0]</c> would otherwise take the editor down at
    /// whatever moment that string was due to be drawn. Showing the raw template
    /// is visibly wrong in a way that names its own cause, which is the right
    /// failure for a catalogue that is meant to be sent back and fixed.
    /// </remarks>
    private static string Format(string template, object?[] args)
    {
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
