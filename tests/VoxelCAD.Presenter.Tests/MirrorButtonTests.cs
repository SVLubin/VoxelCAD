using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The symmetry rows' Mirror button: the image of every cell across the plane
/// the row shows, written where empty, as one record.
/// </summary>
public sealed class MirrorButtonTests : IDisposable
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

    public MirrorButtonTests()
    {
        _scene = _project.Scenes[0];
        _element = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        // Cells 0..2 on x; the first edit puts the pivot at x=1.5.
        _scene.SetVoxels(_element, Enumerable.Range(0, 3)
            .Select(x => new VoxelEdit(new Vector3I(x, 0, 0), _project.Materials[0].Id))
            .ToList());
    }

    public void Dispose() => _document?.Dispose();

    [Fact]
    public void TheButtonMirrorsAcrossTheRowsPlaneAsOneRecord()
    {
        Build();

        // The X row at +1.5 from the pivot: plane x=3, so 0..2 image to 5..3.
        _scene.SetSymmetry(_element, new SymmetryValues(new SymmetryPlane(true, 6), default, default));
        int before = _project.History.Records.Count;

        _inspector.RaiseMirrorRequested(Vector3.Axis.X);

        Assert.Equal(before + 1, _project.History.Records.Count);
        Assert.Equal(6, Model().Voxels.Count);

        for (var x = 3; x < 6; x++)
            Assert.True(Model().TryGetVoxel(new Vector3I(x, 0, 0), out _));

        _project.History.Undo();

        Assert.Equal(3, Model().Voxels.Count);
    }

    /// <summary>The second use: pressing again on a now symmetric model writes nothing.</summary>
    [Fact]
    public void ASecondPressOnASymmetricModelRecordsNothing()
    {
        Build();
        _scene.SetSymmetry(_element, new SymmetryValues(new SymmetryPlane(true, 6), default, default));

        _inspector.RaiseMirrorRequested(Vector3.Axis.X);
        int after = _project.History.Records.Count;

        _inspector.RaiseMirrorRequested(Vector3.Axis.X);

        Assert.Equal(after, _project.History.Records.Count);
    }

    /// <summary>A disabled row still shows a plane, and the button means that plane.</summary>
    [Fact]
    public void ADisabledRowStillMirrorsAcrossThePlaneItShows()
    {
        Build();
        _scene.SetSymmetry(_element, new SymmetryValues(new SymmetryPlane(false, 6), default, default));

        _inspector.RaiseMirrorRequested(Vector3.Axis.X);

        Assert.Equal(6, Model().Voxels.Count);
    }

    /// <summary>An untouched row shows 0 — through the pivot — and mirrors there.</summary>
    [Fact]
    public void AnUntouchedRowMirrorsThroughThePivot()
    {
        Build();
        _scene.SetVoxels(_element, new[] { new VoxelEdit(new Vector3I(3, 0, 0), _project.Materials[0].Id) });

        _inspector.RaiseMirrorRequested(Vector3.Axis.X);

        // Pivot x=1.5: 0..2 map onto themselves and cell 3 images to -1.
        Assert.Equal(5, Model().Voxels.Count);
        Assert.True(Model().TryGetVoxel(new Vector3I(-1, 0, 0), out _));
    }

    /// <summary>
    /// The rows are drawn on a prefab instance's contents — the element is
    /// inspectable — and the button must not write: the model refuses it by
    /// throwing, which out of a Godot button press is a logged backtrace and a
    /// control that appeared to do nothing.
    /// </summary>
    [Fact]
    public void OnPrefabContentTheButtonDoesNothing()
    {
        Build();
        _scene.CreatePrefab(_element);

        // A transfer, not a copy: an instance stands where the element did.
        IHierarchyElement instance = _scene.Children[0];

        Assert.Equal(ComponentKind.PrefabInstance, instance.ComponentKind);

        _session.SetSelection(
            new SelectionTarget(SelectionKind.Element, instance.Children[0].Id));

        int before = _project.History.Records.Count;

        _inspector.RaiseMirrorRequested(Vector3.Axis.X);

        Assert.Equal(before, _project.History.Records.Count);
    }

    [Fact]
    public void NothingSelectedDoesNothing()
    {
        Build();
        _session.ClearSelection();
        int before = _project.History.Records.Count;

        _inspector.RaiseMirrorRequested(Vector3.Axis.X);

        Assert.Equal(before, _project.History.Records.Count);
    }

    private void Build()
    {
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
