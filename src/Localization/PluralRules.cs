using System;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Localization;

/// <summary>
/// How many forms a language counts in, and which of them a given number takes.
/// </summary>
/// <remarks>
/// A table rather than an evaluator for the <c>Plural-Forms</c> expression a
/// <c>.po</c> header carries. That expression is a fragment of C, and running one
/// means writing a parser and an evaluator for a language this codebase does not
/// otherwise speak — against six formulas that are known, short and stable. What
/// the table costs is that a seventh language is a change here as well as a new
/// catalogue, which is the same bargain <see cref="EditorLanguage"/> is a closed
/// set for.
/// <para>
/// The header is still written into every catalogue, because Poedit and Weblate
/// read it to decide how many boxes to show a translator — and
/// <see cref="GetGettextHeader"/> is where that text comes from, so the rule this
/// file implements and the rule a translator is shown cannot come apart.
/// </para>
/// </remarks>
public static class PluralRules
{
    /// <summary>
    /// How many plural forms <paramref name="language"/> distinguishes. This is
    /// how many <c>msgstr[n]</c> entries a plural message needs.
    /// </summary>
    public static int GetFormCount(EditorLanguage language) =>
        language switch
        {
            EditorLanguage.Russian => 3,
            EditorLanguage.English => 2,
            EditorLanguage.German => 2,
            EditorLanguage.French => 2,
            EditorLanguage.Spanish => 2,
            EditorLanguage.BrazilianPortuguese => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };

    /// <summary>
    /// Which form <paramref name="count"/> takes in <paramref name="language"/> —
    /// an index into the catalogue's <c>msgstr[n]</c> list, always inside
    /// <see cref="GetFormCount"/>.
    /// </summary>
    /// <remarks>
    /// The count is taken as its magnitude. C#'s <c>%</c> keeps the sign of its
    /// left operand, so a negative number would miss every branch of the Russian
    /// rule and land on the last form by accident rather than by decision; no
    /// caller counts backwards, and this makes that assumption harmless instead
    /// of silent.
    /// </remarks>
    public static int SelectForm(EditorLanguage language, int count)
    {
        var n = count == int.MinValue ? int.MaxValue : Math.Abs(count);

        return language switch
        {
            // One, few, many — the rule every East Slavic language keeps: 1, 21,
            // 31 take the first; 2..4, 22..24 the second; the teens are the
            // exception that makes 11 differ from 1.
            EditorLanguage.Russian =>
                n % 10 == 1 && n % 100 != 11 ? 0
                : n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 10 || n % 100 >= 20) ? 1
                : 2,

            // Zero is plural: "0 files".
            EditorLanguage.English => n == 1 ? 0 : 1,
            EditorLanguage.German => n == 1 ? 0 : 1,
            EditorLanguage.Spanish => n == 1 ? 0 : 1,

            // Zero is singular: "0 fichier", "0 arquivo". This is the gettext
            // convention for both, and it is what Poedit generates for their
            // headers — so it is the rule a translator will be working to.
            EditorLanguage.French => n > 1 ? 1 : 0,
            EditorLanguage.BrazilianPortuguese => n > 1 ? 1 : 0,

            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
    }

    /// <summary>
    /// The <c>Plural-Forms</c> header for <paramref name="language"/>, as it is
    /// written into that language's catalogue.
    /// </summary>
    public static string GetGettextHeader(EditorLanguage language) =>
        language switch
        {
            EditorLanguage.Russian =>
                "nplurals=3; plural=(n%10==1 && n%100!=11 ? 0 : "
                + "n%10>=2 && n%10<=4 && (n%100<10 || n%100>=20) ? 1 : 2);",
            EditorLanguage.English => "nplurals=2; plural=(n != 1);",
            EditorLanguage.German => "nplurals=2; plural=(n != 1);",
            EditorLanguage.Spanish => "nplurals=2; plural=(n != 1);",
            EditorLanguage.French => "nplurals=2; plural=(n > 1);",
            EditorLanguage.BrazilianPortuguese => "nplurals=2; plural=(n > 1);",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
}
