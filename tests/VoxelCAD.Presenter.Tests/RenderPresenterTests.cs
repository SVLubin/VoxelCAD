using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Commands;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using VoxelCAD.Render;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The render card's decisions, none of which anything asked about before: every
/// one of them is gated behind there being a render, and only a graphics device
/// could make one. Behind <see cref="IRenderJobs"/> they are ordinary.
/// </summary>
/// <remarks>
/// What is still not here is the tracing itself and anything the picture looks
/// like — those want a device and a pair of eyes, and the ledger says so.
/// </remarks>
public sealed class RenderPresenterTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly FakeRenderView _card = new();
    private readonly FakeSceneView _scene = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakePreferences _preferences = new();
    private readonly FakeRenderJobs _jobs = new();
    private readonly CommandContext _context;
    private readonly RenderPresenter _presenter;

    public RenderPresenterTests()
    {
        _context = new CommandContext(_project);
        _presenter = new RenderPresenter(_card, _scene, _context, _dialogs, _preferences, _jobs);
    }

    public void Dispose() => _presenter.Dispose();

    [Fact]
    public void OpeningRaisesTheCardAndStartsARenderAtOnce()
    {
        _presenter.Open();

        Assert.True(_card.IsOpen);
        Assert.Equal(1, _jobs.Starts);
    }

    /// <summary>
    /// A render setup is a thing somebody arrived at, so it survives the card being
    /// put away — which is what the stored dials are for.
    /// </summary>
    [Fact]
    public void TheCardOpensAtWhatItWasLastLeftAt()
    {
        _preferences.ApplyRenderDials(RenderDials.Default with { Samples = 999, Width = 640 });

        _presenter.Open();

        Assert.Equal(999, _card.Dials.Samples);
        Assert.Equal(640, _jobs.Started[0].Width);
    }

    [Fact]
    public void ClosingStopsTheRender()
    {
        _presenter.Open();

        FakeRenderJob? job = _jobs.Last;

        _presenter.Close();

        Assert.False(_card.IsOpen);
        Assert.True(job!.Disposed);
    }

    /// <summary>
    /// A machine with no device that runs compute is an answer rather than a fault,
    /// so it is said on the card instead of thrown.
    /// </summary>
    [Fact]
    public void ARefusedRenderIsReportedRatherThanThrown()
    {
        _jobs.Refusal = "no device";

        _presenter.Open();

        Assert.True(_card.IsOpen);
        Assert.Contains("no device", _card.Message);
    }

    /// <summary>
    /// Two dials describe how the result is read rather than what is traced, and a
    /// picture that started again for one of those would throw away a finished
    /// render for a change costing one pass over a buffer.
    /// </summary>
    [Fact]
    public void ExposureAndSamplesRegradeInsteadOfStartingAgain()
    {
        _presenter.Open();

        FakeRenderJob job = _jobs.Last!;

        _presenter.ApplyDials(_card.Dials with { Exposure = 2.5f, Samples = 4096 });

        Assert.Equal(1, _jobs.Starts);
        Assert.False(job.Disposed);
        Assert.Equal(2.5f, job.Exposure);
        Assert.Equal(4096, job.SamplesWanted);
    }

    /// <summary>
    /// Everything else changes what the light does, and a picture that kept its old
    /// samples through one of those would be two renders averaged together.
    /// </summary>
    [Theory]
    [InlineData("width")]
    [InlineData("sun")]
    [InlineData("glare")]
    [InlineData("ground")]
    public void AnythingElseStartsAgain(string dial)
    {
        _presenter.Open();

        FakeRenderJob first = _jobs.Last!;
        RenderDials dials = _card.Dials;

        _presenter.ApplyDials(dial switch
        {
            "width" => dials with { Width = 320 },
            "sun" => dials with { SunElevationDegrees = 10f },
            "glare" => dials with { Glare = 0.2f },
            _ => dials with { Ground = !dials.Ground }
        });

        Assert.Equal(2, _jobs.Starts);
        Assert.True(first.Disposed, "the render it replaced is stopped, not left running");
    }

    /// <summary>
    /// Turning a dial stores it, so the setup outlives the session and not merely
    /// the card.
    /// </summary>
    [Fact]
    public void AMovedDialReachesTheStoredPreferences()
    {
        _presenter.Open();

        _presenter.ApplyDials(_card.Dials with { Samples = 77 });

        Assert.Equal(77, _preferences.RenderDials.Samples);
    }

    /// <summary>
    /// A render is a snapshot: compiled from the document as it stood, and it does
    /// not follow later edits. That is what a picture is — and a picture that has
    /// quietly stopped matching the model is worth saying out loud.
    /// </summary>
    [Fact]
    public void EditingTheSceneMarksThePictureStale()
    {
        _presenter.Open();

        Assert.False(_presenter.IsStale);

        Guid element = _context.Scene.CreateElement(
            _context.Scene.Id, ComponentKind.VoxelModel, "Block");

        Assert.True(_presenter.IsStale);
        Assert.Contains("changed", _card.Message);

        // Named so the reader knows why an unused id is fine here.
        Assert.NotEqual(Guid.Empty, element);
    }

    /// <summary>
    /// A material is not in a scene, and recolouring one changes every picture that
    /// wears it — so the project is watched as well as the tree.
    /// </summary>
    [Fact]
    public void RecolouringAMaterialMarksThePictureStale()
    {
        _presenter.Open();

        Guid material = _project.Materials[0].Id;

        _project.SetMaterialColor(material, new Godot.Color(0.1f, 0.2f, 0.3f));

        Assert.True(_presenter.IsStale);
    }

    /// <summary>
    /// Taking the picture again is the only way to get one of a scene that has
    /// moved, and it clears the notice that said so.
    /// </summary>
    [Fact]
    public void RenderingAgainClearsTheStaleNoticeAndStartsOver()
    {
        _presenter.Open();

        _context.Scene.CreateElement(_context.Scene.Id, ComponentKind.VoxelModel, "Block");

        Assert.True(_presenter.IsStale);

        _presenter.RenderAgain();

        Assert.False(_presenter.IsStale);
        Assert.Equal(2, _jobs.Starts);
    }

    /// <summary>
    /// The presenter outlives the document while a render does not, so the scene
    /// subscription has to go with the render — or a picture nobody has open would
    /// go on reacting to edits it is not a picture of.
    /// </summary>
    [Fact]
    public void AClosedCardStopsFollowingTheDocument()
    {
        _presenter.Open();
        _presenter.Close();

        _context.Scene.CreateElement(_context.Scene.Id, ComponentKind.VoxelModel, "Block");

        Assert.False(_presenter.IsStale);
    }

    /// <summary>
    /// Focusing reads the distance the render already measured for that pixel, so
    /// what is focused on is exactly what is drawn there — and it writes the
    /// camera's own lens, which is where a focus distance lives now.
    /// </summary>
    [Fact]
    public void FocusingOnAPointWritesTheDistanceOntoTheCamera()
    {
        _presenter.Open();
        _jobs.Last!.Depth = 42.5f;

        Assert.True(_presenter.FocusAt(10, 10));
        Assert.Equal(42.5f, Lens().FocusDistance);

        // An edit to the document, so it is on the history like any other.
        Assert.Equal(ActionKind.ChangeCamera, _project.History.Records[^1].Kind);

        // Focus decides what the light does, so it is a restart like any other.
        Assert.Equal(2, _jobs.Starts);
        Assert.Equal(42.5f, _jobs.Started[1].FocusDistance);
    }

    /// <summary>
    /// Where the camera ray reached the sky there is no distance, and focusing on
    /// nothing has to be refused rather than answered with a zero.
    /// </summary>
    [Fact]
    public void FocusingWhereThePictureShowsNothingIsRefused()
    {
        _presenter.Open();
        _jobs.Last!.Depth = null;

        Assert.False(_presenter.FocusAt(10, 10));
        Assert.Equal(1, _jobs.Starts);
    }

    /// <summary>
    /// Setting the dials with no card up does nothing at all — there is no form to
    /// write to, and writing the stored preferences instead would be an agent
    /// changing what the user finds next time they open it.
    /// </summary>
    [Fact]
    public void SettingDialsWithNoCardOpenDoesNothing()
    {
        _presenter.ApplyDials(RenderDials.Default with { Samples = 5 });

        Assert.Equal(0, _jobs.Starts);
        Assert.Equal(0, _card.DialsShown);
    }

    /// <summary>
    /// Every frame is one submission, and a card with no render does not pretend
    /// to be ticking one.
    /// </summary>
    // ---- the camera the picture is taken through ----

    /// <summary>
    /// A picture is taken through a camera, so asking for one where the scene has
    /// none places it — one record, where the viewport is looking, and a row in the
    /// tree that can be aimed afterwards.
    /// </summary>
    [Fact]
    public void OpeningTheCardInASceneWithNoCameraPlacesOne()
    {
        Assert.Equal(Guid.Empty, _context.Scene.ActiveCameraId);

        _presenter.Open();

        Assert.NotEqual(Guid.Empty, _context.Scene.ActiveCameraId);
        Assert.Single(_project.History.Records);
        Assert.Equal(ActionKind.CreateElement, _project.History.Records[0].Kind);
        Assert.Equal(1, _jobs.Starts);
    }

    /// <summary>
    /// Placed where the viewport is looking, and at its angle — the camera it
    /// would otherwise have asked somebody to make is the camera they were already
    /// framing.
    /// </summary>
    [Fact]
    public void ThePlacedCameraStandsWhereTheViewportWasLooking()
    {
        var stand = new Transform3D(Basis.Identity, new Vector3(3f, 9f, 27f));

        _scene.CameraView = new CameraView(stand, 35f);

        _presenter.Open();

        _context.Scene.TryGetElement(_context.Scene.ActiveCameraId, out IHierarchyElement camera);

        Assert.Equal(new Vector3(3f, 9f, 27f), camera.Transform.Position);
        Assert.Equal(35f, ((ICamera)camera.Component!).Lens.FovDegrees);
    }

    /// <summary>
    /// A camera placed from a view is placed to keep a shot, so its position and
    /// its aim must not be snapped to whole cells and right angles the way a
    /// buildable element's are.
    /// </summary>
    [Fact]
    public void ThePlacedCameraIsNotQuantisedOutOfItsOwnShot()
    {
        var stand = new Transform3D(
            new Basis(Vector3.Up, Mathf.DegToRad(37f)),
            new Vector3(1.25f, 6.5f, -3.75f));

        _scene.CameraView = new CameraView(stand, 50f);

        _presenter.Open();

        _context.Scene.TryGetElement(_context.Scene.ActiveCameraId, out IHierarchyElement camera);

        Assert.Equal(new Vector3(1.25f, 6.5f, -3.75f), camera.Transform.Position);
        Assert.Equal(37f, camera.Transform.RotationDegrees.Y, 3);
        Assert.Equal(FieldMode.Float, camera.TransformModes.Position);
        Assert.Equal(FieldMode.Float, camera.TransformModes.Rotation);
    }

    [Fact]
    public void ASceneThatAlreadyHasACameraGetsNoSecondOne()
    {
        Guid made = _context.Scene.CreateElement(
            _context.Scene.Id, ComponentKind.Camera, "Mine");

        int before = _project.History.Records.Count;

        _presenter.Open();

        Assert.Equal(made, _context.Scene.ActiveCameraId);
        Assert.Equal(before, _project.History.Records.Count);
    }

    /// <summary>
    /// The whole of "only one camera renders": the tracer is handed the active
    /// one's place, and choosing another hands it a different one.
    /// </summary>
    [Fact]
    public void TheRenderIsTakenFromWhereTheActiveCameraStands()
    {
        Guid near = Camera("Near", new Vector3(1f, 2f, 3f));
        Guid far = Camera("Far", new Vector3(40f, 50f, 60f));

        _presenter.Open();

        Assert.Equal(new Vector3(1f, 2f, 3f), _jobs.Cameras[0].Transform.Origin);

        _context.Scene.SetActiveCamera(far);
        _presenter.RenderAgain();

        Assert.Equal(new Vector3(40f, 50f, 60f), _jobs.Cameras[1].Transform.Origin);
        Assert.NotEqual(near, _context.Scene.ActiveCameraId);
    }

    /// <summary>
    /// The lens is the camera's and the film is the card's, so what the tracer is
    /// asked for has to be assembled from both.
    /// </summary>
    [Fact]
    public void TheLensReachesTheTracerFromTheCameraAndTheFilmFromTheCard()
    {
        Guid id = Camera("Camera", Vector3.Zero);

        _context.Scene.SetCameraLens(
            id,
            new CameraLens(RenderProjection.Orthographic, 20f, 18f, 1.5f, 12f));
        _preferences.ApplyRenderDials(RenderDials.Default with { Width = 640, Samples = 99 });

        _presenter.Open();

        RenderSettings asked = _jobs.Started[0];

        Assert.Equal(RenderProjection.Orthographic, asked.Projection);
        Assert.Equal(18f, asked.OrthographicHeight);
        Assert.Equal(1.5f, asked.Aperture);
        Assert.Equal(12f, asked.FocusDistance);
        Assert.Equal(20f, _jobs.Cameras[0].FovDegrees);
        Assert.Equal(640, asked.Width);
        Assert.Equal(99, asked.Samples);
    }

    /// <summary>
    /// Focusing means nothing through a pinhole, so the button that arms it is
    /// offered or refused by the camera's own blur rather than by a dial the card
    /// no longer holds.
    /// </summary>
    [Fact]
    public void TheFocusButtonFollowsTheCamerasBlur()
    {
        Guid id = Camera("Camera", Vector3.Zero);

        _presenter.Open();

        Assert.False(_card.FocusAvailable);

        _context.Scene.SetCameraLens(id, CameraLens.Default with { Aperture = 2f });
        _presenter.RenderAgain();

        Assert.True(_card.FocusAvailable);
    }

    /// <summary>
    /// Reachable one way only — the camera was deleted while the card was open —
    /// and it is a sentence rather than a throw, because it is a thing the user
    /// can fix.
    /// </summary>
    [Fact]
    public void ARenderWithNoCameraLeftIsRefusedAndSaysWhy()
    {
        _presenter.Open();

        _context.Scene.DeleteElement(_context.Scene.ActiveCameraId);
        _presenter.RenderAgain();

        Assert.Equal(1, _jobs.Starts);
        Assert.Contains("no camera", _presenter.Message);
        Assert.False(_card.FocusAvailable);
    }

    /// <summary>
    /// A camera ignores scale — it has no size of its own — but a scale of nought
    /// leaves which way it points underivable, so it is refused by name rather
    /// than guessed at. The same rule the picker keeps, and the one place an
    /// infinity would otherwise become a direction.
    /// </summary>
    [Fact]
    public void ACameraScaledToNothingIsRefusedByName()
    {
        Guid id = Camera("Squashed", Vector3.Zero);

        _presenter.Open();
        _context.Scene.SetTransform(id, TransformChannel.Scale, new Vector3(1f, 0f, 1f));
        _presenter.RenderAgain();

        Assert.Equal(1, _jobs.Starts);
        Assert.Contains("Squashed", _presenter.Message);
        Assert.Contains("zero scale", _presenter.Message);
    }

    /// <summary>
    /// And an ordinary scale is ignored rather than skewing every ray: what the
    /// tracer is handed is the camera's rotation, not its basis.
    /// </summary>
    [Fact]
    public void AScaledCameraStillTracesSquare()
    {
        Guid id = Camera("Stretched", new Vector3(2f, 3f, 4f));

        _context.Scene.SetTransform(id, TransformChannel.Scale, new Vector3(3f, 1f, 1f));
        _presenter.Open();

        Basis basis = _jobs.Cameras[0].Transform.Basis;

        Assert.True(basis.X.IsEqualApprox(Vector3.Right));
        Assert.Equal(new Vector3(2f, 3f, 4f), _jobs.Cameras[0].Transform.Origin);
    }

    /// <summary>
    /// A camera is part of the document, so moving one is a change like any other
    /// and the picture taken before it says so.
    /// </summary>
    [Fact]
    public void MovingTheCameraMarksThePictureStale()
    {
        _presenter.Open();

        Assert.False(_presenter.IsStale);

        _context.Scene.SetTransform(
            _context.Scene.ActiveCameraId,
            TransformChannel.Position,
            new Vector3(9f, 9f, 9f));

        Assert.True(_presenter.IsStale);
    }

    [Fact]
    public void TickingDrivesTheRenderAndAimsItWhereTheViewerIsLooking()
    {
        _card.PriorityRegion = new RenderRegion(8, 16, 32, 64);

        _presenter.Open();
        _presenter.Tick(0.016);

        Assert.Equal(1, _jobs.Last!.Ticks);
        Assert.Equal(new RenderRegion(8, 16, 32, 64), _jobs.Last.Region);
    }

    private Guid Camera(string name, Vector3 position)
    {
        Guid id = _context.Scene.CreateElement(_context.Scene.Id, ComponentKind.Camera, name);

        _context.Scene.SetTransform(id, TransformChannel.Position, position);

        return id;
    }

    private CameraLens Lens()
    {
        _context.Scene.TryGetElement(_context.Scene.ActiveCameraId, out IHierarchyElement camera);

        return ((ICamera)camera.Component!).Lens;
    }
}
