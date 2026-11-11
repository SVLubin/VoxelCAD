using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Putting somebody else's file in the scene: dropped on the window, or asked for
/// through a picker. Where it lands and what it is called are the shell's, and
/// they are the same answers whichever route was taken.
/// </summary>
public sealed class ReferenceImportTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"voxelcad-{Guid.NewGuid():N}");

    public ReferenceImportTests()
    {
        _shell = new ShellPresenter(_view, new FakePreferences());

        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        _shell.Dispose();

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ADroppedPictureLandsInTheSceneNamedAfterItsFile()
    {
        _view.RaiseFilesDropped(Picture("hull-side.png"));

        Assert.Equal(
            new[] { "hull-side" },
            Children(Scene).Select(_view.HierarchyView.NameOf));
    }

    /// <summary>
    /// Each reference becomes the selection as it lands, so a parent re-read
    /// between drops would nest the second file inside the first.
    /// </summary>
    [Fact]
    public void SeveralDroppedFilesLandBesideEachOtherRatherThanInsideEachOther()
    {
        _view.RaiseFilesDropped(Picture("front.png"), Picture("side.png"), Touch("bust.glb"));

        Assert.Equal(
            new[] { "front", "side", "bust" },
            Children(Scene).Select(_view.HierarchyView.NameOf));
    }

    /// <summary>
    /// Under whatever is selected, like every other create.
    /// </summary>
    [Fact]
    public void ADropLandsUnderWhateverIsSelected()
    {
        _view.RaiseCreateGroup();

        SelectionTarget group = _view.HierarchyView.Selected;

        _view.RaiseFilesDropped(Picture("plan.png"));

        Assert.Equal(new[] { "plan" }, Children(group).Select(_view.HierarchyView.NameOf));
    }

    /// <summary>
    /// Somebody meant something by dragging it here, so what cannot be used is
    /// named rather than silently dropped — and what can is still placed.
    /// </summary>
    [Fact]
    public void AFileNothingCouldStandForIsNamedAndTheRestStillLand()
    {
        _view.RaiseFilesDropped(Picture("plan.png"), Touch("notes.txt"));

        Assert.Equal(new[] { "plan" }, Children(Scene).Select(_view.HierarchyView.NameOf));

        (string Title, string Message) error = Assert.Single(_view.Errors);

        Assert.Contains("notes.txt", error.Message);
        Assert.Contains(".png", error.Message);
    }

    [Fact]
    public void ADroppedReferenceUndoesInOneStep()
    {
        _view.RaiseFilesDropped(Picture("plan.png"));

        Assert.Single(Children(Scene));

        _view.RaiseUndo();

        Assert.Empty(Children(Scene));
    }

    /// <summary>
    /// The button reports and is told what it became, rather than acting on itself:
    /// the same switch is on the command surface, and a button that decided its own
    /// state would be able to disagree with the editor about which way it stands.
    /// </summary>
    [Fact]
    public void TheWholeSceneSwitchGoesThereAndComesBack()
    {
        _view.RaiseFilesDropped(Picture("plan.png"));

        Assert.True(_view.SceneView.ReferencesVisible);

        _view.RaiseReferencesVisible(false);

        Assert.False(_view.SceneView.ReferencesVisible);
        Assert.False(_view.ReferencesVisible);

        _view.RaiseReferencesVisible(true);

        Assert.True(_view.SceneView.ReferencesVisible);
        Assert.True(_view.ReferencesVisible);
    }

    /// <summary>
    /// It is for looking at the model clean for a moment: no record, no file, and
    /// no element's own eye touched.
    /// </summary>
    [Fact]
    public void TheSwitchIsNotTheDocument()
    {
        _view.RaiseFilesDropped(Picture("plan.png"));

        SelectionTarget reference = _view.HierarchyView.Selected;
        int records = _view.HistoryView.Rows.Count;

        _view.RaiseReferencesVisible(false);

        Assert.Equal(records, _view.HistoryView.Rows.Count);
        Assert.Equal(RowVisibility.Shown, _view.HierarchyView.VisibilityOf(reference));
    }

    /// <summary>
    /// The create is offered wherever an element can be made — it is one, and the
    /// only thing that makes it different is that it names a file.
    /// </summary>
    [Fact]
    public void TheImportIsOfferedWhereverAnElementCanBeMade() =>
        Assert.True(_view.IsCreateEnabled(CreateKind.Reference));

    private SelectionTarget Scene => new(SelectionKind.Scene, _view.HierarchyView.SceneId);

    private IReadOnlyList<SelectionTarget> Children(SelectionTarget parent) =>
        _view.HierarchyView.ChildrenOf(parent);

    private string Picture(string name)
    {
        string path = Path.Combine(_directory, name);
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        bytes.AddRange(new byte[] { 0, 0, 0, 13 });
        bytes.AddRange(new byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' });
        bytes.AddRange(new byte[] { 0, 0, 0, 32, 0, 0, 0, 16 });
        bytes.AddRange(new byte[] { 8, 6, 0, 0, 0, 0, 0, 0, 0 });

        File.WriteAllBytes(path, bytes.ToArray());

        return path;
    }

    private string Touch(string name)
    {
        string path = Path.Combine(_directory, name);

        File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3 });

        return path;
    }
}
