using System;
using System.IO;
using VoxelCAD.Commands;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// The path a document is known by is absolute or nothing. A relative one is
/// written down — by a recovery snapshot among others — and a later launch from
/// another directory would resolve it elsewhere.
/// </summary>
public sealed class ProjectPathTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"voxelcad-path-{Guid.NewGuid():N}");

    private readonly string _previousDirectory = Directory.GetCurrentDirectory();

    public ProjectPathTests()
    {
        Directory.CreateDirectory(_directory);
        Directory.SetCurrentDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_previousDirectory);

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ARelativePathIsResolvedAgainstTheWorkingDirectoryWhenItIsSet()
    {
        var context = new CommandContext();
        ICommandDispatcher dispatcher = CommandFactory.CreateDispatcher(context);

        dispatcher.RunOk("new");
        dispatcher.RunOk("save path=part.vcad");

        // Against the directory as the process resolves it — on macOS the temp
        // directory is reached through a symlink, and a full path follows it.
        string expected = Path.Combine(Directory.GetCurrentDirectory(), "part.vcad");

        Assert.Equal(expected, context.Path);
        Assert.True(Path.IsPathRooted(context.Path));
        Assert.True(File.Exists(expected));

        dispatcher.RunOk("new");
        dispatcher.RunOk("open path=part.vcad");

        Assert.Equal(expected, context.Path);
    }

    [Fact]
    public void AnAbsolutePathAndNoPathAreLeftAlone()
    {
        var context = new CommandContext();
        string absolute = Path.Combine(_directory, "elsewhere.vcad");

        context.SetProject(ModelFactory.CreateProject(), absolute);
        Assert.Equal(absolute, context.Path);

        context.SetProject(ModelFactory.CreateProject(), null);
        Assert.Null(context.Path);
    }
}
