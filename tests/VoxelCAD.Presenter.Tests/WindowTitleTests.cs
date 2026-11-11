using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>What the title bar says, which is the only place unsaved work is announced.</summary>
public sealed class WindowTitleTests
{
    [Fact]
    public void ADocumentWithNoPathIsCalledUntitled()
    {
        Assert.Equal("Untitled — VoxelCAD", WindowTitleHelper.Compose(null, isModified: false));
    }

    [Fact]
    public void OnlyTheFileNameIsShown()
    {
        Assert.Equal(
            "part.vcad — VoxelCAD",
            WindowTitleHelper.Compose("/home/user/models/part.vcad", isModified: false));
    }

    [Fact]
    public void UnsavedWorkIsMarked()
    {
        Assert.StartsWith("• part.vcad", WindowTitleHelper.Compose("part.vcad", isModified: true));
    }
}
