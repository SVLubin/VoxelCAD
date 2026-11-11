using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using VoxelCAD.Render;
using Xunit;

namespace VoxelCAD.Render.Tests;

/// <summary>
/// The shader is assembled here and compiled on a device, so nothing between the
/// two can say whether it is well formed. What can be held without a device is
/// that the text is not corrupted on its way out — which is exactly how it broke
/// the first time.
/// </summary>
public sealed class ShaderSourceTests
{
    /// <summary>
    /// This machine writes a decimal comma, and the roughness floor went into the
    /// source as <c>0,03</c> — the whole shader then failed to parse on line seven
    /// with a message about an identifier, which names neither the number nor the
    /// locale. Source code is machine-readable text and is written invariantly.
    /// </summary>
    [Fact]
    public void ConstantsAreWrittenWithADecimalPointWhateverTheLocaleIs()
    {
        CultureInfo was = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");

            string source = PathTraceShader.Source;

            Assert.Contains("MinimumRoughness = 0.03", source);
            Assert.Contains("EmissionGain = 24.0", source);
            Assert.DoesNotContain(",", GetConstantLines(source));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = was;
        }
    }

    /// <summary>
    /// Every word GLSL keeps for itself, checked as an identifier in the assembled
    /// source. This is the one class of shader defect that has actually shipped
    /// here, twice — <c>half</c>, and <c>extent</c> before it — and it is invisible
    /// to everything else: the shader is a string until a render starts, so a
    /// reserved word survives every build and every other test and then fails to
    /// compile on a device, with a message about an identifier.
    /// </summary>
    /// <remarks>
    /// Not a substitute for compiling it. What would be is <c>glslangValidator</c>,
    /// which is not installed on this machine and is a decision about what the
    /// repository depends on rather than something to add in passing; until then
    /// this covers the failure mode that has a history. The list is the GLSL
    /// specification's own reserved-for-future-use set, plus the qualifiers a
    /// compute shader has no business declaring.
    /// </remarks>
    [Fact]
    public void NoReservedWordIsUsedAsAnIdentifier()
    {
        IReadOnlyList<string> offenders = FindReservedWords(PathTraceShader.Source);

        Assert.True(
            offenders.Count == 0,
            "GLSL keeps these words for itself, and a shader using one does not "
                + "compile: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// And the check catches what it is for, which is the only reason to believe
    /// the one above. Both halves matter: the word is found where it is code, and
    /// <em>not</em> found where it is only being talked about — most of the places
    /// these words appear in this file are prose explaining them.
    /// </summary>
    [Fact]
    public void TheCheckFindsAReservedWordAndIgnoresOneInAComment()
    {
        Assert.Contains(
            "'half'",
            Assert.Single(FindReservedWords("float half = 0.5;")));

        Assert.Empty(FindReservedWords("// half of it, and /* half again */\nfloat a = 1.0;"));
    }

    /// <summary>Every reserved word used as an identifier, with the line it is on.</summary>
    private static IReadOnlyList<string> FindReservedWords(string source)
    {
        string glsl = WithoutComments(source);
        var offendersList = new List<string>();

        foreach (string word in ReservedWords)
        {
            Match match = Regex.Match(glsl, $@"\b{word}\b");

            if (!match.Success)
                continue;

            int line = glsl.Take(match.Index).Count(c => c == '\n') + 1;

            offendersList.Add($"'{word}' at line {line}");
        }

        return offendersList;
    }

    /// <summary>
    /// The braces balance. A crude check and a cheap one — the source is assembled
    /// from several pieces, and a piece that lost one takes the whole compile with
    /// it for a reason no message names.
    /// </summary>
    [Fact]
    public void TheAssembledSourceIsBalanced()
    {
        string glsl = WithoutComments(PathTraceShader.Source);

        Assert.Equal(glsl.Count(c => c == '{'), glsl.Count(c => c == '}'));
        Assert.Equal(glsl.Count(c => c == '('), glsl.Count(c => c == ')'));
        Assert.Contains("void main()", glsl);
    }

    /// <summary>
    /// Reserved for future use by the specification, plus the qualifiers from the
    /// vertex pipeline that mean nothing here. <c>half</c> is on it because it
    /// shipped.
    /// </summary>
    private static readonly string[] ReservedWords =
    {
        "common", "partition", "active", "asm", "class", "union", "enum", "typedef",
        "template", "this", "goto", "inline", "noinline", "public", "static",
        "extern", "external", "interface", "long", "short", "half", "fixed",
        "unsigned", "superp", "input", "output", "hvec2", "hvec3", "hvec4",
        "fvec2", "fvec3", "fvec4", "sizeof", "cast", "namespace", "using",
        "resource", "attribute", "varying"
    };

    /// <summary>
    /// The source with its comments taken out, so a word explained in prose is not
    /// read as a word used in code — which is most of where these appear.
    /// </summary>
    private static string WithoutComments(string source) =>
        Regex.Replace(
            Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline),
            @"//[^\n]*",
            " ");

    /// <summary>
    /// The lines that carry an interpolated number. Only these: the body of the
    /// shader has commas in it for perfectly good reasons.
    /// </summary>
    private static string GetConstantLines(string source)
    {
        string[] lines = source.Split('\n');
        var kept = new System.Text.StringBuilder();

        foreach (string line in lines)
        {
            if (line.Contains("MinimumRoughness =") || line.Contains("EmissionGain ="))
                kept.AppendLine(line);
        }

        return kept.ToString();
    }
}
