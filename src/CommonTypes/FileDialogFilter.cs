namespace VoxelCAD.CommonTypes;

/// <summary>
/// One filter a file dialog offers: a glob such as <c>*.obj</c> and what to call
/// it. A save dialog may present several — one per export format — and the chosen
/// one settles the extension.
/// </summary>
public readonly record struct FileDialogFilter(string Glob, string Label);
