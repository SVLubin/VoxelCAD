using System;
using System.IO;

namespace VoxelCAD.Preferences;

/// <summary>
/// Both files this layer writes carry the endpoint's bearer token, so neither is
/// anyone else's business on a shared machine.
/// </summary>
internal static class FilePermissions
{
    public static void RestrictToOwner(string path)
    {
        // Windows has no mode bits to set, and its per-user profile directory
        // already carries the equivalent ACL.
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            // A file system that cannot express this — a mounted share, say — is
            // not a reason to fail the write that has already happened.
        }
    }
}
