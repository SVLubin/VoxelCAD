using System;
using System.IO;

namespace VoxelCAD.Model;

/// <summary>
/// Whether the file a reference names is actually there.
/// </summary>
/// <remarks>
/// One question with two readers — the inspector's own row and the command
/// surface's report — so it is one answer rather than two calls that could come to
/// disagree about what an unreadable directory or an empty path means. It costs a
/// look at the disk, and it is the first thing anybody needs: everything else said
/// about a reference describes a picture that may not be on screen at all.
/// </remarks>
public static class ReferenceFile
{
    public static bool Exists(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }
        catch (Exception error) when (error is ArgumentException or IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
