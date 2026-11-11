using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The eyedropper as the session experiences it: a click reads the voxel's
/// material into what the brush paints with, leaves the document — and so the
/// history — untouched, and answers to its own tool alone.
/// </summary>
public sealed class EyedropperTests : IDisposable
{
    private static readonly Vector3I Cell = new(1, 0, 0);

    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _element;
    private readonly Guid _sampledMaterial;
    private DocumentPresenter? _document;

    public EyedropperTests()
    {
        _scene = _project.Scenes[0];
        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _sampledMaterial = _project.CreateMaterial("Sampled", new Color(0.2f, 0.6f, 0.3f));
        _scene.SetVoxels(_element, new[] { new VoxelEdit(Cell, _sampledMaterial) });
    }

    public void Dispose() => _document?.Dispose();

    [Fact]
    public void SamplingAVoxelSelectsItsMaterial()
    {
        Build();
        _session.RequestTool(ToolKind.Eyedropper);

        _view.RaisePicked(new ScenePick(Cell, new Vector3I(0, 1, 0), true, false));

        Assert.Equal(_sampledMaterial, _session.CurrentMaterialId);
    }

    /// <summary>
    /// Sampling is choosing what the brush paints with, exactly like clicking
    /// the material's row — session state, so the document and the history do
    /// not move, and there is nothing for undo to take back.
    /// </summary>
    [Fact]
    public void SamplingLeavesNoHistoryRecord()
    {
        Build();
        _session.RequestTool(ToolKind.Eyedropper);
        int recordsBefore = _project.History.Records.Count;

        _view.RaisePicked(new ScenePick(Cell, new Vector3I(0, 1, 0), true, false));

        Assert.Equal(recordsBefore, _project.History.Records.Count);
    }

    /// <summary>
    /// The selection stays on the element being sampled: moving it to the
    /// material's row would take the pick target — and the inspector — away
    /// from the very model under the pointer.
    /// </summary>
    [Fact]
    public void SamplingLeavesTheSelectionAlone()
    {
        Build();
        _session.RequestTool(ToolKind.Eyedropper);

        _view.RaisePicked(new ScenePick(Cell, new Vector3I(0, 1, 0), true, false));

        Assert.Equal(new SelectionTarget(SelectionKind.Element, _element), _session.ActiveTarget);
    }

    /// <summary>
    /// Picking up a colour is a step in painting, so the tool goes back to the
    /// brush the moment it has one — the sampled material is only of use under the
    /// brush, and sitting in the eyedropper would make every sample two gestures.
    /// </summary>
    [Fact]
    public void SamplingSwitchesToTheBrush()
    {
        Build();
        _session.RequestTool(ToolKind.Eyedropper);

        _view.RaisePicked(new ScenePick(Cell, new Vector3I(0, 1, 0), true, false));

        Assert.Equal(ToolKind.Paint, _session.ActiveTool);
        Assert.Equal(ToolKind.Paint, _view.Tool);
    }

    /// <summary>
    /// The switch follows the material: a click that read nothing leaves the
    /// eyedropper in hand, so the next one can try again.
    /// </summary>
    [Fact]
    public void ASampleThatReadNothingKeepsTheEyedropper()
    {
        Build();
        _session.RequestTool(ToolKind.Eyedropper);

        _view.RaisePicked(new ScenePick(Cell, new Vector3I(0, 1, 0), false, false));

        Assert.Equal(ToolKind.Eyedropper, _session.ActiveTool);
    }

    /// <summary>
    /// The brush is where the keys that drive it arrive, so handing the tool over
    /// hands the keyboard with it — the same bargain any tool request makes.
    /// </summary>
    [Fact]
    public void SamplingHandsTheViewportTheKeyboard()
    {
        Build();
        _session.RequestTool(ToolKind.Eyedropper);
        int before = _view.KeyboardCount;

        _view.RaisePicked(new ScenePick(Cell, new Vector3I(0, 1, 0), true, false));

        Assert.True(_view.KeyboardCount > before);
    }

    [Fact]
    public void SamplingDoesNotPaint()
    {
        Build();
        _session.RequestTool(ToolKind.Eyedropper);

        // The empty cell in front of the face the brush would have filled.
        _view.RaisePicked(new ScenePick(Cell, new Vector3I(0, 1, 0), true, false));

        Assert.False(Model().TryGetVoxel(new Vector3I(1, 1, 0), out _));
    }

    /// <summary>A wall of the region has no material to give.</summary>
    [Fact]
    public void ARegionPickSamplesNothing()
    {
        Build();
        _session.RequestTool(ToolKind.Eyedropper);
        Guid before = _session.CurrentMaterialId;

        _view.RaisePicked(new ScenePick(Cell, new Vector3I(0, 1, 0), false, false));

        Assert.Equal(before, _session.CurrentMaterialId);
    }

    /// <summary>Under the brush a click paints; the eyedropper must sit it out.</summary>
    [Fact]
    public void UnderPaintAClickDoesNotSample()
    {
        Build();
        _session.RequestTool(ToolKind.Paint);

        Guid before = _session.CurrentMaterialId;

        _view.RaisePicked(new ScenePick(Cell, new Vector3I(0, 1, 0), true, false));

        Assert.Equal(before, _session.CurrentMaterialId);
        Assert.True(Model().TryGetVoxel(new Vector3I(1, 1, 0), out _));
    }

    /// <summary>The complement of the case above, pinned from the other side.</summary>
    [Fact]
    public void UnderTheEyedropperAClickDoesNotPaint()
    {
        Build();
        _session.RequestTool(ToolKind.Eyedropper);
        int recordsBefore = _project.History.Records.Count;

        _view.RaisePicked(new ScenePick(Cell, new Vector3I(0, 1, 0), true, false));

        Assert.Equal(recordsBefore, _project.History.Records.Count);
        Assert.False(Model().TryGetVoxel(new Vector3I(1, 1, 0), out _));
    }

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

        _session.SetSelection(new SelectionTarget(SelectionKind.Element, _element));
    }

    private IVoxelModel Model()
    {
        Assert.True(_scene.TryGetElement(_element, out IHierarchyElement element));

        return (IVoxelModel)element.Component!;
    }
}
