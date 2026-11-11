using System;
using System.Collections.Generic;
using System.Linq;

namespace VoxelCAD.Export;

/// <summary>
/// Every writer there is, keyed by the extension it owns. Registering a format is
/// one line here; nothing else in the assembly changes.
/// </summary>
internal sealed class ExportRegistry
{
    private readonly Dictionary<string, IExportFormat> _formatsByExtensionDictionary;

    public ExportRegistry()
    {
        // The formats, in the order they should appear in a menu or a dialog. Each
        // stage of the export work adds its writer here.
        IReadOnlyList<IExportFormat> formatsList = new IExportFormat[]
        {
            new ObjFormat(),
            new StlFormat(),
            new VoxFormat(),
            new FbxFormat()
        };

        _formatsByExtensionDictionary = formatsList.ToDictionary(
            format => format.Extension,
            StringComparer.OrdinalIgnoreCase);

        Formats = formatsList
            .Select(format => new ExportFormatInfo(format.Extension, format.Label, format.Facet))
            .ToList();
    }

    public IReadOnlyList<ExportFormatInfo> Formats { get; }

    /// <summary>The format that owns an extension, matched case-insensitively.</summary>
    public IExportFormat Resolve(string extension)
    {
        if (_formatsByExtensionDictionary.TryGetValue(extension, out IExportFormat? format))
            return format;

        string known = _formatsByExtensionDictionary.Count == 0
            ? "none are registered yet"
            : string.Join(", ", _formatsByExtensionDictionary.Keys.OrderBy(key => key));

        throw new ArgumentException(
            $"No exporter for extension '{extension}'. Known: {known}.");
    }
}
