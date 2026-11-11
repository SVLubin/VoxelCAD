namespace VoxelCAD.Export;

/// <summary>
/// What an agent or a file dialog needs to know about a readable format without
/// reaching its reader.
/// </summary>
public readonly record struct ImportFormatInfo(string Extension, string Label);
