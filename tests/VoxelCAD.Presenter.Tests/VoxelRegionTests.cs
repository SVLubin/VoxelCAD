using System;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// A voxel change carries the box of cells it touched, and that box is the whole
/// of the chunked remesh at this seam.
/// </summary>
/// <remarks>
/// Nothing held it before, and the failure it guards against is invisible in
/// every other way: a presenter that quietly stopped carrying the region would
/// still draw the right picture, because null means "every chunk" and every
/// chunk is a superset of the right ones. What it would cost is the whole
/// optimisation — 400 ms a click instead of 3 to 10 — and no picture and no
/// other assertion would say so.
/// </remarks>
public sealed class VoxelRegionTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly DocumentPresenter _document;
    private readonly Guid _element;

    public VoxelRegionTests()
    {
        _scene = _project.Scenes[0];
        _session.CurrentMaterialId = _project.Materials[0].Id;

        _document = new DocumentPresenter(
            _project,
            _scene,
            new FakeHierarchyView(),
            new FakeInspectorView(),
            new FakeHistoryView(),
            _view,
            _session,
            new FakeDialogService(),
            new FakePreferences(),
            _ => { });

        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Block");
    }

    public void Dispose() => _document.Dispose();

    private CellRegion? RegionOf(Guid element) =>
        _view.VoxelRegions.TryGetValue(element, out CellRegion? region) ? region : null;

    private void Paint(params Vector3I[] cells)
    {
        var edits = new VoxelEdit[cells.Length];

        for (var index = 0; index < cells.Length; index++)
            edits[index] = new VoxelEdit(cells[index], _session.CurrentMaterialId);

        _scene.SetVoxels(_element, edits);
    }

    [Fact]
    public void OneCellCarriesTheOneCellItTouched()
    {
        Paint(new Vector3I(4, 5, 6));

        CellRegion? carried = RegionOf(_element);

        Assert.True(carried.HasValue, "the edit carried no region, so every chunk is rebuilt");

        CellRegion region = carried.Value;

        Assert.Equal(new Vector3I(4, 5, 6), region.Min);
        Assert.Equal(new Vector3I(4, 5, 6), region.Max);
    }

    [Fact]
    public void SeveralCellsCarryTheBoxAroundThemAll()
    {
        Paint(new Vector3I(1, 1, 1), new Vector3I(9, 2, 7));

        CellRegion? carried = RegionOf(_element);

        Assert.True(carried.HasValue, "the edit carried no region, so every chunk is rebuilt");

        CellRegion region = carried.Value;

        Assert.Equal(new Vector3I(1, 1, 1), region.Min);
        Assert.Equal(new Vector3I(9, 2, 7), region.Max);
    }

    /// <summary>
    /// An erase touches its cell as much as a paint does — the faces of every
    /// neighbour change — so it carries a region too.
    /// </summary>
    [Fact]
    public void AnEraseCarriesItsCellAsWell()
    {
        Paint(new Vector3I(3, 3, 3));

        _scene.SetVoxels(_element, new[] { new VoxelEdit(new Vector3I(3, 3, 3), null) });

        CellRegion? carried = RegionOf(_element);

        Assert.True(carried.HasValue, "the edit carried no region, so every chunk is rebuilt");

        CellRegion region = carried.Value;

        Assert.Equal(new Vector3I(3, 3, 3), region.Min);
    }

    /// <summary>
    /// A fresh element carries none: there is no previous state to have changed
    /// from, so every chunk it has is new. Null is that, and it is what the view
    /// reads as "rebuild everything".
    /// </summary>
    [Fact]
    public void AFreshElementCarriesNoRegionAtAll()
    {
        Assert.Null(RegionOf(_element));
    }
}
