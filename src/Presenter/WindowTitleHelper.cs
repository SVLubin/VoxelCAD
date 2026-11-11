using System.IO;

namespace VoxelCAD.Presenter;

/// <summary>Composes the window title. Pure — the view is handed the finished string.</summary>
internal static class WindowTitleHelper
{
    public const string ApplicationName = "VoxelCAD";

    /// <summary>What a document that has never been written to disk is called.</summary>
    public const string UntitledName = "Untitled";

    /// <summary>
    /// <c>part.vcad — VoxelCAD</c>, with a leading bullet while there is anything
    /// to lose. The file name leads because that is what a user scans a taskbar
    /// for; the directory is left out for the same reason.
    /// </summary>
    public static string Compose(string? path, bool isModified)
    {
        var name = string.IsNullOrEmpty(path) ? UntitledName : Path.GetFileName(path);
        var mark = isModified ? "• " : string.Empty;

        return $"{mark}{name} — {ApplicationName}";
    }
}
