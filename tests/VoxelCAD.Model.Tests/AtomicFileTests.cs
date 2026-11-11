using System;
using System.IO;
using System.Text;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// A file the model writes is the old one or the new one, never half of either.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"voxelcad-atomic-{Guid.NewGuid():N}");

    private readonly string _path;

    public AtomicFileTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "part.vcad");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void AWriteLandsWholeAndLeavesNoWritingFileBehind()
    {
        AtomicFile.WriteAllText(_path, "first");
        AtomicFile.WriteAllText(_path, "second");

        Assert.Equal("second", File.ReadAllText(_path));
        Assert.False(File.Exists(AtomicFile.WritingPathFor(_path)));
        Assert.Single(Directory.GetFileSystemEntries(_directory));
    }

    /// <summary>
    /// A directory planted where the temporary file goes makes the write fail
    /// before a byte reaches the target — which must then still hold what it held.
    /// </summary>
    [Fact]
    public void AFailedWriteLeavesThePreviousFileIntact()
    {
        AtomicFile.WriteAllText(_path, "kept");
        Directory.CreateDirectory(AtomicFile.WritingPathFor(_path));

        Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(_path, "lost"));

        Assert.Equal("kept", File.ReadAllText(_path));
    }

    [Fact]
    public void TheBytesAreExactlyWhatWriteAllTextWrites()
    {
        const string content = "version: 5\nname: Ünïcode — ‘quotes’\n";
        string reference = Path.Combine(_directory, "reference.vcad");

        File.WriteAllText(reference, content);
        AtomicFile.WriteAllText(_path, content);

        Assert.Equal(File.ReadAllBytes(reference), File.ReadAllBytes(_path));
        Assert.NotEqual(Encoding.UTF8.GetPreamble()[0], File.ReadAllBytes(_path)[0]);
    }

    /// <summary>
    /// A rename over a symlink would replace the link with a file and leave the
    /// real one untouched; the link is followed instead.
    /// </summary>
    [Fact]
    public void ASymlinkedTargetKeepsItsLinkAndChangesTheFileBehindIt()
    {
        if (OperatingSystem.IsWindows())
            return;

        string real = Path.Combine(_directory, "real.vcad");
        string link = Path.Combine(_directory, "link.vcad");

        File.WriteAllText(real, "before");
        File.CreateSymbolicLink(link, real);

        AtomicFile.WriteAllText(link, "after");

        Assert.Equal("after", File.ReadAllText(real));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }
}
