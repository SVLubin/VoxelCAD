using System;
using System.IO;

namespace VoxelCAD.Model;

/// <summary>
/// Turns the path a reference carries into the path a file stores, and back.
/// </summary>
/// <remarks>
/// <para>
/// In memory a reference's path is absolute, because that is the only form every
/// consumer can use without knowing where the document lives. In the file it is
/// relative whenever the asset sits under the project's own directory, so that a
/// project and its references move together.
/// </para>
/// <para>
/// Only the storage layer knows where the project file itself sits, which is why
/// this is asked there and nowhere else. A path that cannot be expressed relatively
/// — another drive, a directory above the project — is stored as it is, and says so
/// by still being rooted.
/// </para>
/// </remarks>
internal static class ReferencePaths
{
    public static string ToStored(string path, string projectDirectory)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(projectDirectory))
            return path ?? string.Empty;

        if (!Path.IsPathRooted(path))
            return path;

        try
        {
            string relative = Path.GetRelativePath(projectDirectory, path);

            // Rooted means the two share no common directory at all; ".." means
            // the asset is outside the project's own folder, and an absolute path
            // is the honest thing to write for both.
            if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
                return path;

            // Written the same way on every platform, so a project made on Windows
            // resolves on macOS.
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    public static string FromStored(string path, string projectDirectory)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(projectDirectory))
            return path ?? string.Empty;

        if (Path.IsPathRooted(path))
            return path;

        try
        {
            return Path.GetFullPath(
                Path.Combine(projectDirectory, path.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (ArgumentException)
        {
            return path;
        }
    }
}
