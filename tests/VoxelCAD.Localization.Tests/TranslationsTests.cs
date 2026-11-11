using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Localization;
using Xunit;

namespace VoxelCAD.Localization.Tests;

/// <summary>
/// Marks the tests that write the one piece of ambient state in this layer, so
/// xUnit runs them one at a time instead of letting two of them swap the
/// catalogue out from under each other.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TranslationsCollection
{
    public const string Name = "Translations";
}

/// <summary>
/// What a call site gets back.
/// </summary>
/// <remarks>
/// The behaviour worth pinning down is all about what happens when the
/// catalogue does <b>not</b> have the answer: a translation is always partly
/// finished and always slightly out of date, so the fallback is not an error
/// path here — it is the ordinary one for whichever messages were added last.
/// </remarks>
[Collection(TranslationsCollection.Name)]
public sealed class TranslationsTests : IDisposable
{
    public void Dispose() =>
        Translations.Use(TranslationCatalog.CreateEmpty(EditorLanguage.English));

    [Fact]
    public void AMissingMessageFallsBackToTheEnglishAtTheCallSite()
    {
        Translations.Use(TranslationCatalog.CreateEmpty(EditorLanguage.Russian));

        Assert.Equal("Undo", Translations.Localized("Undo"));
    }

    [Fact]
    public void APresentMessageIsTranslated()
    {
        UseCatalog(("Undo", "Отменить"));

        Assert.Equal("Отменить", Translations.Localized("Undo"));
    }

    /// <summary>
    /// The reason placeholders are numbered rather than interpolated at the call
    /// site: a translator has to be able to put the parts in another order, and
    /// a string built by <c>$"..."</c> has already decided it.
    /// </summary>
    [Fact]
    public void ATranslationMayReorderThePartsItIsGiven()
    {
        UseCatalog(("Move {0} to {1}", "В {1} перемещён {0}"));

        Assert.Equal("В Сцена перемещён Куб", Translations.Localized("Move {0} to {1}", "Куб", "Сцена"));
    }

    [Fact]
    public void TheFallbackIsFormattedToo()
    {
        Translations.Use(TranslationCatalog.CreateEmpty(EditorLanguage.Russian));

        Assert.Equal("Move Cube", Translations.Localized("Move {0}", "Cube"));
    }

    /// <summary>
    /// A translation is data from outside, and a dropped or mistyped placeholder
    /// would otherwise take the editor down at whatever moment that label was
    /// due to be drawn. Showing the raw template is visibly wrong in a way that
    /// names its own cause.
    /// </summary>
    [Fact]
    public void ABrokenPlaceholderShowsTheTemplateRatherThanThrowing()
    {
        UseCatalog(("Move {0}", "Перемещён {0]"));

        Assert.Equal("Перемещён {0]", Translations.Localized("Move {0}", "Куб"));
    }

    /// <summary>
    /// A placeholder a translator left out is not an error either — the argument
    /// simply goes unused, which is what <c>string.Format</c> already does.
    /// </summary>
    [Fact]
    public void APlaceholderLeftOutOfATranslationCostsNothing()
    {
        UseCatalog(("Move {0}", "Перемещено"));

        Assert.Equal("Перемещено", Translations.Localized("Move {0}", "Куб"));
    }

    [Fact]
    public void APluralPicksTheFormItsCountTakes()
    {
        UsePluralCatalog(
            EditorLanguage.Russian,
            "{0} action",
            "{0} действие",
            "{0} действия",
            "{0} действий");

        Assert.Equal("1 действие", Translations.LocalizedPlural("{0} action", "{0} actions", 1, 1));
        Assert.Equal("3 действия", Translations.LocalizedPlural("{0} action", "{0} actions", 3, 3));
        Assert.Equal("7 действий", Translations.LocalizedPlural("{0} action", "{0} actions", 7, 7));
        Assert.Equal("11 действий", Translations.LocalizedPlural("{0} action", "{0} actions", 11, 11));
        Assert.Equal("21 действие", Translations.LocalizedPlural("{0} action", "{0} actions", 21, 21));
    }

    /// <summary>
    /// The case that would be wrong in a way nobody notices until it is: with no
    /// translation, the form has to be chosen by <b>English's</b> rule and not by
    /// the language the editor is set to. Russian's rule puts 3 on its second
    /// form, and reading the English pair by that index would hand back the
    /// plural for a count of one.
    /// </summary>
    [Fact]
    public void AnUntranslatedPluralIsChosenByEnglishsOwnRule()
    {
        Translations.Use(TranslationCatalog.CreateEmpty(EditorLanguage.Russian));

        Assert.Equal("1 action", Translations.LocalizedPlural("{0} action", "{0} actions", 1, 1));
        Assert.Equal("3 actions", Translations.LocalizedPlural("{0} action", "{0} actions", 3, 3));
        Assert.Equal("0 actions", Translations.LocalizedPlural("{0} action", "{0} actions", 0, 0));
    }

    /// <summary>
    /// Every one of these messages names something as well as counting it, so the
    /// count is one argument among several rather than an implicit first one.
    /// </summary>
    [Fact]
    public void APluralCarriesWhateverArgumentsItsTemplateNames()
    {
        Translations.Use(TranslationCatalog.CreateEmpty(EditorLanguage.English));

        Assert.Equal(
            "Cube and 1 more",
            Translations.LocalizedPlural("{0} and {1} more", "{0} and {1} more", 1, "Cube", 1));
    }

    [Fact]
    public void SwappingTheCatalogueTellsWhoeverIsListening()
    {
        var raised = 0;

        void OnChanged() => raised++;

        Translations.LanguageChanged += OnChanged;

        try
        {
            Translations.Use(TranslationCatalog.CreateEmpty(EditorLanguage.Russian));

            Assert.Equal(1, raised);
            Assert.Equal(EditorLanguage.Russian, Translations.Language);
        }
        finally
        {
            Translations.LanguageChanged -= OnChanged;
        }
    }

    /// <summary>
    /// Chrome answers a language change by drawing itself again, which is not
    /// free. Putting the catalogue that is already in force back is not a change.
    /// </summary>
    [Fact]
    public void PuttingTheSameCatalogueBackRaisesNothing()
    {
        var catalog = TranslationCatalog.CreateEmpty(EditorLanguage.Russian);

        Translations.Use(catalog);

        var raised = 0;

        void OnChanged() => raised++;

        Translations.LanguageChanged += OnChanged;

        try
        {
            Translations.Use(catalog);

            Assert.Equal(0, raised);
        }
        finally
        {
            Translations.LanguageChanged -= OnChanged;
        }
    }

    private static void UseCatalog(params (string Id, string Translation)[] entries)
    {
        var singulars = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (id, translation) in entries)
            singulars[id] = translation;

        Translations.Use(
            PoParser.Parse(EditorLanguage.Russian, Compose(singulars)));
    }

    private static void UsePluralCatalog(
        EditorLanguage language,
        string singular,
        params string[] forms)
    {
        var text = $"msgid \"{singular}\"\nmsgid_plural \"{singular}s\"\n";

        for (var i = 0; i < forms.Length; i++)
            text += $"msgstr[{i}] \"{forms[i]}\"\n";

        Translations.Use(PoParser.Parse(language, text));
    }

    private static string Compose(IReadOnlyDictionary<string, string> singulars)
    {
        var text = string.Empty;

        foreach (var (id, translation) in singulars)
            text += $"msgid \"{id}\"\nmsgstr \"{translation}\"\n\n";

        return text;
    }
}
