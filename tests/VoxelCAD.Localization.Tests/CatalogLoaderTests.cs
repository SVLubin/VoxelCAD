using VoxelCAD.CommonTypes;
using VoxelCAD.Localization;
using Xunit;

namespace VoxelCAD.Localization.Tests;

/// <summary>
/// Whether the catalogues that were built into the assembly can be found again.
/// </summary>
/// <remarks>
/// This is the one seam nothing else would catch. MSBuild derives an embedded
/// resource's logical name from the root namespace and the path, and is entitled
/// to mangle whatever is not an identifier — <c>pt-BR</c> carries a hyphen. A
/// name that did not match would not fail the build; it would ship an editor
/// that quietly ran in English.
/// </remarks>
public sealed class CatalogLoaderTests
{
    /// <summary>
    /// English has no catalogue and needs none: it is what the messages are keyed
    /// by, so every lookup missing is exactly right.
    /// </summary>
    [Fact]
    public void EnglishLoadsAnEmptyCatalogue()
    {
        var catalog = CatalogLoader.Load(EditorLanguage.English);

        Assert.Equal(EditorLanguage.English, catalog.Language);
        Assert.Null(catalog.Find("Toolbar"));
    }

    [Fact]
    public void EveryShippedLanguageLoadsWithoutThrowing()
    {
        foreach (var language in LanguageCodes.All)
        {
            var catalog = CatalogLoader.Load(language);

            Assert.Equal(language, catalog.Language);
        }
    }

    /// <summary>
    /// A catalogue that is embedded but unreachable answers exactly like one that
    /// was never written, so the check has to be that something specific came
    /// back — not merely that nothing threw.
    /// </summary>
    [Fact]
    public void TheRussianCatalogueIsReachableAndCarriesItsMessages()
    {
        var catalog = CatalogLoader.Load(EditorLanguage.Russian);

        Assert.Equal("Инструменты", catalog.Find("Toolbar"));
        Assert.Equal("Проект", catalog.Find("Project"));
    }
}
