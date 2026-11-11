using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// File → Import… and Open on somebody else's voxel file, as the user reaches
/// them.
/// </summary>
/// <remarks>
/// What the reading does has its own tests one layer down; what is here is the
/// shell's half — which formats a picker offers, that a cancel leaves the
/// document alone, and that a refusal is shown rather than thrown.
/// </remarks>
public sealed class ImportFlowTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "voxelcad-import-flow-" + Path.GetRandomFileName());

    public ImportFlowTests()
    {
        _shell = new ShellPresenter(_view, new FakePreferences());
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        _shell.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void TheImportPickerOffersEveryFormatTheEditorReads()
    {
        AnswerPath(null);

        _view.RaiseImport();

        FileDialogRequest request = Assert.NotNull(_view.LastPathRequest);

        Assert.Equal(FileDialogMode.Open, request.Mode);
        Assert.Equal(new[] { "*.vox", "*.qb" }, request.Filters.Select(filter => filter.Glob));
    }

    /// <summary>
    /// The gesture somebody arriving from MagicaVoxel reaches for first is Open,
    /// not Import — so the voxel formats have to be on that picker too, beside
    /// this editor's own.
    /// </summary>
    [Fact]
    public void TheOpenPickerOffersTheProjectFormatAndTheVoxelOnes()
    {
        AnswerPath(null);

        _view.RaiseOpen();

        FileDialogRequest request = Assert.NotNull(_view.LastPathRequest);

        Assert.Equal(
            new[] { "*.vcad", "*.vox", "*.qb" },
            request.Filters.Select(filter => filter.Glob));
    }

    [Fact]
    public void CancellingTheImportPickerChangesNothing()
    {
        AnswerPath(null);

        _view.RaiseImport();

        Assert.Empty(_view.Errors);
        Assert.False(IsModified());
    }

    [Fact]
    public void AnImportedFileArrivesInTheOpenDocumentRatherThanReplacingIt()
    {
        string path = Write("part.vox", OneVoxel());

        _view.RaiseCreateGroup();

        Guid scene = _view.HierarchyView.SceneId;

        AnswerPath(path);
        _view.RaiseImport();

        Assert.Empty(_view.Errors);

        // The same scene, with something new in it: an import is an edit, not an
        // open. Nothing was asked about discarding, because nothing was discarded.
        Assert.Equal(scene, _view.HierarchyView.SceneId);
        Assert.Equal(0, _view.ConfirmCount);
        Assert.True(IsModified());
    }

    [Fact]
    public void ADamagedFileIsReportedRatherThanThrown()
    {
        string path = Write("broken.vox", new byte[] { 1, 2, 3, 4 });

        AnswerPath(path);
        _view.RaiseImport();

        (string title, string message) = Assert.Single(_view.Errors);

        Assert.Equal("Cannot import", title);
        Assert.Contains("cannot be read", message);
    }

    [Fact]
    public void OpeningAVoxelFileReplacesTheDocumentAndLeavesItUntitled()
    {
        string path = Write("part.vox", OneVoxel());

        AnswerPath(path);
        _view.RaiseOpen();

        Assert.Empty(_view.Errors);

        // Untitled and modified: the path names somebody else's file in somebody
        // else's format, so Save has to ask where rather than writing over it.
        Assert.Equal("• Untitled — VoxelCAD", _view.WindowTitle);
    }

    /// <summary>
    /// The bullet is not decoration. A document opened from somebody else's file
    /// holds work that nothing on disk matches, so closing it has to ask — which
    /// is the whole reason it is born modified rather than merely untitled.
    /// </summary>
    [Fact]
    public void ClosingADocumentOpenedFromAVoxelFileAsksFirst()
    {
        string path = Write("part.vox", OneVoxel());

        AnswerPath(path);
        _view.RaiseOpen();

        _view.OnAskConfirm = _ => Task.FromResult(ConfirmResult.Cancel);
        _view.RaiseClose();

        Assert.Equal(1, _view.ConfirmCount);
        Assert.False(_view.Closed);
    }

    /// <summary>The smallest legal file: one cell, one colour, no scene graph.</summary>
    private static byte[] OneVoxel()
    {
        using var children = new MemoryStream();
        using var childWriter = new BinaryWriter(children);

        WriteChunk(childWriter, "SIZE", Bytes(w =>
        {
            w.Write(1);
            w.Write(1);
            w.Write(1);
        }));
        WriteChunk(childWriter, "XYZI", Bytes(w =>
        {
            w.Write(1);
            w.Write(new byte[] { 0, 0, 0, 1 });
        }));

        var palette = new byte[256 * 4];

        palette[0] = 200;
        palette[3] = 255;

        WriteChunk(childWriter, "RGBA", palette);
        childWriter.Flush();

        byte[] childBytes = children.ToArray();

        return Bytes(writer =>
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("VOX "));
            writer.Write(150);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("MAIN"));
            writer.Write(0);
            writer.Write(childBytes.Length);
            writer.Write(childBytes);
        });
    }

    private static void WriteChunk(BinaryWriter writer, string id, byte[] content)
    {
        writer.Write(System.Text.Encoding.ASCII.GetBytes(id));
        writer.Write(content.Length);
        writer.Write(0);
        writer.Write(content);
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        write(writer);
        writer.Flush();

        return buffer.ToArray();
    }

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_directory, name);

        File.WriteAllBytes(path, bytes);

        return path;
    }

    private void AnswerPath(string? path) => _view.OnAskPath = _ => Task.FromResult(path);

    private bool IsModified() => _view.WindowTitle.StartsWith("•", StringComparison.Ordinal);
}
