using System;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The editor is never left in a tool that has nothing to act on. That state is
/// silent — no preview, no click answered, and nothing saying why — so it reads as
/// selection having broken, which is exactly how it was reported.
/// </summary>
/// <remarks>
/// In practice that is the tools aiming at cells, and not because the transform
/// tools were carved out: a tool whose click picks what is under it is never over
/// nothing, because the click is what finds the something. The two rules are read
/// together — see <c>SelectingTools</c>.
/// </remarks>
public sealed class ToolFallbackTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    public ToolFallbackTests() => _shell = new ShellPresenter(_view, new FakePreferences());

    public void Dispose() => _shell.Dispose();

    /// <summary>
    /// The way in that was reported: a tool is active, the selection goes away —
    /// here by clicking a material row — and the viewport stops answering.
    /// </summary>
    [Fact]
    public void SelectingAMaterialDropsACellTool()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Paint);

        _view.HierarchyView.RaiseItemClicked(FirstMaterial);

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
    }

    /// <summary>
    /// And the other half of the same rule: the handles have nothing to hang on
    /// either, but a click under Move picks the next thing to move, so the tool is
    /// still worth being in.
    /// </summary>
    [Fact]
    public void SelectingAMaterialKeepsATransformTool()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Move);

        _view.HierarchyView.RaiseItemClicked(FirstMaterial);

        Assert.Equal(ToolKind.Move, _view.SceneView.Tool);
    }

    [Fact]
    public void SelectingASceneRowDropsACellTool()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Extrude);

        _view.HierarchyView.RaiseItemClicked(
            new SelectionTarget(SelectionKind.Scene, _view.HierarchyView.SceneId));

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
    }

    /// <summary>
    /// The selection goes on naming an element the document no longer has, so the
    /// tool has to be resolved against the document rather than against the
    /// selection alone.
    /// </summary>
    [Fact]
    public void DeletingWhatWasSelectedDropsACellTool()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Fill);

        _view.RaiseDeleteSelection();

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
    }

    [Fact]
    public void DeletingWhatWasSelectedKeepsATransformTool()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Rotate);

        _view.RaiseDeleteSelection();

        Assert.Equal(ToolKind.Rotate, _view.SceneView.Tool);
    }

    /// <summary>A group has no cells, and the brush aims at cells.</summary>
    [Fact]
    public void AGroupDropsACellTool()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Paint);

        _view.RaiseCreateGroup();

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
    }

    /// <summary>A group has a transform, which is what the handles take hold of.</summary>
    [Fact]
    public void AGroupKeepsATransformTool()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Move);

        _view.RaiseCreateGroup();

        Assert.Equal(ToolKind.Move, _view.SceneView.Tool);
    }

    /// <summary>
    /// Moving from one model to another is not a reason to leave the tool: the new
    /// one is something it can act on.
    /// </summary>
    [Fact]
    public void AnotherModelKeepsTheTool()
    {
        _view.RaiseCreateVoxelModel();
        SelectionTarget first = _view.HierarchyView.Selected;
        _view.RaiseCreateVoxelModel();

        _view.SceneView.RaiseToolRequested(ToolKind.Paint);
        _view.HierarchyView.RaiseItemClicked(first);

        Assert.Equal(ToolKind.Paint, _view.SceneView.Tool);
    }

    /// <summary>
    /// The other way into the same dead state: reaching for the brush before
    /// picking anything to paint on. The editor stays on the cursor, which is the
    /// tool that can then find something.
    /// </summary>
    [Fact]
    public void ACellToolAskedForWithNothingSelectedStaysOnTheCursor()
    {
        _view.SceneView.RaiseToolRequested(ToolKind.Paint);

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
    }

    /// <summary>
    /// Where a transform tool stands, because reaching for it first and then
    /// clicking the part to move is a way of working rather than a dead end.
    /// </summary>
    [Fact]
    public void ATransformToolAskedForWithNothingSelectedStands()
    {
        _view.SceneView.RaiseToolRequested(ToolKind.Move);

        Assert.Equal(ToolKind.Move, _view.SceneView.Tool);
    }

    /// <summary>
    /// The keyboard is still handed over, because the request was a real one: what
    /// the fallback refuses is the tool, not the gesture that asked for it.
    /// </summary>
    [Fact]
    public void AskingForItStillMovesTheKeyboard()
    {
        int before = _view.SceneView.KeyboardCount;
        _view.SceneView.RaiseToolRequested(ToolKind.Paint);

        Assert.Equal(before + 1, _view.SceneView.KeyboardCount);
    }

    /// <summary>
    /// The fallback itself must not move the keyboard: nobody asked for the cursor,
    /// and the user whose selection just went away is working in another panel.
    /// </summary>
    [Fact]
    public void TheFallbackLeavesTheKeyboardWhereItIs()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Paint);

        int before = _view.SceneView.KeyboardCount;
        _view.HierarchyView.RaiseItemClicked(FirstMaterial);

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
        Assert.Equal(before, _view.SceneView.KeyboardCount);
    }

    /// <summary>
    /// Switching scenes drops the selection with the scene-bound presenters, so a
    /// cell tool is left with nothing to paint on. The tool is still session state
    /// rather than the scene's — it is the subject that went away.
    /// </summary>
    [Fact]
    public void SwitchingScenesDropsACellTool()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Paint);

        _view.RaiseCreate(CreateKind.Scene);

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
    }

    /// <summary>
    /// It does not remember: a tool that fell back stays fallen, so the same click
    /// on a model always leaves the editor in the same mode.
    /// </summary>
    [Fact]
    public void SelectingAModelAgainDoesNotBringTheToolBack()
    {
        _view.RaiseCreateVoxelModel();
        SelectionTarget model = _view.HierarchyView.Selected;

        _view.SceneView.RaiseToolRequested(ToolKind.Paint);
        _view.HierarchyView.RaiseItemClicked(FirstMaterial);
        _view.HierarchyView.RaiseItemClicked(model);

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
    }

    /// <summary>
    /// The panel's own key is refused before the request rather than resolved
    /// after it, because a request that reached the session would have taken the
    /// keyboard off the panel first.
    /// </summary>
    [Fact]
    public void ACellToolKeyOnAGroupRowIsIgnored()
    {
        _view.RaiseCreateGroup();

        int before = _view.SceneView.KeyboardCount;
        _view.HierarchyView.RaiseToolRequested(ToolKind.Paint);

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
        Assert.Equal(before, _view.SceneView.KeyboardCount);
    }

    [Fact]
    public void ATransformToolKeyOnAGroupRowIsAnswered()
    {
        _view.RaiseCreateGroup();

        int before = _view.SceneView.KeyboardCount;
        _view.HierarchyView.RaiseToolRequested(ToolKind.Move);

        Assert.Equal(ToolKind.Move, _view.SceneView.Tool);
        Assert.Equal(before + 1, _view.SceneView.KeyboardCount);
    }

    private SelectionTarget FirstMaterial =>
        _view.HierarchyView.ChildrenOf(new SelectionTarget(SelectionKind.MaterialsCategory, Guid.Empty))[0];
}
