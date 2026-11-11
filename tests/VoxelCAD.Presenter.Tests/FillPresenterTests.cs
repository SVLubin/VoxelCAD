using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The fill tool: a click recolours the connected volume of the clicked cell's
/// material with the current one, as one record, its mirror images of the same
/// material included.
/// </summary>
public sealed class FillPresenterTests : IDisposable
{
    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakeSceneView _view = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly Guid _element;
    private readonly Guid _red;
    private readonly Guid _blue;
    private DocumentPresenter? _document;

    public FillPresenterTests()
    {
        _scene = _project.Scenes[0];
        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");
        _red = _project.CreateMaterial("Red", new Color(1, 0, 0));
        _blue = _project.CreateMaterial("Blue", new Color(0, 0, 1));

        // Two red bars with a gap: 0..2 and 5..6 on x.
        _scene.SetVoxels(_element, Enumerable.Range(0, 3)
            .Concat(new[] { 5, 6 })
            .Select(x => new VoxelEdit(new Vector3I(x, 0, 0), _red))
            .ToList());
    }

    public void Dispose() => _document?.Dispose();

    [Fact]
    public void AClickRecoloursTheClickedVolumeAndNoOther()
    {
        Build();
        int before = _project.History.Records.Count;

        _view.RaisePicked(Pick(new Vector3I(1, 0, 0)));

        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(ActionKind.EditVoxels, _project.History.Records[^1].Kind);

        for (var x = 0; x < 3; x++)
            Assert.Equal(_blue, Model().Voxels[new Vector3I(x, 0, 0)]);

        Assert.Equal(_red, Model().Voxels[new Vector3I(5, 0, 0)]);
        Assert.Equal(_red, Model().Voxels[new Vector3I(6, 0, 0)]);

        _project.History.Undo();

        Assert.All(Model().Voxels.Values, worn => Assert.Equal(_red, worn));
    }

    /// <summary>The second use: a second fill straight after is its own record.</summary>
    [Fact]
    public void ASecondFillIsASecondRecord()
    {
        Build();
        int before = _project.History.Records.Count;

        _view.RaisePicked(Pick(new Vector3I(1, 0, 0)));
        _view.RaisePicked(Pick(new Vector3I(5, 0, 0)));

        Assert.Equal(before + 2, _project.History.Records.Count);
        Assert.All(Model().Voxels.Values, worn => Assert.Equal(_blue, worn));

        _project.History.Undo();

        Assert.Equal(_blue, Model().Voxels[new Vector3I(0, 0, 0)]);
        Assert.Equal(_red, Model().Voxels[new Vector3I(5, 0, 0)]);
    }

    [Fact]
    public void UnderAMirrorTheImageSeedsItsOwnVolume()
    {
        Build();

        // Plane at x=4 (doubled 8): 0..2 pairs with 7..5, so the click on 1 also
        // seeds 6, which floods the second bar.
        _scene.SetSymmetry(_element, new SymmetryValues(new SymmetryPlane(true, 8), default, default));
        int before = _project.History.Records.Count;

        _view.RaisePicked(Pick(new Vector3I(1, 0, 0)));

        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.All(Model().Voxels.Values, worn => Assert.Equal(_blue, worn));
    }

    [Fact]
    public void TheCurrentMaterialAlreadyWornRecordsNothing()
    {
        Build();
        _session.CurrentMaterialId = _red;
        int before = _project.History.Records.Count;

        _view.RaisePicked(Pick(new Vector3I(1, 0, 0)));

        Assert.Equal(before, _project.History.Records.Count);
    }

    [Fact]
    public void AWallPickAndAnotherToolAreIgnored()
    {
        Build();
        int before = _project.History.Records.Count;

        _view.RaisePicked(new ScenePick(new Vector3I(1, -1, 0), Vector3I.Up, false, false));

        _session.ActiveTool = ToolKind.Paint;
        _view.RaisePicked(Pick(new Vector3I(1, 0, 0)));

        // The brush answered the second one — it aims at the cell in front of the
        // face — and the fill did not.
        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(_red, Model().Voxels[new Vector3I(0, 0, 0)]);
    }

    /// <summary>
    /// An image landing in a region of another material seeds nothing: a fill
    /// recolours the region the click named, and the mirror repeats it only where
    /// the other side wears the same material. The other region stays as it was.
    /// </summary>
    [Fact]
    public void AnImageInsideAnotherMaterialSeedsNothing()
    {
        Build();

        // The right bar is blue; the plane at x=4 pairs the click's cell with it.
        _scene.SetVoxels(_element, new[]
        {
            new VoxelEdit(new Vector3I(5, 0, 0), _blue),
            new VoxelEdit(new Vector3I(6, 0, 0), _blue),
        });

        _scene.SetSymmetry(_element, new SymmetryValues(new SymmetryPlane(true, 8), default, default));
        Guid fill = _project.Materials[0].Id;
        _session.CurrentMaterialId = fill;

        _view.RaisePicked(Pick(new Vector3I(1, 0, 0)));

        for (var x = 0; x < 3; x++)
            Assert.Equal(fill, Model().Voxels[new Vector3I(x, 0, 0)]);

        Assert.Equal(_blue, Model().Voxels[new Vector3I(5, 0, 0)]);
        Assert.Equal(_blue, Model().Voxels[new Vector3I(6, 0, 0)]);
    }

    /// <summary>
    /// A prefab instance's contents are read-only wherever they appear, and the
    /// model refuses a write by throwing — which out of a click handler is a
    /// backtrace in the log and a click that silently did nothing.
    /// </summary>
    [Fact]
    public void AClickOnPrefabContentDoesNothing()
    {
        Build();
        _scene.CreatePrefab(_element);

        // A transfer, not a copy: an instance stands where the element did, and
        // its children are the derived, read-only contents.
        IHierarchyElement instance = _scene.Children[0];

        Assert.Equal(ComponentKind.PrefabInstance, instance.ComponentKind);

        Guid content = instance.Children[0].Id;
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, content));
        _session.ActiveTool = ToolKind.Fill;

        int before = _project.History.Records.Count;

        _view.RaisePicked(Pick(new Vector3I(1, 0, 0)));

        Assert.Equal(before, _project.History.Records.Count);
    }

    private static ScenePick Pick(Vector3I cell) => new(cell, Vector3I.Up, true, false);

    private void Build()
    {
        _session.CurrentMaterialId = _blue;

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
        _session.ActiveTool = ToolKind.Fill;
    }

    private IVoxelModel Model()
    {
        Assert.True(_scene.TryGetElement(_element, out IHierarchyElement element));

        return (IVoxelModel)element.Component!;
    }
}
