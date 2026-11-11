using System;
using VoxelCAD.CommonTypes;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The viewport's tools are exclusive session state, not a viewport mode: a key
/// reports an intention, the session records it, and the viewport is told what to
/// draw. Which is what lets the selection and the tool arrive from either side and
/// still agree.
/// </summary>
public sealed class ToolModeTests : IDisposable
{
    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    public ToolModeTests() => _shell = new ShellPresenter(_view, new FakePreferences());

    public void Dispose() => _shell.Dispose();

    /// <summary>
    /// The cursor is what a fresh window is holding, and the viewport hears it the
    /// same way it hears every other tool — the session says so and the panel is
    /// told, rather than the viewport starting in a mode of its own.
    /// </summary>
    [Fact]
    public void TheCursorIsWhatTheViewportStartsIn()
    {
        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
    }

    /// <summary>
    /// Over an element, because a tool with nothing to act on falls back to the
    /// cursor rather than being held over nothing — see <c>ToolFallbackTests</c>.
    /// </summary>
    [Fact]
    public void ATitleKeyChangesTheTool()
    {
        _view.RaiseCreateVoxelModel();

        _view.SceneView.RaiseToolRequested(ToolKind.Move);

        Assert.Equal(ToolKind.Move, _view.SceneView.Tool);
    }

    /// <summary>
    /// The round trip is the point: the key does not set the viewport, it sets the
    /// session, and the viewport hears about it the way anything else would.
    /// </summary>
    [Fact]
    public void TheToolIsExclusive()
    {
        _view.RaiseCreateVoxelModel();

        _view.SceneView.RaiseToolRequested(ToolKind.Rotate);
        _view.SceneView.RaiseToolRequested(ToolKind.Scale);

        Assert.Equal(ToolKind.Scale, _view.SceneView.Tool);
    }

    /// <summary>
    /// Leaving a mode is entering one — the cursor is a tool like the others, so
    /// the exclusivity that switches between brush and gizmo is what switches back
    /// to pointing at things, with no second mechanism for "no tool".
    /// </summary>
    [Fact]
    public void TheCursorIsWhatLeavingAToolMeans()
    {
        _view.RaiseCreateVoxelModel();

        _view.SceneView.RaiseToolRequested(ToolKind.Rotate);
        _view.SceneView.RaiseToolRequested(ToolKind.Select);

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
    }

    /// <summary>
    /// The one tool key a row without a transform still answers: the cursor takes
    /// hold of nothing, so there is nothing for it to need selected, and leaving a
    /// mode must work from wherever the keyboard happens to be.
    /// </summary>
    [Fact]
    public void TheCursorKeyAnswersOnARowNoOtherToolWould()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Rotate);
        _view.HierarchyView.RaiseItemClicked(FirstMaterial);

        int before = _view.SceneView.KeyboardCount;
        _view.HierarchyView.RaiseToolRequested(ToolKind.Select);

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
        Assert.Equal(before + 1, _view.SceneView.KeyboardCount);
    }

    /// <summary>
    /// The tool is not selection state: picking something else the tool can act on
    /// leaves it exactly where it was. What it does not survive is a selection with
    /// nothing in it for the tool to take hold of — <c>ToolFallbackTests</c>.
    /// </summary>
    [Fact]
    public void TheToolSurvivesSelectingSomethingElse()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Move);

        _view.RaiseCreateGroup();

        Assert.Equal(ToolKind.Move, _view.SceneView.Tool);
    }

    [Fact]
    public void SelectingAnElementGivesItTheHandlesAndTheBox()
    {
        _view.RaiseCreateVoxelModel();

        Assert.Equal(_view.HierarchyView.Selected.Id, _view.SceneView.HighlightTarget);
    }

    /// <summary>A group has no voxels; the box is the viewport's business, the target is not.</summary>
    [Fact]
    public void AGroupIsHighlightedToo()
    {
        _view.RaiseCreateGroup();

        Assert.Equal(_view.HierarchyView.Selected.Id, _view.SceneView.HighlightTarget);
    }

    [Fact]
    public void SelectingASceneClearsThem()
    {
        _view.RaiseCreateVoxelModel();
        _view.HierarchyView.RaiseItemClicked(
            new SelectionTarget(SelectionKind.Scene, _view.HierarchyView.SceneId));

        Assert.Null(_view.SceneView.HighlightTarget);
    }

    [Fact]
    public void SelectingAMaterialClearsThem()
    {
        _view.RaiseCreateVoxelModel();
        _view.HierarchyView.RaiseItemClicked(FirstMaterial);

        Assert.Null(_view.SceneView.HighlightTarget);
    }

    /// <summary>
    /// Deleting what the handles were on leaves them on nothing — an element the
    /// document no longer has is not something to keep drawing a box around.
    /// </summary>
    [Fact]
    public void DeletingTheTargetClearsTheHighlight()
    {
        _view.RaiseCreateVoxelModel();
        _view.RaiseDeleteSelection();

        Assert.Null(_view.SceneView.HighlightTarget);
    }

    /// <summary>
    /// Undoing the delete brings the element back, but not the selection — the
    /// shell drops that with the element. The highlight follows the selection
    /// rather than the element, so it stays off until something is picked again.
    /// </summary>
    [Fact]
    public void UndoingTheDeleteDoesNotReselectIt()
    {
        _view.RaiseCreateVoxelModel();
        _view.RaiseDeleteSelection();

        _view.RaiseUndo();

        Assert.Null(_view.SceneView.HighlightTarget);
    }

    /// <summary>
    /// Switching scenes throws the scene-bound presenters away, selection included.
    /// The tool is not one of them: it belongs to the session, like the current
    /// material — and a transform tool survives an empty selection because its own
    /// click is how the next thing to move is found. A cell tool does not; that is
    /// <c>ToolFallbackTests</c>.
    /// </summary>
    [Fact]
    public void SwitchingScenesClearsTheTargetButKeepsTheTool()
    {
        _view.RaiseCreateVoxelModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Rotate);

        _view.RaiseCreate(CreateKind.Scene);

        Assert.Null(_view.SceneView.HighlightTarget);
        Assert.Equal(ToolKind.Rotate, _view.SceneView.Tool);
    }

    /// <summary>
    /// The panel's own tool key: a row is selected there and the tool is about to
    /// act on it, so the key switches the tool and the viewport takes the keyboard
    /// the tool is driven by.
    /// </summary>
    [Fact]
    public void AToolKeyOnASelectedElementSwitchesTheToolAndTheKeyboard()
    {
        _view.RaiseCreateVoxelModel();

        int before = _view.SceneView.KeyboardCount;
        _view.HierarchyView.RaiseToolRequested(ToolKind.Rotate);

        Assert.Equal(ToolKind.Rotate, _view.SceneView.Tool);
        Assert.Equal(before + 1, _view.SceneView.KeyboardCount);
    }

    /// <summary>
    /// Asking for the tool already active still moves the keyboard — that case is
    /// the whole reason activation is a request rather than a change.
    /// </summary>
    [Fact]
    public void AskingForTheActiveToolStillMovesTheKeyboard()
    {
        _view.RaiseCreateVoxelModel();
        _view.HierarchyView.RaiseToolRequested(ToolKind.Move);

        int before = _view.SceneView.KeyboardCount;
        _view.HierarchyView.RaiseToolRequested(ToolKind.Move);

        Assert.Equal(ToolKind.Move, _view.SceneView.Tool);
        Assert.Equal(before + 1, _view.SceneView.KeyboardCount);
    }

    /// <summary>
    /// A material row has nothing for the brush to paint on, so the key means
    /// nothing there — and above all does not take the keyboard away from the panel
    /// being browsed.
    /// </summary>
    [Fact]
    public void ACellToolKeyOnARowWithNoModelIsIgnored()
    {
        _view.RaiseCreateVoxelModel();
        _view.HierarchyView.RaiseItemClicked(FirstMaterial);

        int before = _view.SceneView.KeyboardCount;
        _view.HierarchyView.RaiseToolRequested(ToolKind.Paint);

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
        Assert.Equal(before, _view.SceneView.KeyboardCount);
    }

    [Fact]
    public void ACellToolKeyOnASceneRowIsIgnored()
    {
        _view.RaiseCreateVoxelModel();
        _view.HierarchyView.RaiseItemClicked(
            new SelectionTarget(SelectionKind.Scene, _view.HierarchyView.SceneId));

        int before = _view.SceneView.KeyboardCount;
        _view.HierarchyView.RaiseToolRequested(ToolKind.Extrude);

        Assert.Equal(ToolKind.Select, _view.SceneView.Tool);
        Assert.Equal(before, _view.SceneView.KeyboardCount);
    }

    /// <summary>
    /// A transform key is answered on any row, because the tool it asks for finds
    /// its own subject: the keyboard goes to the viewport, and the click that lands
    /// there picks what to move. The same reason the cursor's key answers anywhere.
    /// </summary>
    [Fact]
    public void ATransformToolKeyOnASceneRowIsAnswered()
    {
        _view.RaiseCreateVoxelModel();
        _view.HierarchyView.RaiseItemClicked(
            new SelectionTarget(SelectionKind.Scene, _view.HierarchyView.SceneId));

        int before = _view.SceneView.KeyboardCount;
        _view.HierarchyView.RaiseToolRequested(ToolKind.Scale);

        Assert.Equal(ToolKind.Scale, _view.SceneView.Tool);
        Assert.Equal(before + 1, _view.SceneView.KeyboardCount);
    }

    /// <summary>
    /// The viewport's own key goes through the same request, so the keyboard is
    /// asked for there too — where it is already, and so a no-op.
    /// </summary>
    [Fact]
    public void TheViewportsOwnKeyAsksForTheKeyboardAsWell()
    {
        int before = _view.SceneView.KeyboardCount;
        _view.SceneView.RaiseToolRequested(ToolKind.Move);

        Assert.Equal(before + 1, _view.SceneView.KeyboardCount);
    }

    /// <summary>
    /// The inspector's strip appears for an element the tools act on, already
    /// showing the tool the editor is in — the strip mirrors the session, it
    /// does not keep a state of its own.
    /// </summary>
    [Fact]
    public void SelectingAVoxelModelShowsTheToolStrip()
    {
        _view.RaiseCreateVoxelModel();

        Assert.Equal(ToolKind.Select, _view.InspectorView.Tools);
    }

    [Fact]
    public void AGroupGetsNoToolStrip()
    {
        _view.RaiseCreateGroup();

        Assert.Null(_view.InspectorView.Tools);
    }

    /// <summary>
    /// A strip button is the tool key wearing a face: the same request, through
    /// the same session, and the keyboard moves to the viewport where the tool
    /// acts — then the strip hears the change back and shows it.
    /// </summary>
    [Fact]
    public void AStripButtonSwitchesTheToolAndTheKeyboard()
    {
        _view.RaiseCreateVoxelModel();

        int before = _view.SceneView.KeyboardCount;
        _view.InspectorView.RaiseToolSelected(ToolKind.Move);

        Assert.Equal(ToolKind.Move, _view.SceneView.Tool);
        Assert.Equal(ToolKind.Move, _view.InspectorView.Tools);
        Assert.Equal(before + 1, _view.SceneView.KeyboardCount);
    }

    /// <summary>Exclusivity is the session's: a key elsewhere moves the strip too.</summary>
    [Fact]
    public void TheStripFollowsAToolKeyPressedElsewhere()
    {
        _view.RaiseCreateVoxelModel();

        _view.SceneView.RaiseToolRequested(ToolKind.Rotate);

        Assert.Equal(ToolKind.Rotate, _view.InspectorView.Tools);
    }

    private SelectionTarget FirstMaterial =>
        _view.HierarchyView.ChildrenOf(new SelectionTarget(SelectionKind.MaterialsCategory, Guid.Empty))[0];
}
