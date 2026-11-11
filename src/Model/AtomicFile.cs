using System;
using System.IO;
using System.Text;

namespace VoxelCAD.Model;

/// <summary>
/// Writes a text file whole or not at all. The bytes go to a sibling with a
/// <see cref="WritingSuffix"/>, are flushed to disk, and the sibling is renamed
/// over the target — so a crash, a full disk or a killed process in the middle
/// of a write leaves the old file or the new one, never a truncated one.
/// </summary>
/// <remarks>
/// The sibling lives in the target's own directory, which is what makes the
/// rename atomic: a rename across volumes is a copy. A symlinked target is
/// resolved first, so the link keeps pointing where it pointed and the file
/// behind it is what changes. There is deliberately no in-place fallback for a
/// directory that lets a file be rewritten but not created — that would be the
/// truncation risk under a new name, so the error reaches the caller instead.
/// <para>
/// What it costs: the target gets a new inode on every write, so tags and
/// creation dates kept on the old one go with it; and a crash between opening
/// the sibling and renaming it leaves <c>name.writing</c> beside the file, one
/// file's size, until the next write to the same path replaces it.
/// </para>
/// </remarks>
internal static class AtomicFile
{
    public const string WritingSuffix = ".writing";

    /// <summary>UTF-8 without a byte order mark, the bytes <c>File.WriteAllText</c> writes.</summary>
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string content)
    {
        string target = ResolveTarget(path);
        string writing = WritingPathFor(target);

        try
        {
            using (var stream = new FileStream(writing, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(writing, target, overwrite: true);
        }
        catch
        {
            TryDelete(writing);
            throw;
        }
    }

    /// <summary>Where the bytes land first. Public so a test can assert nothing is left behind.</summary>
    public static string WritingPathFor(string path) => path + WritingSuffix;

    /// <summary>
    /// The file a link points at, or the path itself. Only an existing link is
    /// followed; a dangling one is replaced like any other missing file.
    /// </summary>
    private static string ResolveTarget(string path)
    {
        if (!File.Exists(path))
            return path;

        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException)
        {
            return path;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: the failure being reported is the one that matters.
        }
    }
}
