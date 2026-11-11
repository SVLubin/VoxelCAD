using System.Collections.Generic;
using System.IO;
using System.Linq;
using VoxelCAD.Commands;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// The <c>export</c> surface as an agent meets it. The writers arrive stage by
/// stage; what is fixed from the start is that export is a read, that it picks a
/// format from the extension, and that a bad request is refused before it can
/// leave a file behind.
/// </summary>
public sealed class ExportCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public ExportCommandTests()
    {
        _dispatcher.RunOk("new");
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
        _dispatcher.RunOk("fill-box element=Model from=0,0,0 to=1,1,1 material=Default");
    }

    [Fact]
    public void BothExportCommandsAreReads()
    {
        Assert.All(
            _dispatcher.Commands.Where(command => command.Name is "export" or "export-formats"),
            command => Assert.False(command.Mutates));
    }

    [Fact]
    public void APathWithoutAnExtensionIsRefused()
    {
        Assert.Contains("no extension", _dispatcher.RunFails("export path=model"));
    }

    [Fact]
    public void AnUnknownFormatNamesTheExtension()
    {
        string error = _dispatcher.RunFails("export path=model.xyz");

        Assert.Contains(".xyz", error);
    }

    [Fact]
    public void AFailedExportLeavesNoFileBehind()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voxelcad-export-{Path.GetRandomFileName()}.xyz");

        _dispatcher.RunFails($"export path={path}");

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ExportFormatsListsWhatIsRegistered()
    {
        var formats = (List<object?>)_dispatcher.RunOk("export-formats").Value!;

        // Every registered format answers with the three fields a menu needs.
        Assert.All(formats, entry =>
        {
            var row = (IReadOnlyDictionary<string, object?>)entry!;
            Assert.True(row.ContainsKey("extension"));
            Assert.True(row.ContainsKey("label"));
            Assert.True(row.ContainsKey("facet"));
        });
    }
}
