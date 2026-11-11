using System;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The viewport reports that framing was asked for; deciding what deserves it is
/// a question about the selection, which the viewport must not be able to answer
/// on its own.
/// </summary>
public sealed class CameraFocusTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    public CameraFocusTests() => _shell = new ShellPresenter(_view, new FakePreferences());

    public void Dispose() => _shell.Dispose();

    [Fact]
    public void FramingWithAnElementSelectedFramesThatElement()
    {
        _view.RaiseCreateVoxelModel();
        SelectionTarget selection = _view.HierarchyView.Selected;

        _view.SceneView.RaiseFocusRequested();

        Assert.Equal(selection.Id, _view.SceneView.FocusTarget);
    }

    /// <summary>
    /// Which also makes the key a way back out of a lost view: the scene frames
    /// the whole buildable region.
    /// </summary>
    [Fact]
    public void FramingWithNothingSelectedFallsBackToTheScene()
    {
        _view.SceneView.RaiseFocusRequested();

        Assert.Equal(_view.HierarchyView.SceneId, _view.SceneView.FocusTarget);
    }

    /// <summary>
    /// A material's id names nothing in the scene, so it is not an element id
    /// that happens to be selected — it is the wrong registry entirely.
    /// </summary>
    [Fact]
    public void FramingWithAMaterialSelectedFallsBackToTheScene()
    {
        _view.RaiseCreate(CreateKind.Material);
        Assert.Equal(SelectionKind.Material, _view.HierarchyView.Selected.Kind);

        _view.SceneView.RaiseFocusRequested();

        Assert.Equal(_view.HierarchyView.SceneId, _view.SceneView.FocusTarget);
    }

    /// <summary>
    /// The viewport presenter is rebuilt when the active scene changes, and a
    /// disposed one still receives the event .NET snapshotted it into.
    /// </summary>
    [Fact]
    public void FramingAfterASceneSwitchFramesTheSceneNowOpen()
    {
        _view.RaiseCreate(CreateKind.Scene);
        Guid activeSceneId = _view.HierarchyView.SceneId;

        _view.SceneView.RaiseFocusRequested();

        Assert.Equal(activeSceneId, _view.SceneView.FocusTarget);
    }
}
