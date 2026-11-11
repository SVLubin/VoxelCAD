using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// What the two panels do with a camera: the viewport draws one so it can be
/// aimed, and the inspector edits the lens and says which one renders.
/// </summary>
public sealed class CameraPanelTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly IScene _scene;
    private readonly FakeSceneView _view = new();
    private readonly FakeHierarchyView _hierarchy = new();
    private readonly FakeInspectorView _inspector = new();
    private readonly FakeHistoryView _history = new();
    private readonly FakePreferences _preferences = new();
    private readonly EditorSession _session = new();
    private readonly DocumentPresenter _document;

    public CameraPanelTests()
    {
        _scene = _project.Scenes[0];
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
            _preferences,
            _ => { });
    }

    public void Dispose() => _document.Dispose();

    // ---- the viewport ----

    /// <summary>
    /// A camera holds no cells, so without a gizmo it is a row in the tree and
    /// nothing on screen — and a shot nobody can see is a shot nobody can aim.
    /// </summary>
    [Fact]
    public void ACameraIsDrawnAndAnOrdinaryElementIsNot()
    {
        Guid camera = Camera("Camera");
        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        Assert.NotNull(_view.Cameras[camera].Lens);
        Assert.Null(_view.Cameras[model].Lens);
    }

    [Fact]
    public void TheLensReachesTheDrawing()
    {
        Guid camera = Camera("Camera");
        var lens = new CameraLens(RenderProjection.Orthographic, 24f, 14f, 1f, 5f);

        _scene.SetCameraLens(camera, lens);

        Assert.Equal(lens, _view.Cameras[camera].Lens);
    }

    /// <summary>
    /// Which camera renders is a fact about two of them at once, and the change
    /// names only the winner — so the one that lost it has to be re-dressed as
    /// well, or two cameras would be drawn as the one that renders.
    /// </summary>
    [Fact]
    public void ChoosingACameraRedressesTheOneThatLostIt()
    {
        Guid first = Camera("First");
        Guid second = Camera("Second");

        Assert.True(_view.Cameras[first].Active);
        Assert.False(_view.Cameras[second].Active);

        _scene.SetActiveCamera(second);

        Assert.False(_view.Cameras[first].Active);
        Assert.True(_view.Cameras[second].Active);
    }

    /// <summary>
    /// The fallback is answered rather than announced, so deleting the camera that
    /// was rendering has to re-dress the one that inherits it.
    /// </summary>
    [Fact]
    public void DeletingTheRenderingCameraRedressesTheOneThatInheritsIt()
    {
        Guid first = Camera("First");
        Guid second = Camera("Second");

        _scene.SetActiveCamera(second);
        _scene.DeleteElement(second);

        Assert.True(_view.Cameras[first].Active);
    }

    /// <summary>
    /// What the wireframe encloses has to be what the picture will contain, so the
    /// frustum follows the render form's own size.
    /// </summary>
    [Fact]
    public void TheFrustumIsDrawnAtThePicturesOwnShape()
    {
        Assert.Equal(16f / 9f, _view.CameraAspect, 3);

        _preferences.ApplyRenderDials(
            _preferences.RenderDials with { Width = 1000, Height = 500 });

        Assert.Equal(2f, _view.CameraAspect, 3);
    }

    // ---- the inspector ----

    [Fact]
    public void SelectingACameraShowsItsLensAndWhetherItRenders()
    {
        Guid camera = Camera("Camera");

        Select(camera);

        Assert.Equal(CameraLens.Default, _inspector.Camera);
        Assert.True(_inspector.CameraRenders);
    }

    /// <summary>
    /// The section stays hidden for any target it never arrives for, the bargain
    /// the symmetry section already strikes.
    /// </summary>
    [Fact]
    public void SelectingSomethingElseHidesTheSection()
    {
        Camera("Camera");

        Guid model = _scene.CreateElement(_scene.Id, ComponentKind.VoxelModel, "Model");

        Select(model);

        Assert.Null(_inspector.Camera);
    }

    [Fact]
    public void EditingTheLensReachesTheDocumentAsOneRecord()
    {
        Guid camera = Camera("Camera");

        Select(camera);

        var lens = new CameraLens(RenderProjection.Orthographic, 30f, 9f, 0.5f, 20f);

        _inspector.RaiseCameraLensEdited(lens);

        Assert.Equal(lens, ((ICamera)Element(camera).Component!).Lens);
        Assert.Equal(ActionKind.ChangeCamera, _project.History.Records[^1].Kind);
    }

    [Fact]
    public void AskingToRenderThroughItReachesTheDocument()
    {
        Camera("First");

        Guid second = Camera("Second");

        Select(second);

        Assert.False(_inspector.CameraRenders);

        _inspector.RaiseActiveCameraRequested();

        Assert.Equal(second, _scene.ActiveCameraId);
        Assert.True(_inspector.CameraRenders);
    }

    /// <summary>
    /// The row is about the scene rather than about the element, so the camera
    /// that lost the picture has to stop saying it renders even though nothing
    /// about that element changed.
    /// </summary>
    [Fact]
    public void ACameraThatLosesThePictureStopsSayingItRenders()
    {
        Guid first = Camera("First");
        Guid second = Camera("Second");

        Select(first);

        Assert.True(_inspector.CameraRenders);

        _scene.SetActiveCamera(second);

        Assert.False(_inspector.CameraRenders);
    }

    /// <summary>
    /// An undone lens edit has to reach the panel, or the fields would go on
    /// showing what was typed rather than what the document holds.
    /// </summary>
    [Fact]
    public void UndoingALensEditRefreshesThePanel()
    {
        Guid camera = Camera("Camera");

        Select(camera);

        _inspector.RaiseCameraLensEdited(CameraLens.Default with { Aperture = 3f });

        Assert.Equal(3f, _inspector.Camera!.Value.Aperture);

        _project.History.Undo();

        Assert.Equal(0f, _inspector.Camera!.Value.Aperture);
    }

    private Guid Camera(string name) =>
        _scene.CreateElement(_scene.Id, ComponentKind.Camera, name);

    private IHierarchyElement Element(Guid id)
    {
        _scene.TryGetElement(id, out IHierarchyElement element);

        return element;
    }

    private void Select(Guid id) =>
        _session.SetSelection(new SelectionTarget(SelectionKind.Element, id));
}
