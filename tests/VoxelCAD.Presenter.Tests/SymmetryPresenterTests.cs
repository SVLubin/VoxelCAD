using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// Symmetry as the brush experiences it: a click writes the whole orbit of its
/// cell as one record, erasing defeats every image at once, and the viewport is
/// kept told which slots are on so its preview can offer the same orbit.
/// </summary>
public sealed class SymmetryPresenterTests : IDisposable
{
    private static readonly Vector3I Cell = new(1, 0, 0);
    private static readonly Vector3I Mirrored = new(4, 0, 0);

    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _element;
    private DocumentPresenter? _document;

    public SymmetryPresenterTests()
    {
        _scene = _project.Scenes[0];
        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
    }

    public void Dispose() => _document?.Dispose();

    /// <summary>Mirror across the plane at 3 on X: cell 1 pairs with cell 4.</summary>
    private static SymmetryValues MirrorX =>
        new(new SymmetryPlane(true, 6), default, default);

    [Fact]
    public void PaintingUnderAMirrorWritesBothSidesAsOneRecord()
    {
        Build();
        _scene.SetSymmetry(_element, MirrorX);
        int recordsBefore = _project.History.Records.Count;

        // A click on the -X face of a cell one step right of the target: the
        // brush places at Cell, and the mirror carries it to Mirrored.
        _view.RaisePicked(new ScenePick(new Vector3I(2, 0, 0), new Vector3I(-1, 0, 0), true, false));

        Assert.Equal(recordsBefore + 1, _project.History.Records.Count);
        Assert.True(Model().TryGetVoxel(Cell, out _));
        Assert.True(Model().TryGetVoxel(Mirrored, out _));

        _project.History.Undo();

        Assert.Empty(Model().Voxels);
    }

    [Fact]
    public void ErasingUnderAMirrorDefeatsEveryImage()
    {
        Build();
        _scene.SetVoxels(_element, new[]
        {
            new VoxelEdit(Cell, _session.CurrentMaterialId),
            new VoxelEdit(Mirrored, _session.CurrentMaterialId)
        });
        _scene.SetSymmetry(_element, MirrorX);

        _view.RaisePicked(new ScenePick(Cell, new Vector3I(0, 1, 0), true, true));

        Assert.Empty(Model().Voxels);
    }

    /// <summary>
    /// A cell lying on the plane is its own image: the click writes once and the
    /// history holds exactly one change, not a duplicate folded away.
    /// </summary>
    [Fact]
    public void ACellOnThePlaneWritesOnce()
    {
        Build();
        _scene.SetSymmetry(_element, new SymmetryValues(new SymmetryPlane(true, 7), default, default));

        _view.RaisePicked(new ScenePick(new Vector3I(3, 1, 0), new Vector3I(0, -1, 0), true, false));

        Assert.Equal(new[] { new Vector3I(3, 0, 0) }, new List<Vector3I>(Model().Voxels.Keys));
    }

    [Fact]
    public void WithEverySlotOffAClickWritesTheOneCell()
    {
        Build();

        _view.RaisePicked(new ScenePick(new Vector3I(2, 0, 0), new Vector3I(-1, 0, 0), true, false));

        Assert.Equal(new[] { Cell }, new List<Vector3I>(Model().Voxels.Keys));
    }

    [Fact]
    public void TheViewportIsToldTheSlotsWhenTheyChange()
    {
        Build();

        _scene.SetSymmetry(_element, MirrorX);

        Assert.Equal(MirrorX, _view.Symmetry[_element]);
    }

    /// <summary>A document opened with slots already set pushes them from scratch.</summary>
    [Fact]
    public void TheConstructorPushesTheSlotsItFinds()
    {
        _scene.SetSymmetry(_element, MirrorX);

        Build();

        Assert.Equal(MirrorX, _view.Symmetry[_element]);
    }

    [Fact]
    public void SelectingAVoxelElementShowsItsSlots()
    {
        _scene.SetSymmetry(_element, MirrorX);

        Build();

        Assert.Equal(MirrorX, _inspector.Symmetry);
    }

    [Fact]
    public void SelectingAGroupHidesTheSection()
    {
        Build();
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        _session.SetSelection(new SelectionTarget(SelectionKind.Element, group));

        Assert.Null(_inspector.Symmetry);
    }

    /// <summary>
    /// What an element holds reaches the panel, which is what its title's glyph
    /// is drawn from. Without it a group and a voxel model are told apart only
    /// by which sections happen to turn up, and that is not a thing to read.
    /// </summary>
    [Fact]
    public void TheTitleIsToldWhatTheElementHolds()
    {
        Build();

        Assert.Equal(ComponentKind.VoxelModel, _inspector.TargetComponent);

        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        _session.SetSelection(new SelectionTarget(SelectionKind.Element, group));

        Assert.Equal(ComponentKind.None, _inspector.TargetComponent);
    }

    [Fact]
    public void AnEditedRowWritesThroughToTheModel()
    {
        Build();

        _inspector.RaiseSymmetryEdited(Vector3.Axis.X, true, 3.5f);

        Assert.Equal(new SymmetryPlane(true, 7), Model().Symmetry.X);
    }

    /// <summary>
    /// The presenter rounds, not the spin box: a position off the half-cell
    /// lattice lands on the nearest half, away from zero at the midpoints.
    /// </summary>
    [Fact]
    public void AnOffLatticePositionIsRoundedToTheNearestHalf()
    {
        Build();

        _inspector.RaiseSymmetryEdited(Vector3.Axis.Y, true, 3.3f);
        Assert.Equal(new SymmetryPlane(true, 7), Model().Symmetry.Y);

        _inspector.RaiseSymmetryEdited(Vector3.Axis.Z, true, 3.25f);
        Assert.Equal(new SymmetryPlane(true, 7), Model().Symmetry.Z);
    }

    /// <summary>An agent's set-symmetry lands in the open section too.</summary>
    [Fact]
    public void AModelSideChangeRefreshesTheSection()
    {
        Build();

        _scene.SetSymmetry(_element, MirrorX);

        Assert.Equal(MirrorX, _inspector.Symmetry);
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

        // In this order, and not by taste: the editor opens on the cursor, these
        // are about what the brush does with a click, and a tool with nothing to
        // act on falls straight back to the cursor — so the model is picked first
        // and the brush reached for second, which is also what a hand does.
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, _element));
        _session.ActiveTool = ToolKind.Paint;
    }

    private IVoxelModel Model()
    {
        Assert.True(_scene.TryGetElement(_element, out IHierarchyElement element));

        return (IVoxelModel)element.Component!;
    }
}
