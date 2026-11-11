using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// A brush stroke as the presenter files it: every cell the viewport proposed
/// while the button was down lands in one history record, held open on the
/// session for the length of the gesture, and a second stroke straight after is
/// a second record — the rule that has caught per-gesture state three times.
/// </summary>
public sealed class BrushStrokePresenterTests : IDisposable
{
    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _element;
    private DocumentPresenter? _document;

    public BrushStrokePresenterTests()
    {
        _scene = _project.Scenes[0];
        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
    }

    public void Dispose() => _document?.Dispose();

    [Fact]
    public void AStrokeOfEightCellsIsOneRecordAndUndoTakesAllEight()
    {
        Build();
        int before = _project.History.Records.Count;

        _view.RaiseBrushStroke(FloorRun(8));

        Assert.Equal(8, Model().Voxels.Count);
        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.EditVoxels, _project.History.Records[^1].Kind);
        Assert.Equal("Model", _project.History.Records[^1].TargetName);
        Assert.False(_session.HasGesture);

        _project.History.Undo();

        Assert.Empty(Model().Voxels);

        _project.History.Redo();

        Assert.Equal(8, Model().Voxels.Count);
    }

    /// <summary>
    /// The second use. A stroke straight after the first is its own record, and
    /// undoing it leaves the first stroke's cells standing.
    /// </summary>
    [Fact]
    public void ASecondStrokeIsASecondRecord()
    {
        Build();
        int before = _project.History.Records.Count;

        _view.RaiseBrushStroke(FloorRun(3));
        _view.RaiseBrushStroke(FloorRun(3, offset: 5));

        Assert.Equal(6, Model().Voxels.Count);
        Assert.Equal(before + 2, _project.History.Records.Count);

        _project.History.Undo();

        Assert.Equal(3, Model().Voxels.Count);
        Assert.True(Model().TryGetVoxel(new Vector3I(0, 0, 0), out _));
        Assert.False(Model().TryGetVoxel(new Vector3I(5, 0, 0), out _));
    }

    [Fact]
    public void AnErasingStrokeIsOneRecordToo()
    {
        Build();
        _view.RaiseBrushStroke(FloorRun(5));
        int before = _project.History.Records.Count;

        // Erasing aims at the voxel itself: the pick's cell is the filled cell.
        ScenePick[] erasing = Enumerable.Range(0, 5)
            .Select(x => new ScenePick(new Vector3I(x, 0, 0), Vector3I.Up, true, true))
            .ToArray();

        _view.RaiseBrushStroke(erasing);

        Assert.Empty(Model().Voxels);
        Assert.Equal(before + 1, _project.History.Records.Count);

        _project.History.Undo();

        Assert.Equal(5, Model().Voxels.Count);
    }

    /// <summary>Every step of a stroke writes its whole orbit, as a click does.</summary>
    [Fact]
    public void UnderAMirrorEveryStepWritesBothSides()
    {
        Build();
        _scene.SetSymmetry(_element, new SymmetryValues(new SymmetryPlane(true, 20), default, default));
        int before = _project.History.Records.Count;

        _view.RaiseBrushStroke(FloorRun(4));

        Assert.Equal(8, Model().Voxels.Count);

        for (var x = 0; x < 4; x++)
        {
            Assert.True(Model().TryGetVoxel(new Vector3I(x, 0, 0), out _));
            Assert.True(Model().TryGetVoxel(new Vector3I(19 - x, 0, 0), out _));
        }

        Assert.Equal(before + 1, _project.History.Records.Count);
    }

    /// <summary>The gesture is the session's while the button is down, and nobody's after.</summary>
    [Fact]
    public void TheGestureIsHeldOnTheSessionForTheLengthOfTheStroke()
    {
        Build();

        _view.RaiseStrokeBegan();

        Assert.True(_session.HasGesture);

        _view.RaisePicked(FloorRun(1)[0]);

        Assert.True(_session.HasGesture);

        _view.RaiseStrokeEnded();

        Assert.False(_session.HasGesture);
        Assert.Single(Model().Voxels);
    }

    [Fact]
    public void AStrokeUnderAnotherToolOpensNothingAndWritesNothing()
    {
        Build();
        _session.ActiveTool = ToolKind.Select;
        int before = _project.History.Records.Count;

        _view.RaiseStrokeBegan();

        Assert.False(_session.HasGesture);

        _view.RaisePicked(FloorRun(1)[0]);
        _view.RaiseStrokeEnded();

        Assert.Empty(Model().Voxels);
        Assert.Equal(before, _project.History.Records.Count);
    }

    /// <summary>
    /// With a material selected the brush has nothing to paint on, and the editor
    /// is never in a tool with nothing to act on: the fallback has already put it
    /// in the cursor by the time the press arrives, so it is the tool guard that
    /// refuses. The target guard behind it is belt and braces, and this test says
    /// so rather than claiming to reach it.
    /// </summary>
    [Fact]
    public void AStrokeWithNothingToPaintOnFallsBackAndOpensNothing()
    {
        Build();
        _session.SetSelection(new SelectionTarget(SelectionKind.Material, _project.Materials[0].Id));
        _session.ActiveTool = ToolKind.Paint;

        Assert.Equal(ToolKind.Select, _session.ActiveTool);

        _view.RaiseStrokeBegan();

        Assert.False(_session.HasGesture);

        _view.RaiseStrokeEnded();

        Assert.False(_session.HasGesture);
    }

    /// <summary>
    /// A gesture must never outlive the presenter that opened it: a document
    /// change mid-stroke closes the record rather than leaving a batch open over
    /// the next document's first edit.
    /// </summary>
    [Fact]
    public void DisposingMidStrokeClosesTheGesture()
    {
        Build();
        _view.RaiseStrokeBegan();
        _view.RaisePicked(FloorRun(1)[0]);

        Assert.True(_session.HasGesture);

        _document!.Dispose();
        _document = null;

        Assert.False(_session.HasGesture);
        Assert.Single(Model().Voxels);
    }

    /// <summary>
    /// The gesture ended under a stroke — by an action that ends the gesture in
    /// flight before it acts — ends the stroke: its record is filed, and the
    /// presenter tells the view to close rather than letting it go on painting
    /// against a surface it froze at the press.
    /// </summary>
    [Fact]
    public void AGestureEndedUnderAStrokeEndsTheStroke()
    {
        Build();
        int before = _project.History.Records.Count;
        ScenePick[] run = FloorRun(3);

        _view.RaiseStrokeBegan();
        _view.RaisePicked(run[0]);
        _view.RaisePicked(run[1]);

        Assert.Equal(0, _view.EndStrokeCount);

        _session.EndGesture();

        Assert.Equal(1, _view.EndStrokeCount);
        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.False(_session.HasGesture);

        // The view's own closing arrives afterwards and finds nothing to end.
        _view.RaiseStrokeEnded();

        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(2, Model().Voxels.Count);
    }

    /// <summary>
    /// The stroke's own release ends the gesture, which raises the very event the
    /// stroke listens to — and must not read as somebody else ending it.
    /// </summary>
    [Fact]
    public void AStrokesOwnReleaseDoesNotCloseItTwice()
    {
        Build();

        _view.RaiseBrushStroke(FloorRun(2));

        Assert.Equal(0, _view.EndStrokeCount);
        Assert.False(_session.HasGesture);
    }

    /// <summary>
    /// Opening a stroke over a gesture already in flight ends that gesture, and
    /// the stroke must not mistake that ending for its own.
    /// </summary>
    [Fact]
    public void OpeningOverAnotherGestureDoesNotCloseTheNewStroke()
    {
        Build();
        _session.BeginGesture(new Scope(() => { }));

        _view.RaiseStrokeBegan();
        _view.RaisePicked(FloorRun(1)[0]);

        Assert.Equal(0, _view.EndStrokeCount);
        Assert.True(_session.HasGesture);

        _view.RaiseStrokeEnded();

        Assert.False(_session.HasGesture);
        Assert.Single(Model().Voxels);
    }

    /// <summary>A plain click is the degenerate stroke, and still one record.</summary>
    [Fact]
    public void AClickWithoutAStrokeStillWritesOneRecord()
    {
        Build();
        int before = _project.History.Records.Count;

        _view.RaisePicked(FloorRun(1)[0]);

        Assert.Single(Model().Voxels);
        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.False(_session.HasGesture);
    }

    /// <summary>
    /// A stroke opening replaces any gesture the session was holding — the
    /// standing "anything that starts a new action ends the one in flight" rule.
    /// </summary>
    [Fact]
    public void AStrokeEndsAGestureAlreadyInFlight()
    {
        Build();
        var closed = false;
        _session.BeginGesture(new Scope(() => closed = true));

        _view.RaiseStrokeBegan();

        Assert.True(closed);
        Assert.True(_session.HasGesture);

        _view.RaiseStrokeEnded();
    }

    /// <summary>Picks along the floor at y=0, x from <paramref name="offset"/>, placing against the floor's inward face.</summary>
    private static ScenePick[] FloorRun(int count, int offset = 0) =>
        Enumerable.Range(offset, count)
            .Select(x => new ScenePick(new Vector3I(x, -1, 0), Vector3I.Up, false, false))
            .ToArray();

    private void Build()
    {
        _session.CurrentMaterialId = _project.Materials[0].Id;

        _document = new DocumentPresenter(
            _project,
            _scene,
            _hierarchy,
            _inspector,
            _history,
            _view,
            _session,
            new FakeDialogService(),
            new FakePreferences(),
            _ => { });

        // Selection first, tool second: a tool with nothing to act on falls back
        // to the cursor, which is also the order a hand does it in.
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, _element));
        _session.ActiveTool = ToolKind.Paint;
    }

    private IVoxelModel Model()
    {
        Assert.True(_scene.TryGetElement(_element, out IHierarchyElement element));

        return (IVoxelModel)element.Component!;
    }

    private sealed class Scope : IDisposable
    {
        private readonly Action _onDispose;

        public Scope(Action onDispose) => _onDispose = onDispose;

        public void Dispose() => _onDispose();
    }
}
