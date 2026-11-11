using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The pivot row of the inspector: it shows the point the Position row names, in
/// the element's own cells; typed edits go to the model raw, and the Center
/// button asks for the one-shot centring through the very calculation the
/// command surface's <c>point=center</c> uses.
/// </summary>
public sealed class PivotInspectorTests : IDisposable
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

    public PivotInspectorTests()
    {
        _scene = _project.Scenes[0];
        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
    }

    public void Dispose() => _document?.Dispose();

    [Fact]
    public void ANewElementShowsAZeroPivot()
    {
        Build();

        Assert.Equal(Vector3.Zero, _inspector.Pivot);
    }

    /// <summary>
    /// The row speaks in offsets from the content's middle, so a freshly painted
    /// model reads 0 — the first edit's default is the centre of what it filled —
    /// and an asymmetric second edit moves the middle out from under the pivot,
    /// which the row reports as the drift it is.
    /// </summary>
    [Fact]
    public void TheRowReadsZeroAfterTheFirstPaintAndDriftAfterTheNext()
    {
        Build();
        FillCube();

        Assert.Equal(Vector3.Zero, _inspector.Pivot);

        _scene.SetVoxels(_element, new[]
        {
            new VoxelEdit(new Vector3I(20, 20, 20), _project.Materials[0].Id)
        });

        // Cells now span [0, 21): centred on 10.5, with the pivot still at 2.
        Assert.Equal(new Vector3(-8.5f, -8.5f, -8.5f), _inspector.Pivot);
    }

    [Fact]
    public void ATypedPointComesBackOnTheHalfCellLattice()
    {
        Build();

        _inspector.RaisePivotEdited(new Vector3(2.3f, 0f, 0f));

        Assert.Equal(new Vector3(2.5f, 0f, 0f), _inspector.Pivot);
        Assert.Equal(new Vector3(2.5f, 0f, 0f), Element().PivotPoint);
    }

    /// <summary>
    /// The Center button after the model has grown: the default stayed where the
    /// first edit put it, the button re-centres over everything there is now.
    /// </summary>
    [Fact]
    public void TheCenterButtonRecentresOverTheCellsAsTheyStand()
    {
        Build();
        FillCube();

        _scene.SetVoxels(_element, new[]
        {
            new VoxelEdit(new Vector3I(20, 20, 20), _project.Materials[0].Id)
        });

        Assert.Equal(new Vector3(-8.5f, -8.5f, -8.5f), _inspector.Pivot);

        _inspector.RaisePivotCenterRequested();

        Assert.Equal(Vector3.Zero, _inspector.Pivot);
    }

    [Fact]
    public void CenteringAnEmptyModelChangesNothing()
    {
        Build();
        int recordsBefore = _project.History.Records.Count;

        _inspector.RaisePivotCenterRequested();

        Assert.Equal(recordsBefore, _project.History.Records.Count);
    }

    /// <summary>
    /// A default mirror slot reads 0 — not the distance to the local origin it
    /// happens to rest in — and enabling it at that 0 puts the mirror through
    /// the pivot, which is what a fresh checkbox should mean.
    /// </summary>
    [Fact]
    public void DefaultMirrorRowsReadZeroAndEnableThroughThePivot()
    {
        Build();
        FillCube();

        Assert.Equal(default(SymmetryValues), _inspector.Symmetry);

        _inspector.RaiseSymmetryEdited(Vector3.Axis.X, true, 0f);

        IVoxelModel model = (IVoxelModel)Element().Component!;

        // The pivot sits at (2, 2, 2); doubled, the plane through it is 4.
        Assert.Equal(new SymmetryPlane(true, 4), model.Symmetry.X);
        Assert.Equal(new SymmetryPlane(true, 0), _inspector.Symmetry?.X);
    }

    /// <summary>A group has a pivot row too; symmetry stays a voxel model's own.</summary>
    [Fact]
    public void AGroupShowsAPivotAsWell()
    {
        Guid group = _scene.CreateElement(_scene.Id, ComponentKind.None, "Group");

        Build();
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, group));

        Assert.NotNull(_inspector.Pivot);
        Assert.Null(_inspector.Symmetry);
    }

    [Fact]
    public void AMaterialHasNoPivotRow()
    {
        Build();
        _session.SetSelection(new SelectionTarget(SelectionKind.Material, _project.Materials[0].Id));

        Assert.Null(_inspector.Pivot);
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

    private void FillCube()
    {
        var editsList = new List<VoxelEdit>();

        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                for (var z = 0; z < 4; z++)
                    editsList.Add(new VoxelEdit(new Vector3I(x, y, z), _project.Materials[0].Id));
            }
        }

        _scene.SetVoxels(_element, editsList);
    }

    private IHierarchyElement Element()
    {
        Assert.True(_scene.TryGetElement(_element, out IHierarchyElement element));

        return element;
    }
}
