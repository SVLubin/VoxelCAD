namespace VoxelCAD.Export;

/// <summary>
/// Text or binary, where a format offers both. Most formats in scope are fixed —
/// OBJ is text, STL and <c>.vox</c> are binary — so this is carried on
/// <see cref="ExportOptions"/> as a nullable override and ignored by a format
/// that has only one encoding.
/// </summary>
public enum ExportEncoding
{
    Ascii,
    Binary
}
