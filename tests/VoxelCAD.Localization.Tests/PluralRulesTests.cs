using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Localization;
using Xunit;

namespace VoxelCAD.Localization.Tests;

/// <summary>
/// Which form a number takes, per language.
/// </summary>
/// <remarks>
/// The table is small and the temptation is to trust it by reading. These exist
/// because the two rules that differ from "one or many" differ in ways that look
/// like typos: Russian's teens are an exception inside an exception, and French
/// counts zero as singular where German does not.
/// </remarks>
public sealed class PluralRulesTests
{
    /// <summary>
    /// The invariant the catalogue lookup rests on: a form index is always one
    /// the catalogue could have. A rule that answered outside its own count
    /// would read past the end of a translation's forms, which
    /// <see cref="TranslationCatalog.FindPlural"/> would then report as missing
    /// for every count that reached it.
    /// </summary>
    [Fact]
    public void NoRuleEverAnswersOutsideItsOwnFormCount()
    {
        foreach (var language in LanguageCodes.All)
        {
            var forms = PluralRules.GetFormCount(language);

            for (var count = 0; count <= 1000; count++)
            {
                var form = PluralRules.SelectForm(language, count);

                Assert.InRange(form, 0, forms - 1);
            }
        }
    }

    /// <summary>
    /// The header is what Poedit and Weblate read to decide how many boxes to
    /// show a translator, and the table is what the editor reads to pick between
    /// them. If the two ever disagreed about how many there are, a translator
    /// would fill in a form nothing could reach — or leave one blank that
    /// everything did.
    /// </summary>
    [Fact]
    public void TheDeclaredHeaderAgreesWithTheTableAboutHowManyFormsThereAre()
    {
        foreach (var language in LanguageCodes.All)
        {
            var header = PluralRules.GetGettextHeader(language);
            var declared = ReadDeclaredFormCount(header);

            Assert.Equal(PluralRules.GetFormCount(language), declared);
        }
    }

    [Theory]
    // One is one; everything else, zero included, is many.
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(11, 1)]
    [InlineData(21, 1)]
    public void EnglishCountsOneAgainstTheRest(int count, int expected) =>
        Assert.Equal(expected, PluralRules.SelectForm(EditorLanguage.English, count));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void GermanAndSpanishCountLikeEnglish(int count, int expected)
    {
        Assert.Equal(expected, PluralRules.SelectForm(EditorLanguage.German, count));
        Assert.Equal(expected, PluralRules.SelectForm(EditorLanguage.Spanish, count));
    }

    /// <summary>
    /// Zero is singular in both — "0 fichier", "0 arquivo" — which is the one
    /// place they part company with German and the reason they are not the same
    /// row of the table.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(21, 1)]
    public void FrenchAndBrazilianPortugueseCountZeroAsOne(int count, int expected)
    {
        Assert.Equal(expected, PluralRules.SelectForm(EditorLanguage.French, count));
        Assert.Equal(
            expected,
            PluralRules.SelectForm(EditorLanguage.BrazilianPortuguese, count));
    }

    /// <summary>
    /// One, few, many. The teens are what makes this more than "look at the last
    /// digit": 11 ends in 1 and is not one of anything.
    /// </summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(21, 0)]
    [InlineData(101, 0)]
    [InlineData(1001, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(22, 1)]
    [InlineData(24, 1)]
    [InlineData(104, 1)]
    [InlineData(0, 2)]
    [InlineData(5, 2)]
    [InlineData(10, 2)]
    [InlineData(20, 2)]
    [InlineData(25, 2)]
    [InlineData(100, 2)]
    public void RussianCountsInOneFewAndMany(int count, int expected) =>
        Assert.Equal(expected, PluralRules.SelectForm(EditorLanguage.Russian, count));

    /// <summary>
    /// Eleven through fourteen end in the digits that would otherwise take the
    /// first two forms, and take the third instead. Written out because getting
    /// this wrong is the single most common way a Russian plural rule is wrong,
    /// and it is invisible until somebody has eleven of something.
    /// </summary>
    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(111)]
    [InlineData(112)]
    public void RussianTeensAreTheExceptionThatOutranksTheLastDigit(int count) =>
        Assert.Equal(2, PluralRules.SelectForm(EditorLanguage.Russian, count));

    /// <summary>
    /// C# keeps the sign of the left operand through <c>%</c>, so a negative
    /// count would miss every branch of the Russian rule and land on the last
    /// form by accident. Nothing counts backwards; this makes that assumption
    /// harmless rather than silent.
    /// </summary>
    [Fact]
    public void ANegativeCountIsReadAsItsMagnitude()
    {
        foreach (var language in LanguageCodes.All)
        {
            for (var count = 1; count <= 30; count++)
            {
                Assert.Equal(
                    PluralRules.SelectForm(language, count),
                    PluralRules.SelectForm(language, -count));
            }
        }
    }

    /// <summary>
    /// The one magnitude that has no positive counterpart, and so is the one
    /// value <see cref="Math.Abs(int)"/> would throw on.
    /// </summary>
    [Fact]
    public void TheSmallestIntegerDoesNotThrow()
    {
        foreach (var language in LanguageCodes.All)
        {
            var form = PluralRules.SelectForm(language, int.MinValue);

            Assert.InRange(form, 0, PluralRules.GetFormCount(language) - 1);
        }
    }

    private static int ReadDeclaredFormCount(string header)
    {
        const string key = "nplurals=";

        var start = header.IndexOf(key, StringComparison.Ordinal);

        Assert.True(start >= 0, $"No nplurals in '{header}'.");

        var digits = header[(start + key.Length)..];
        var end = 0;

        while (end < digits.Length && char.IsDigit(digits[end]))
            end++;

        return int.Parse(digits[..end]);
    }
}
