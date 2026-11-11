using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoxelCAD.Export;

/// <summary>
/// Every reader there is, keyed by the extension it owns. Registering a format is
/// one line here; nothing else in the assembly changes.
/// </summary>
internal sealed class ImportRegistry
{
    private readonly Dictionary<string, IImportFormat> _formatsByExtensionDictionary;

    public ImportRegistry()
    {
        // The formats, in the order they should appear in a menu or a dialog.
        IReadOnlyList<IImportFormat> formatsList = new IImportFormat[]
        {
            new VoxImportFormat(),
            new QbImportFormat()
        };

        _formatsByExtensionDictionary = formatsList.ToDictionary(
            format => format.Extension,
            StringComparer.OrdinalIgnoreCase);

        Formats = formatsList
            .Select(format => new ImportFormatInfo(format.Extension, format.Label))
            .ToList();
    }

    public IReadOnlyList<ImportFormatInfo> Formats { get; }

    /// <summary>Whether an extension has a reader, matched case-insensitively.</summary>
    public bool Knows(string path) =>
        _formatsByExtensionDictionary.ContainsKey(Path.GetExtension(path));

    /// <summary>The reader that owns an extension, matched case-insensitively.</summary>
    public IImportFormat Resolve(string extension)
    {
        if (_formatsByExtensionDictionary.TryGetValue(extension, out IImportFormat? format))
            return format;

        string known = string.Join(", ", _formatsByExtensionDictionary.Keys.OrderBy(key => key));

        throw new ArgumentException(
            $"No importer for extension '{extension}'. Known: {known}.");
    }
}
