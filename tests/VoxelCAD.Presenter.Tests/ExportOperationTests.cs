using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// File → Export… as the user reaches it: the formats offered are the registry's,
/// and the export itself runs the same command an agent runs. The GUI adds no
/// export logic of its own, so these tests watch the wiring, not a serializer.
/// </summary>
public sealed class ExportOperationTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;
    private readonly string _basePath = TempPath();

    public ExportOperationTests()
    {
        _shell = new ShellPresenter(_view, new FakePreferences());
    }

    public void Dispose()
    {
        _shell.Dispose();

        foreach (string extension in new[] { ".obj", ".mtl", ".stl", ".vox", ".fbx" })
            Delete(Path.ChangeExtension(_basePath, extension));
    }

    [Fact]
    public void ExportOffersEveryRegisteredFormatAsASaveFilter()
    {
        AnswerPath(null);

        _view.RaiseExport();

        FileDialogRequest request = _view.LastPathRequest!.Value;
        IReadOnlyList<string> globs = request.Filters.Select(filter => filter.Glob).ToList();

        Assert.Equal(FileDialogMode.Save, request.Mode);
        Assert.Contains("*.obj", globs);
        Assert.Contains("*.stl", globs);
        Assert.Contains("*.vox", globs);
        Assert.Contains("*.fbx", globs);
    }

    [Fact]
    public void ExportingWritesTheChosenFormatAndItsCompanion()
    {
        string objPath = Path.ChangeExtension(_basePath, ".obj");
        AnswerPath(objPath);
        _view.RaiseCreateVoxelModel();

        _view.RaiseExport();

        Assert.True(File.Exists(objPath));
        Assert.True(File.Exists(Path.ChangeExtension(_basePath, ".mtl")));
        Assert.Empty(_view.Errors);
    }

    [Fact]
    public void CancellingTheExportDialogWritesNothing()
    {
        string objPath = Path.ChangeExtension(_basePath, ".obj");
        AnswerPath(null);

        _view.RaiseExport();

        Assert.False(File.Exists(objPath));
    }

    [Fact]
    public void AnUnknownFormatIsReportedRatherThanCrashing()
    {
        AnswerPath(Path.ChangeExtension(_basePath, ".xyz"));

        _view.RaiseExport();

        (string Title, string Message) error = Assert.Single(_view.Errors);
        Assert.Equal("Cannot export", error.Title);
        Assert.Contains(".xyz", error.Message);
    }

    /// <summary>
    /// The picker can outlive the document it was raised for — an agent's open runs
    /// while it is up — and what comes back must not be exported from the new one.
    /// </summary>
    [Fact]
    public void AnExportPathThatOutlivesItsDocumentIsDropped()
    {
        string objPath = Path.ChangeExtension(_basePath, ".obj");
        var picker = new TaskCompletionSource<string?>();
        _view.OnAskPath = _ => picker.Task;

        _view.RaiseExport();
        TakeControl().Execute("new", new Dictionary<string, string>());
        picker.SetResult(objPath);

        Assert.False(File.Exists(objPath));
    }

    private ICommandDispatcher TakeControl()
    {
        ICommandDispatcher dispatcher = _shell.CreateAgentDispatchers().ForClient("test-client");

        dispatcher.Execute("take-control", new Dictionary<string, string> { ["name"] = "Claude" });
        _shell.Tick(10.0);

        return dispatcher;
    }

    private void AnswerPath(string? path) => _view.OnAskPath = _ => Task.FromResult(path);

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"voxelcad-export-{Guid.NewGuid():N}.vcad");

    private static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
