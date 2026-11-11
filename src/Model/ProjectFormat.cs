namespace VoxelCAD.Model;

/// <summary>
/// What a project can be stored as, published so a file dialog can offer it.
/// </summary>
/// <remarks>
/// The layers above never learn the format itself — they get a path and hand it
/// back. But a dialog has to name an extension, and the model is the only layer
/// entitled to say which one, so it says it here rather than letting the shell
/// guess.
/// </remarks>
public static class ProjectFormat
{
    /// <summary>The one mode that is implemented; see the tech-debt ledger for the other.</summary>
    public const string Extension = ProjectFileStorage.MonolithicExtension;

    public const string Filter = "*" + Extension;

    public const string Label = "VoxelCAD project";
}
