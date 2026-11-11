using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// New, open, save and close as the user reaches them — including the questions
/// asked before anything is lost.
/// </summary>
public sealed class FileOperationTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;
    private readonly string _path = TempPath();
    private readonly string _otherPath = TempPath();

    public FileOperationTests()
    {
        _shell = new ShellPresenter(_view, new FakePreferences());
    }

    public void Dispose()
    {
        _shell.Dispose();

        Delete(_path);
        Delete(_otherPath);
    }

    [Fact]
    public void TheEditorStartsOnAnEmptyUntitledDocument()
    {
        Assert.Equal("Untitled — VoxelCAD", _view.WindowTitle);
        Assert.NotEqual(Guid.Empty, _view.HierarchyView.SceneId);
    }

    [Fact]
    public void AnEditMarksTheTitle()
    {
        _view.RaiseCreateGroup();

        Assert.Equal("• Untitled — VoxelCAD", _view.WindowTitle);
    }

    [Fact]
    public void SavingAnUntitledDocumentAsksWhereToPutIt()
    {
        AnswerPath(_path);
        _view.RaiseCreateGroup();

        _view.RaiseSave();

        Assert.True(File.Exists(_path));
        Assert.Equal(FileDialogMode.Save, _view.LastPathRequest!.Value.Mode);
        Assert.Equal($"{Path.GetFileName(_path)} — VoxelCAD", _view.WindowTitle);
    }

    [Fact]
    public void SavingAgainReusesThePathWithoutAsking()
    {
        var asked = 0;
        _view.OnAskPath = _ =>
        {
            asked++;

            return Task.FromResult<string?>(_path);
        };

        _view.RaiseCreateGroup();
        _view.RaiseSave();

        _view.RaiseCreateVoxelModel();
        _view.RaiseSave();

        Assert.Equal(1, asked);
        Assert.False(IsModified());
    }

    [Fact]
    public void SaveAsAsksEveryTime()
    {
        AnswerPath(_path);
        _view.RaiseSave();

        AnswerPath(_otherPath);
        _view.RaiseSaveAs();

        Assert.True(File.Exists(_otherPath));
        Assert.Contains(Path.GetFileName(_otherPath), _view.WindowTitle);
    }

    /// <summary>
    /// A save dialog can come back with a bare name, and the user meant a project.
    /// </summary>
    [Fact]
    public void ANameWithNoExtensionGetsTheProjectOne()
    {
        AnswerPath(_path[..^ProjectFormat.Extension.Length]);

        _view.RaiseSaveAs();

        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void CancellingTheSaveDialogWritesNothing()
    {
        AnswerPath(null);
        _view.RaiseCreateGroup();

        _view.RaiseSave();

        Assert.False(File.Exists(_path));
        Assert.True(IsModified());
    }

    /// <summary>
    /// The multi-file mode is refused by the model, and the refusal is the user's
    /// to read — the shell does not translate it.
    /// </summary>
    [Fact]
    public void SavingUnderAnUnsupportedExtensionIsReported()
    {
        AnswerPath(Path.ChangeExtension(_path, ".vcadproj"));
        _view.RaiseCreateGroup();

        _view.RaiseSaveAs();

        (string Title, string Message) error = Assert.Single(_view.Errors);
        Assert.Equal("Cannot save the project", error.Title);
        Assert.Contains("multi-file", error.Message);
        Assert.True(IsModified());
    }

    [Fact]
    public void AnUnmodifiedDocumentIsReplacedWithoutAQuestion()
    {
        Guid before = _view.HierarchyView.SceneId;

        _view.RaiseNew();

        Assert.Equal(0, _view.ConfirmCount);
        Assert.NotEqual(before, _view.HierarchyView.SceneId);
    }

    [Fact]
    public void CancellingTheQuestionKeepsTheDocument()
    {
        Guid before = _view.HierarchyView.SceneId;
        _view.RaiseCreateGroup();
        Answer(ConfirmResult.Cancel);

        _view.RaiseNew();

        Assert.Equal(1, _view.ConfirmCount);
        Assert.Equal(before, _view.HierarchyView.SceneId);
        Assert.True(IsModified());
    }

    [Fact]
    public void DiscardingThrowsTheWorkAwayAndStartsOver()
    {
        Guid before = _view.HierarchyView.SceneId;
        _view.RaiseCreateGroup();
        Answer(ConfirmResult.Deny);

        _view.RaiseNew();

        Assert.NotEqual(before, _view.HierarchyView.SceneId);
        Assert.Equal("Untitled — VoxelCAD", _view.WindowTitle);
    }

    [Fact]
    public void AnsweringSaveWritesBeforeStartingOver()
    {
        Guid before = _view.HierarchyView.SceneId;
        _view.RaiseCreateGroup();
        Answer(ConfirmResult.Confirm);
        AnswerPath(_path);

        _view.RaiseNew();

        Assert.True(File.Exists(_path));
        Assert.NotEqual(before, _view.HierarchyView.SceneId);
    }

    /// <summary>
    /// Cancelling the save dialog cancels what the save was for. Otherwise
    /// answering "save" would be a way to lose the work.
    /// </summary>
    [Fact]
    public void CancellingTheSaveThatWasAskedForCancelsTheWholeOperation()
    {
        Guid before = _view.HierarchyView.SceneId;
        _view.RaiseCreateGroup();
        Answer(ConfirmResult.Confirm);
        AnswerPath(null);

        _view.RaiseNew();

        Assert.Equal(before, _view.HierarchyView.SceneId);
    }

    [Fact]
    public void ClosingOnlyQuitsOnceNothingWouldBeLost()
    {
        _view.RaiseCreateGroup();
        Answer(ConfirmResult.Cancel);

        _view.RaiseClose();
        Assert.False(_view.Closed);

        Answer(ConfirmResult.Deny);
        _view.RaiseClose();

        Assert.True(_view.Closed);
    }

    [Fact]
    public void ClosingAnUntouchedDocumentQuitsStraightAway()
    {
        _view.RaiseClose();

        Assert.Equal(0, _view.ConfirmCount);
        Assert.True(_view.Closed);
    }

    [Fact]
    public void UndoingBackToTheSavedPointClearsTheMark()
    {
        AnswerPath(_path);
        _view.RaiseCreateGroup();
        _view.RaiseSave();

        _view.RaiseCreateVoxelModel();
        Assert.True(IsModified());

        _view.RaiseUndo();

        Assert.False(IsModified());
    }

    [Fact]
    public void EditingOverAnUndoneRecordLeavesTheDocumentModified()
    {
        AnswerPath(_path);
        _view.RaiseCreateGroup();
        _view.RaiseSave();

        _view.RaiseUndo();
        _view.RaiseCreateVoxelModel();

        Assert.True(IsModified());
    }

    [Fact]
    public void OpeningWhatIsNotThereIsReportedAndChangesNothing()
    {
        Guid before = _view.HierarchyView.SceneId;
        AnswerPath(Path.Combine(Path.GetTempPath(), $"voxelcad-missing-{Guid.NewGuid():N}.vcad"));

        _view.RaiseOpen();

        Assert.Single(_view.Errors);

        // "Cannot open" rather than "Cannot open the project": the same gesture
        // opens somebody else's voxel file, and calling that a project would be
        // wrong the moment it is one.
        Assert.Equal("Cannot open", _view.Errors[0].Title);
        Assert.Equal(before, _view.HierarchyView.SceneId);
    }

    [Fact]
    public void WhatWasSavedComesBack()
    {
        AnswerPath(_path);
        _view.RaiseCreateGroup();
        _view.RaiseSave();

        _view.RaiseNew();
        Assert.DoesNotContain("Group", _view.HierarchyView.Items.Values);

        AnswerPath(_path);
        _view.RaiseOpen();

        Assert.Contains("Group", _view.HierarchyView.Items.Values);
        Assert.Equal($"{Path.GetFileName(_path)} — VoxelCAD", _view.WindowTitle);
    }

    /// <summary>
    /// The presenter is asynchronous and the document under it is not fixed: an
    /// agent can open a file while the picker is up. What comes back must not be
    /// written over the project that is now open.
    /// </summary>
    [Fact]
    public void APathThatOutlivesItsDocumentIsDropped()
    {
        var picker = new TaskCompletionSource<string?>();
        _view.OnAskPath = _ => picker.Task;

        _view.RaiseCreateGroup();
        _view.RaiseSaveAs();

        TakeControl().Execute("new", new Dictionary<string, string>());

        picker.SetResult(_path);

        Assert.False(File.Exists(_path));
    }

    private ICommandDispatcher TakeControl()
    {
        ICommandDispatcher dispatcher = _shell.CreateAgentDispatchers().ForClient("test-client");

        dispatcher.Execute("take-control", new Dictionary<string, string> { ["name"] = "Claude" });
        _shell.Tick(10.0);

        return dispatcher;
    }

    private void AnswerPath(string? path) => _view.OnAskPath = _ => Task.FromResult(path);

    private void Answer(ConfirmResult result) => _view.OnAskConfirm = _ => Task.FromResult(result);

    private bool IsModified() => _view.WindowTitle.StartsWith("•", StringComparison.Ordinal);

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"voxelcad-{Guid.NewGuid():N}.vcad");

    private static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
