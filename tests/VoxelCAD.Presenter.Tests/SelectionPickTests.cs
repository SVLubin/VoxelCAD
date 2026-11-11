using System;
using VoxelCAD.CommonTypes;
using Xunit;
using Godot;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Clicking an element in the viewport. The view reports which elements the ray
/// crossed; everything below is the decision made about that list — which one the
/// click meant, what a repeat in the same spot means, and what a click that crossed
/// nothing does.
/// </summary>
public sealed class SelectionPickTests : IDisposable
{
    private static readonly Vector2 Spot = new(400f, 300f);

    private readonly FakeShellView _view = new();
    private readonly ShellPresenter _shell;

    public SelectionPickTests()
    {
        _shell = new ShellPresenter(_view, new FakePreferences());
        _view.SceneView.RaiseToolRequested(ToolKind.Select);
    }

    public void Dispose() => _shell.Dispose();

    [Fact]
    public void TheNearestElementIsWhatAClickMeans()
    {
        Guid near = CreateModel();
        Guid far = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, near, far);

        Assert.Equal(new SelectionTarget(SelectionKind.Element, near), _view.HierarchyView.Selected);
    }

    /// <summary>
    /// The selection is written to the session, so the project tree's row, the
    /// selection border and the brush's target all follow through the one channel —
    /// a viewport click and a click on the row are the same event arriving from
    /// opposite sides.
    /// </summary>
    [Fact]
    public void TheHierarchyAndTheViewportAgreeAboutIt()
    {
        Guid model = CreateModel();

        _view.HierarchyView.RaiseItemClicked(
            new SelectionTarget(SelectionKind.Scene, _view.HierarchyView.SceneId));
        _view.SceneView.RaiseElementPicked(Spot, model);

        Assert.Equal(new SelectionTarget(SelectionKind.Element, model), _view.HierarchyView.Selected);
        Assert.Equal(model, _view.SceneView.HighlightTarget);
    }

    [Fact]
    public void ClickingTheSameSpotAgainGoesOneDeeper()
    {
        Guid near = CreateModel();
        Guid far = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, near, far);
        _view.SceneView.RaiseElementPicked(Spot, near, far);

        Assert.Equal(new SelectionTarget(SelectionKind.Element, far), _view.HierarchyView.Selected);
    }

    /// <summary>
    /// And round again rather than sticking at the back: the stack is a ring, so a
    /// user who clicks past what they wanted gets back to it by carrying on.
    /// </summary>
    [Fact]
    public void PastTheLastOneItComesBackToTheFront()
    {
        Guid near = CreateModel();
        Guid far = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, near, far);
        _view.SceneView.RaiseElementPicked(Spot, near, far);
        _view.SceneView.RaiseElementPicked(Spot, near, far);

        Assert.Equal(new SelectionTarget(SelectionKind.Element, near), _view.HierarchyView.Selected);
    }

    [Fact]
    public void AClickSomewhereElseStartsFromTheNearestAgain()
    {
        Guid near = CreateModel();
        Guid far = CreateModel();

        _view.SceneView.RaiseElementPicked(Spot, near, far);
        _view.SceneView.RaiseElementPicked(Spot + new Vector2(80f, 0f), near, far);

        Assert.Equal(new SelectionTarget(SelectionKind.Element, near), _view.HierarchyView.Selected);
    }

    /// <summary>
    /// A click past everything lands on the room, and the room is a row of the
    /// project tree like any other — so the panel says where the user is instead of
    /// going blank.
    /// </summary>
    [Fact]
    public void AClickPastEverythingSelectsTheScene()
    {
        CreateModel();

        _view.SceneView.RaiseElementPicked(Spot);

        Assert.Equal(
            new SelectionTarget(SelectionKind.Scene, _view.HierarchyView.SceneId),
            _view.HierarchyView.Selected);
        Assert.Null(_view.SceneView.HighlightTarget);
    }

    /// <summary>
    /// The cursor is a tool, and a click belongs to the tool that is active — the
    /// same rule that keeps the brush from acting on the eyedropper's click, applied
    /// from the other side.
    /// </summary>
    /// <remarks>
    /// The brush is asked for over a model rather than over nothing, because a tool
    /// with nothing to act on is not a state the editor rests in — see
    /// <c>ToolFallbackPresenter</c>. So the pick names the <em>other</em> model:
    /// were the cursor to answer it, the selection would move there.
    /// </remarks>
    [Fact]
    public void NoOtherToolAnswersTheCursorsClick()
    {
        Guid painted = CreateModel();
        Guid other = CreateModel();

        _view.HierarchyView.RaiseItemClicked(new SelectionTarget(SelectionKind.Element, painted));
        _view.SceneView.RaiseToolRequested(ToolKind.Paint);
        _view.SceneView.RaiseElementPicked(Spot, other);

        Assert.Equal(
            new SelectionTarget(SelectionKind.Element, painted), _view.HierarchyView.Selected);
    }

    /// <summary>
    /// The transform tools select too — a press reaches the presenter only once
    /// the handles have refused it, so a click that missed them picks what is under
    /// it, which is what every editor with a gizmo does.
    /// </summary>
    [Theory]
    [InlineData(ToolKind.Move)]
    [InlineData(ToolKind.Rotate)]
    [InlineData(ToolKind.Scale)]
    public void ATransformToolAnswersTheClickToo(ToolKind tool)
    {
        Guid model = CreateModel();
        Guid other = CreateModel();

        _view.HierarchyView.RaiseItemClicked(new SelectionTarget(SelectionKind.Element, model));
        _view.SceneView.RaiseToolRequested(tool);
        _view.SceneView.RaiseElementPicked(Spot, other);

        Assert.Equal(new SelectionTarget(SelectionKind.Element, other), _view.HierarchyView.Selected);
        Assert.Equal(tool, _view.SceneView.Tool);
    }

    /// <summary>
    /// And a click past everything drops what was held, handles and all — the room
    /// is what the click landed on, under a transform tool exactly as under the
    /// cursor.
    /// </summary>
    [Fact]
    public void AClickPastEverythingUnderATransformToolSelectsTheSceneToo()
    {
        CreateModel();
        _view.SceneView.RaiseToolRequested(ToolKind.Move);

        _view.SceneView.RaiseElementPicked(Spot);

        Assert.Equal(
            new SelectionTarget(SelectionKind.Scene, _view.HierarchyView.SceneId),
            _view.HierarchyView.Selected);
        Assert.Null(_view.SceneView.HighlightTarget);
        Assert.Equal(ToolKind.Move, _view.SceneView.Tool);
    }

    /// <summary>
    /// Selecting is not editing: it writes no record, so there is nothing to undo
    /// and nothing to mark the document dirty with.
    /// </summary>
    [Fact]
    public void SelectingLeavesNoRecordBehind()
    {
        Guid model = CreateModel();
        int records = _view.HistoryView.Labels.Count;

        _view.SceneView.RaiseElementPicked(Spot, model);
        _view.SceneView.RaiseElementPicked(Spot, model);

        Assert.Equal(records, _view.HistoryView.Labels.Count);
    }

    private Guid CreateModel()
    {
        _view.RaiseCreateVoxelModel();

        return _view.HierarchyView.Selected.Id;
    }
}
