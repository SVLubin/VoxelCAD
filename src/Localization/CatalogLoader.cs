using System;
using System.IO;
using System.Reflection;
using System.Text;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Localization;

/// <summary>
/// Finds a language's catalogue among the assembly's embedded resources.
/// </summary>
/// <remarks>
/// The catalogues ship inside this assembly rather than under <c>res://</c>, for
/// two reasons that point the same way. Godot imports a <c>.po</c> into a
/// <c>.translation</c> resource of its own, which is machinery this layer does
/// not use and would have to be told to skip; and a test that reads an embedded
/// resource is reading the very bytes the editor will, where one that reads a
/// path is reading whatever happens to be beside it.
/// </remarks>
public static class CatalogLoader
{
    /// <summary>
    /// The template a translator starts a new catalogue from: every message in
    /// the editor, with its translations left empty.
    /// </summary>
    public const string TemplateResourceName = "voxelcad.pot";

    /// <summary>
    /// The catalogue for <paramref name="language"/>, or an empty one when the
    /// assembly carries none.
    /// </summary>
    /// <remarks>
    /// <see cref="EditorLanguage.English"/> has no catalogue and needs none: it
    /// is the language the messages are keyed by, so every lookup missing is
    /// exactly right. A missing catalogue for any other language is a build that
    /// did not embed it — the editor runs in English rather than refusing to
    /// start, which is the same bargain every other reader of outside data makes
    /// here.
    /// </remarks>
    public static TranslationCatalog Load(EditorLanguage language)
    {
        if (language == EditorLanguage.English)
            return TranslationCatalog.CreateEmpty(language);

        var text = ReadResource($"{LanguageCodes.ToCode(language)}.po");

        return text is null
            ? TranslationCatalog.CreateEmpty(language)
            : PoParser.Parse(language, text);
    }

    /// <summary>
    /// The template's text, or null when the assembly carries none.
    /// </summary>
    public static string? ReadTemplate() => ReadResource(TemplateResourceName);

    /// <summary>
    /// Reads the embedded resource whose name ends in <paramref name="fileName"/>.
    /// </summary>
    /// <remarks>
    /// Matched by suffix rather than composed, because MSBuild derives a logical
    /// name from the root namespace and the path and is entitled to mangle what
    /// is not an identifier — <c>pt-BR</c> carries a hyphen, and a name written
    /// down here would be a guess about that.
    /// </remarks>
    private static string? ReadResource(string fileName)
    {
        var assembly = typeof(CatalogLoader).Assembly;
        var name = FindResourceName(assembly, fileName);

        if (name is null)
            return null;

        using var stream = assembly.GetManifestResourceStream(name);

        if (stream is null)
            return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    private static string? FindResourceName(Assembly assembly, string fileName)
    {
        var suffix = "." + fileName;

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                || name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return null;
    }
}
