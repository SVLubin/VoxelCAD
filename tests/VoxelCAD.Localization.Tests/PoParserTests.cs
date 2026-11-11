using VoxelCAD.CommonTypes;
using VoxelCAD.Localization;
using Xunit;

namespace VoxelCAD.Localization.Tests;

/// <summary>
/// Reading a catalogue. Every case here is one a real file produces: Poedit
/// wraps long messages, marks unconfirmed ones fuzzy, and writes a header entry
/// that is not a message at all.
/// </summary>
public sealed class PoParserTests
{
    private static TranslationCatalog Parse(string text) =>
        PoParser.Parse(EditorLanguage.Russian, text);

    [Fact]
    public void APairIsRead()
    {
        var catalog = Parse(
            """
            msgid "Undo"
            msgstr "Отменить"
            """);

        Assert.Equal("Отменить", catalog.Find("Undo"));
    }

    /// <summary>
    /// The header is the entry with an empty msgid. It carries the charset and
    /// the plural rule, neither of which this layer reads, and it must not turn
    /// up as a message keyed by the empty string.
    /// </summary>
    [Fact]
    public void TheHeaderIsNotAMessage()
    {
        var catalog = Parse(
            """
            msgid ""
            msgstr ""
            "Content-Type: text/plain; charset=UTF-8\n"
            "Plural-Forms: nplurals=3; plural=(n != 1);\n"

            msgid "Save"
            msgstr "Сохранить"
            """);

        Assert.Null(catalog.Find(string.Empty));
        Assert.Equal("Сохранить", catalog.Find("Save"));
    }

    /// <summary>
    /// How the format wraps anything longer than a line: an empty first string,
    /// then the pieces, which are joined with nothing between them.
    /// </summary>
    [Fact]
    public void ContinuationLinesAreJoinedWithoutSeparators()
    {
        var catalog = Parse(
            """
            msgid ""
            "Move this element into the project's prefab library "
            "and leave an instance behind."
            msgstr ""
            "Перенести элемент в библиотеку заготовок "
            "и оставить на его месте экземпляр."
            """);

        Assert.Equal(
            "Перенести элемент в библиотеку заготовок и оставить на его месте экземпляр.",
            catalog.Find(
                "Move this element into the project's prefab library "
                + "and leave an instance behind."));
    }

    [Fact]
    public void EscapesAreResolved()
    {
        var catalog = Parse(
            """
            msgid "a\tb\nc \"quoted\" d\\e"
            msgstr "ok"
            """);

        Assert.Equal("ok", catalog.Find("a\tb\nc \"quoted\" d\\e"));
    }

    [Fact]
    public void AllThreeRussianFormsAreRead()
    {
        var catalog = Parse(
            """
            msgid "{0} action"
            msgid_plural "{0} actions"
            msgstr[0] "{0} действие"
            msgstr[1] "{0} действия"
            msgstr[2] "{0} действий"
            """);

        Assert.Equal("{0} действие", catalog.FindPlural("{0} action", 0));
        Assert.Equal("{0} действия", catalog.FindPlural("{0} action", 1));
        Assert.Equal("{0} действий", catalog.FindPlural("{0} action", 2));
    }

    /// <summary>
    /// A plural message is not also a singular one. Asking the wrong way round
    /// must miss rather than answer the first form, or a count of five would
    /// silently read as one.
    /// </summary>
    [Fact]
    public void APluralIsNotFoundAsASingular()
    {
        var catalog = Parse(
            """
            msgid "{0} action"
            msgid_plural "{0} actions"
            msgstr[0] "{0} действие"
            msgstr[1] "{0} действия"
            msgstr[2] "{0} действий"
            """);

        Assert.Null(catalog.Find("{0} action"));
    }

    /// <summary>
    /// Gettext's own tools withhold a fuzzy entry, and so does this: it marks a
    /// translation somebody has already flagged as a guess, usually one a tool
    /// carried over from a message whose English has since changed.
    /// </summary>
    [Fact]
    public void AFuzzyEntryIsWithheld()
    {
        var catalog = Parse(
            """
            #, fuzzy
            msgid "Redo"
            msgstr "Повторить"

            msgid "Undo"
            msgstr "Отменить"
            """);

        Assert.Null(catalog.Find("Redo"));
        Assert.Equal("Отменить", catalog.Find("Undo"));
    }

    /// <summary>
    /// An untranslated message is left for the caller to fall back on. Reading
    /// it as a translation would show an empty label, which looks like a missing
    /// word rather than like untranslated English.
    /// </summary>
    [Fact]
    public void AnEmptyTranslationIsNotRegistered()
    {
        var catalog = Parse(
            """
            msgid "Undo"
            msgstr ""
            """);

        Assert.Null(catalog.Find("Undo"));
    }

    /// <summary>
    /// A plural half filled in is dropped whole for the same reason, and it is
    /// the likelier accident: a translator working down a Russian message has
    /// three boxes and may leave the last one for later.
    /// </summary>
    [Fact]
    public void APartlyFilledPluralIsDroppedWhole()
    {
        var catalog = Parse(
            """
            msgid "{0} action"
            msgid_plural "{0} actions"
            msgstr[0] "{0} действие"
            msgstr[1] "{0} действия"
            msgstr[2] ""
            """);

        Assert.Null(catalog.FindPlural("{0} action", 0));
        Assert.Null(catalog.FindPlural("{0} action", 1));
    }

    /// <summary>
    /// Asking for a form the translation does not carry misses rather than
    /// throwing — a catalogue whose header claimed two forms where its language
    /// counts three is a hand-edited file, and one message in English beats a
    /// panel that will not draw.
    /// </summary>
    [Fact]
    public void AFormBeyondWhatTheTranslationCarriesMisses()
    {
        var catalog = Parse(
            """
            msgid "{0} action"
            msgid_plural "{0} actions"
            msgstr[0] "{0} действие"
            msgstr[1] "{0} действия"
            """);

        Assert.Equal("{0} действия", catalog.FindPlural("{0} action", 1));
        Assert.Null(catalog.FindPlural("{0} action", 2));
    }

    /// <summary>
    /// Comments carry the source reference and the translator's notes, and one
    /// of them can hold anything at all — including something that looks like a
    /// keyword.
    /// </summary>
    [Fact]
    public void CommentsAreIgnored()
    {
        var catalog = Parse(
            """
            # A translator's note: msgid "not this"
            #. Extracted from the shell.
            #: src/View/ShellView.cs:193
            msgid "Undo"
            msgstr "Отменить"
            """);

        Assert.Equal("Отменить", catalog.Find("Undo"));
        Assert.Null(catalog.Find("not this"));
    }

    /// <summary>
    /// A catalogue edited on Windows, or committed through a checkout that
    /// rewrote its endings.
    /// </summary>
    [Fact]
    public void CarriageReturnsDoNotLeakIntoTheText()
    {
        var catalog = Parse("msgid \"Undo\"\r\nmsgstr \"Отменить\"\r\n");

        Assert.Equal("Отменить", catalog.Find("Undo"));
    }

    /// <summary>
    /// Hand-written catalogues are not reliably spaced, and an entry running
    /// straight into the next must not swallow it.
    /// </summary>
    [Fact]
    public void EntriesNeedNoBlankLineBetweenThem()
    {
        var catalog = Parse(
            """
            msgid "Undo"
            msgstr "Отменить"
            msgid "Redo"
            msgstr "Повторить"
            """);

        Assert.Equal("Отменить", catalog.Find("Undo"));
        Assert.Equal("Повторить", catalog.Find("Redo"));
    }

    /// <summary>
    /// Nothing here writes a context, but a tool may. A contextual entry is a
    /// different message, and must not be mistaken for the plain one.
    /// </summary>
    [Fact]
    public void AContextualEntryDoesNotAnswerForThePlainOne()
    {
        var catalog = Parse(
            """
            msgctxt "menu"
            msgid "Open"
            msgstr "Открыть"
            """);

        Assert.Null(catalog.Find("Open"));
    }

    /// <summary>
    /// A catalogue is data from outside. One damaged line costs its own message
    /// and nothing else.
    /// </summary>
    [Fact]
    public void AMalformedLineCostsOnlyItsOwnEntry()
    {
        var catalog = Parse(
            """
            msgid Undo
            msgstr "Отменить"

            msgid "Redo"
            msgstr "Повторить"
            """);

        Assert.Equal("Повторить", catalog.Find("Redo"));
    }

    [Fact]
    public void AnEmptyFileIsAnEmptyCatalogue()
    {
        var catalog = Parse(string.Empty);

        Assert.Null(catalog.Find("Undo"));
        Assert.Equal(EditorLanguage.Russian, catalog.Language);
    }
}
