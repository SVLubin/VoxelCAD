using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The extrude tool as the document experiences it: the view measures the pull
/// and the pick carries the layers, a click arriving as ±1; the presenter writes
/// the whole run of the grabbed face by that count, as a single record, the new
/// cells carrying the face's own material.
/// </summary>
public sealed class ExtrudePresenterTests : IDisposable
{
    private static readonly Vector3I TopFace = new(0, 1, 0);

    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _element;
    private readonly Guid _material;
    private DocumentPresenter? _document;

    public ExtrudePresenterTests()
    {
        _scene = _project.Scenes[0];
        _material = _project.Materials[0].Id;
        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        // A 3x1x1 bar: three cells in a row, all showing their tops.
        _scene.SetVoxels(_element, new[]
        {
            new VoxelEdit(new Vector3I(0, 0, 0), _material),
            new VoxelEdit(new Vector3I(1, 0, 0), _material),
            new VoxelEdit(new Vector3I(2, 0, 0), _material)
        });
    }

    public void Dispose() => _document?.Dispose();

    [Fact]
    public void AClickPushesTheWholeFaceOutAsOneRecord()
    {
        Build();
        _session.RequestTool(ToolKind.Extrude);
        int recordsBefore = _project.History.Records.Count;

        Pick(new Vector3I(1, 0, 0), layers: 1);

        Assert.Equal(recordsBefore + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.EditVoxels, _project.History.Records[^1].Kind);
        Assert.Equal(6, Model().Voxels.Count);
        Assert.True(Model().Voxels.ContainsKey(new Vector3I(0, 1, 0)));
        Assert.True(Model().Voxels.ContainsKey(new Vector3I(2, 1, 0)));
    }

    /// <summary>A pull of three layers is still one record — one edit list is the whole answer.</summary>
    [Fact]
    public void ADragWritesItsWholeLayerCountAsOneRecord()
    {
        Build();
        _session.RequestTool(ToolKind.Extrude);
        int recordsBefore = _project.History.Records.Count;

        Pick(new Vector3I(1, 0, 0), layers: 3);

        Assert.Equal(recordsBefore + 1, _project.History.Records.Count);
        Assert.Equal(12, Model().Voxels.Count);
        Assert.True(Model().Voxels.ContainsKey(new Vector3I(1, 3, 0)));
    }

    /// <summary>
    /// Zero layers is a gesture that came home — and also every pick that is not
    /// an extrusion at all. Neither writes anything.
    /// </summary>
    [Fact]
    public void ZeroLayersWriteNothing()
    {
        Build();
        _session.RequestTool(ToolKind.Extrude);
        int recordsBefore = _project.History.Records.Count;

        Pick(new Vector3I(1, 0, 0), layers: 0);

        Assert.Equal(recordsBefore, _project.History.Records.Count);
        Assert.Equal(3, Model().Voxels.Count);
    }

    /// <summary>An extrusion continues the surface it grew from, not the palette.</summary>
    [Fact]
    public void TheNewCellsCarryTheFacesOwnMaterial()
    {
        Guid other = _project.CreateMaterial("Other", Colors.Red);
        Build();
        _session.CurrentMaterialId = other;
        _session.RequestTool(ToolKind.Extrude);

        Pick(new Vector3I(1, 0, 0), layers: 1);

        Assert.Equal(_material, Model().Voxels[new Vector3I(1, 1, 0)]);
    }

    /// <summary>The sign is the whole carve decision — the modifier stays in the view.</summary>
    [Fact]
    public void NegativeLayersCarveTheFaceIn()
    {
        Build();
        _session.RequestTool(ToolKind.Extrude);

        Pick(new Vector3I(1, 0, 0), layers: -1);

        Assert.Empty(Model().Voxels);
    }

    /// <summary>
    /// The viewport reports one Picked for every cell tool, so each listener checks
    /// the session rather than the presenters arbitrating between themselves.
    /// </summary>
    [Fact]
    public void AnotherToolIgnoresTheClick()
    {
        Build();
        _session.RequestTool(ToolKind.Move);

        Pick(new Vector3I(1, 0, 0), layers: 1);

        Assert.Equal(3, Model().Voxels.Count);
    }

    /// <summary>The region's walls have no face to take hold of.</summary>
    [Fact]
    public void AWallPickDoesNothing()
    {
        Build();
        _session.RequestTool(ToolKind.Extrude);

        _view.RaisePicked(new ScenePick(new Vector3I(1, 0, 0), TopFace, false, false, 1));

        Assert.Equal(3, Model().Voxels.Count);
    }

    /// <summary>
    /// A mirror at 1.5 on X — doubled 3, so cell 0 pairs with cell 2 — and one
    /// lone cell to push. Both sides grow, as one record, and outward on each.
    /// </summary>
    [Fact]
    public void UnderAMirrorOneClickExtrudesBothSides()
    {
        // Down to a single cell at the origin, so the mirror has somewhere to say
        // something the seed does not already cover.
        _scene.SetVoxels(_element, new[]
        {
            new VoxelEdit(new Vector3I(1, 0, 0), null),
            new VoxelEdit(new Vector3I(2, 0, 0), null)
        });

        Build();
        _scene.SetSymmetry(_element, new SymmetryValues(new SymmetryPlane(true, 3), default, default));
        _session.RequestTool(ToolKind.Extrude);
        int recordsBefore = _project.History.Records.Count;

        Pick(new Vector3I(0, 0, 0), layers: 1);

        Assert.Equal(recordsBefore + 1, _project.History.Records.Count);
        Assert.True(Model().Voxels.ContainsKey(new Vector3I(0, 1, 0)));
        Assert.True(Model().Voxels.ContainsKey(new Vector3I(2, 1, 0)));
        Assert.Equal(3, Model().Voxels.Count);
    }

    [Fact]
    public void UndoTakesAWholeDragBack()
    {
        Build();
        _session.RequestTool(ToolKind.Extrude);

        Pick(new Vector3I(1, 0, 0), layers: 3);
        _project.History.Undo();

        Assert.Equal(3, Model().Voxels.Count);
    }

    private void Pick(Vector3I cell, int layers) =>
        _view.RaisePicked(new ScenePick(cell, TopFace, true, false, layers));

    private void Build()
    {
        _session.CurrentMaterialId = _material;

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
