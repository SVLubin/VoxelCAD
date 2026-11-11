namespace VoxelCAD.Export;

/// <summary>
/// What an agent or a file dialog needs to know about a format without reaching
/// its writer: the extension, a human label, and which facet it draws from.
/// </summary>
public readonly record struct ExportFormatInfo(string Extension, string Label, ExportFacet Facet);
