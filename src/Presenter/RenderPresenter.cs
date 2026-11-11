using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Commands;
using VoxelCAD.Model;
using VoxelCAD.Preferences;
using VoxelCAD.Render;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Drives the render form: compiles the document when the card is opened, starts
/// a render, and shows the picture as it arrives.
/// </summary>
/// <remarks>
/// What a frame spends here is one submission to the device; the batch before it
/// was waited for at the top of the same call, by which time the card had had a
/// whole frame to finish it. It used to submit and wait in one breath, which made
/// every frame of a render wait on the card with nothing else to do.
/// <para>
/// A render is a <em>snapshot</em>. It is compiled from the document as it stood
/// when the card was opened and does not follow later edits — which is what a
/// picture is, and why nothing here subscribes to the model.
/// </para>
/// </remarks>
internal sealed class RenderPresenter : PresenterBase
{
    private readonly IRenderView _view;
    private readonly ISceneView _scene;
    private readonly CommandContext _context;
    private readonly IDialogService _dialogs;
    private readonly IEditorPreferences _preferences;
    private readonly IRenderJobs _jobs;

    private IRenderJob? _job;
    private ImageTexture? _texture;
    private RenderDials _dials;
    private RenderProgress _progress = RenderProgress.None;
    private string _message = string.Empty;

    /// <summary>
    /// The scene this picture is of, while the card is open. Held so the
    /// subscription can be dropped again: the presenter outlives the document, and
    /// a handler left on a scene nobody has open would be a picture reacting to
    /// edits it is not a picture of.
    /// </summary>
    private IScene? _watched;
    private IProject? _watchedProject;

    private bool _stale;

    public RenderPresenter(
        IRenderView view,
        ISceneView scene,
        CommandContext context,
        IDialogService dialogs,
        IEditorPreferences preferences,
        IRenderJobs jobs)
    {
        _view = view;
        _scene = scene;
        _context = context;
        _dialogs = dialogs;
        _preferences = preferences;
        _jobs = jobs;
        _dials = preferences.RenderDials;

        _view.CloseRequested += OnCloseRequested;
        _view.DialMoved += OnDialMoved;
        _view.RenderAgainRequested += OnRenderAgainRequested;
        _view.SaveRequested += OnSaveRequested;
        _view.SizeChanged += OnSizeChanged;
        _view.FocusPicked += OnFocusPicked;
        _view.DialsFolded += OnDialsFolded;
    }

    /// <summary>Raises the card and starts a render at once — nobody opens it to wait.</summary>
    /// <remarks>
    /// A picture is taken through a camera, so asking for one in a scene that has
    /// none places one where the viewport is looking — one undoable record, and a
    /// row in the tree that can be aimed afterwards. The alternative was refusing
    /// until somebody had made one, which is a two-step first render for no gain:
    /// the camera it would ask for is exactly the camera this makes.
    /// </remarks>
    public void Open()
    {
        if (_view.IsOpen)
            return;

        PlaceCameraIfThereIsNone();

        // What the form was left at last time, whenever that was: a render setup
        // is a thing somebody arrived at, and losing it on Close would make every
        // second render start from the beginning.
        _dials = _preferences.RenderDials;

        _view.Open(_dials, _preferences.RenderCardSize, _preferences.RenderDialsOpen);
        Start();
    }

    /// <summary>
    /// Whether the card is up, which is what the command surface asks before it
    /// answers anything about a render.
    /// </summary>
    public bool IsOpen => _view.IsOpen;

    /// <summary>How the open render is going, or nothing when there is not one.</summary>
    public RenderProgress Progress => _job?.Progress ?? RenderProgress.None;

    public RenderDials Dials => _dials;

    /// <summary>What the render had to admit about the document, for a reader.</summary>
    public string Message => _stale
        ? (string.IsNullOrEmpty(_message) ? StaleReport : _message + " " + StaleReport)
        : _message;

    /// <summary>Whether the document has moved since this picture was compiled.</summary>
    public bool IsStale => _stale;

    /// <summary>
    /// One frame of the render, and whatever picture came of it. What the frame
    /// actually spends here is a submission; the batch before it was waited for at
    /// the top, by which time the card had had a whole frame to finish it.
    /// </summary>
    public void Tick(double delta)
    {
        if (_job is null || !_view.IsOpen)
            return;

        // Where the viewer is looking, so the next samples go there. Asked every
        // frame and sent through unconditionally: the job drops a region that has
        // not moved, which is the one place that comparison belongs.
        RenderRegion region = _view.PriorityRegion;

        _job.SetPriorityRegion(region.X, region.Y, region.Width, region.Height);
        _job.Tick();

        _progress = _job.Progress;

        _view.ShowProgress(_progress);

        if (!_job.TryTakeImage(out byte[] pixels, out int imageWidth, out int imageHeight))
            return;

        Show(pixels, imageWidth, imageHeight);
    }

    protected override void OnDispose()
    {
        _view.CloseRequested -= OnCloseRequested;
        _view.DialMoved -= OnDialMoved;
        _view.RenderAgainRequested -= OnRenderAgainRequested;
        _view.SaveRequested -= OnSaveRequested;
        _view.SizeChanged -= OnSizeChanged;
        _view.FocusPicked -= OnFocusPicked;
        _view.DialsFolded -= OnDialsFolded;

        Stop();
    }

    private void Start()
    {
        Stop();

        _dials = _view.Dials;

        _preferences.ApplyRenderDials(_dials);

        if (!SceneCamera.TryRead(
                _context.Scene, out CameraView camera, out CameraLens lens, out string refusal))
        {
            _message = refusal;

            _view.SetFocusAvailable(false);
            _view.ShowMessage(_message);

            return;
        }

        _view.SetFocusAvailable(lens.Aperture > 0f);

        RenderScene scene = RenderFactory.CompileScene(_context.Project, _context.Scene);
        RenderSettings settings = ToSettings(_dials, lens);

        var fields = new Dictionary<string, FinishHeightField>();

        foreach (RenderFacture facture in scene.Factures)
            fields[facture.Name] = FinishHeights.Get(facture.Name, facture.Finish);

        if (!_jobs.TryStart(
                scene,
                settings,
                camera,
                fields,
                out IRenderJob? job,
                out string error))
        {
            _message = $"The render could not be started: {error}";
            _view.ShowMessage(_message);

            return;
        }

        _job = job;
        _progress = RenderProgress.None;
        _message = Describe(scene);
        _stale = false;

        // A render is a snapshot: it is compiled from the document as it stood and
        // does not follow later edits. That is what a picture is — but a picture
        // that has quietly stopped matching the model is worth saying out loud,
        // which is all this does. It does not restart: throwing a render away on a
        // keystroke would be worse than a line of text.
        Watch(_context.Scene, _context.Project);

        _view.ShowSize(settings.Width, settings.Height);
        _view.ShowMessage(_message);
        _view.ShowProgress(RenderProgress.None);
    }

    private void Stop()
    {
        _job?.Dispose();
        _job = null;
        _progress = RenderProgress.None;

        Watch(null, null);
    }

    /// <summary>
    /// Follows one scene, or none. The old subscription goes first whatever
    /// happens next, which is the rule a rebuild in this codebase already keeps:
    /// drop everything you own before deciding whether to take anything.
    /// </summary>
    private void Watch(IScene? scene, IProject? project)
    {
        if (_watched is not null)
            _watched.Changed -= OnSceneChanged;

        if (_watchedProject is not null)
            _watchedProject.Changed -= OnProjectChanged;

        _watched = scene;
        _watchedProject = project;

        if (_watched is not null)
            _watched.Changed += OnSceneChanged;

        // The project as well as the scene, because a material is not in a scene
        // and recolouring one changes every picture that wears it — a render that
        // only watched the tree would go on showing the old colour and say nothing.
        if (_watchedProject is not null)
            _watchedProject.Changed += OnProjectChanged;
    }

    private void OnProjectChanged(ProjectChange change) => NoteStale();

    private void OnSceneChanged(SceneChange change) => NoteStale();

    private void NoteStale()
    {
        if (_stale || _job is null)
            return;

        _stale = true;

        _view.ShowMessage(string.IsNullOrEmpty(_message)
            ? StaleNote
            : _message + " " + StaleNote);
    }

    /// <summary>
    /// Hands the bytes the last batch produced to a texture. Bytes rather than an
    /// <c>Image</c> across that seam because the tracer answers in what a device
    /// gives back, and building the engine object is the drawing side's job.
    /// </summary>
    private void Show(byte[] pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height * 4)
            return;

        Image image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);

        // Rebuilt only when the shape changes: updating in place keeps the card
        // from replacing a texture the form is holding many times a render.
        if (_texture is null
            || _texture.GetWidth() != width
            || _texture.GetHeight() != height)
        {
            _texture = ImageTexture.CreateFromImage(image);
            _view.ShowImage(_texture);
        }
        else
        {
            _texture.Update(image);
        }
    }

    /// <summary>Puts the card away, as its own Close button does.</summary>
    public void Close()
    {
        if (!_view.IsOpen)
            return;

        Stop();
        _view.Close();
    }

    /// <summary>
    /// Sets what the card is a picture of, from somewhere other than the fields.
    /// </summary>
    /// <remarks>
    /// It writes the form and then takes the ordinary dial path, rather than
    /// reaching the renderer directly, so a dial set over the command surface and
    /// one turned by hand cannot come to mean different things: the same
    /// <see cref="RenderRestart"/> decides whether it re-grades or starts again,
    /// and the fields do the same clamping and the same snapping to their own
    /// step either way. Which is also why the answer is read back off the form —
    /// what was asked for and what the card can hold are not always the same
    /// number.
    /// </remarks>
    public void ApplyDials(RenderDials dials)
    {
        if (!_view.IsOpen)
            return;

        _view.ShowDials(dials);

        OnDialMoved();
    }

    /// <summary>
    /// Takes the picture again, as the button does. The only way to get a second
    /// picture of a scene that has moved — a render is compiled from the document
    /// as it stood — and it keeps the viewer where it is, which closing and
    /// reopening the card does not.
    /// </summary>
    public void RenderAgain() => OnRenderAgainRequested();

    /// <summary>
    /// Aims the viewer, which is not only a display change: what is on screen is
    /// what the renderer traces first, so this decides where the next samples go.
    /// </summary>
    public void ShowRegion(RenderRegion region) => _view.ShowRegion(region);

    public void FitImage() => _view.FitImage();

    public void ShowActualSize() => _view.ShowActualSize();

    /// <summary>What the card is looking at, in the picture's own pixels.</summary>
    public RenderRegion Region => _view.PriorityRegion;

    /// <summary>
    /// The picture as it stands, or null when there is not one yet. What the card
    /// is showing rather than a finished render: a render is the same render
    /// however long it is left to run, so a reader looking early gets a noisier
    /// picture of it and never a different one.
    /// </summary>
    public Image? ReadPicture()
    {
        if (_job is null || !_job.TryPeekImage(out byte[] pixels, out int width, out int height))
            return null;

        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
    }

    /// <summary>
    /// Focuses the lens on whatever the picture shows at a point. The distance is
    /// the one the render already measured for that pixel, so what is focused on
    /// is exactly what is drawn there rather than what a second trace would find.
    /// </summary>
    /// <returns>False where the picture shows nothing at that point.</returns>
    public bool FocusAt(int x, int y)
    {
        if (_job is null || !_job.TryReadDepth(x, y, out float distance))
            return false;

        if (!SceneCamera.TryReadLens(_context.Scene, out Guid id, out CameraLens lens))
            return false;

        // The camera's lens, so it is an edit to the document and one record on the
        // history — which is what focusing is now that a lens belongs to the camera
        // rather than to the application. It restarts rather than re-grades: focus
        // decides what the light does.
        _context.Scene.SetCameraLens(id, lens with { FocusDistance = distance });

        Start();

        return true;
    }

    private void OnFocusPicked(int x, int y)
    {
        if (FocusAt(x, y))
            _view.SetPickingFocus(false);
    }

    private void OnSizeChanged(Vector2 size) => _preferences.ApplyRenderCardSize(size);

    private void OnDialsFolded(bool open) => _preferences.ApplyRenderDialsOpen(open);

    private void OnCloseRequested()
    {
        Stop();
        _view.Close();
    }

    /// <summary>
    /// The button. A render is compiled from the document as it stood when it
    /// started, so this is the only way to take a second picture of a scene that
    /// has moved since — and it restarts whatever the dials say, which is what it
    /// was silently not doing while it shared an event with them.
    /// </summary>
    private void OnRenderAgainRequested()
    {
        if (_view.IsOpen)
            Start();
    }

    /// <summary>
    /// A dial moved. Most of them describe what is being traced and mean starting
    /// again; two do not, and pretending otherwise would throw away a finished
    /// render for a change that costs one pass over a buffer.
    /// </summary>
    private void OnDialMoved()
    {
        if (!_view.IsOpen)
            return;

        RenderDials wanted = _view.Dials;

        if (_job is not null && !RenderRestart.IsNeeded(_dials, wanted))
        {
            // Exposure is applied when the picture is read, and asking for more
            // samples is asking the same render to carry on.
            _job.SetExposure(wanted.Exposure);
            _job.SetSamplesWanted(wanted.Samples);

            _dials = wanted;

            _preferences.ApplyRenderDials(_dials);

            return;
        }

        Start();
    }

    private async void OnSaveRequested()
    {
        // What is on the card is what was asked to be kept: read before the
        // dialog, not after, since a render carries on accumulating while a picker
        // is up.
        if (_job is null || !_job.TryPeekImage(out byte[] pixels, out int width, out int height))
            return;

        Image image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);

        string? path = await _dialogs.AskPathAsync(new FileDialogRequest(
            FileDialogMode.Save,
            "Save the render",
            new List<FileDialogFilter> { new("*.png", "PNG image") },
            "render.png"));

        if (!IsAlive || string.IsNullOrEmpty(path))
            return;

        Error error = image.SavePng(path);

        _message = error == Error.Ok
            ? $"Written to {path}"
            : $"Could not write {path}: {error}.";

        _view.ShowMessage(_message);
    }

    /// <summary>
    /// What the card says once the model has moved under a finished picture — the
    /// short half of it. A status bar is a few words beside a button, and the
    /// button it stands beside says the rest: the sentence reads "the model has
    /// changed … render again".
    /// </summary>
    private const string StaleNote = "The model has changed";

    /// <summary>
    /// The same fact for a reader with no button in front of it. Both are named
    /// rather than inline because the card and <c>render-status</c> both say this,
    /// and two wordings drifting apart is the thing one constant prevents — what
    /// differs is only how much of the sentence the surface has to supply.
    /// </summary>
    private const string StaleReport =
        "The model has changed since this render started — press Render again for a "
        + "picture of it as it stands.";

    /// <summary>
    /// Places a camera where the viewport is looking, as one record, when the scene
    /// holds none. Through <see cref="IScene.CreateCamera"/> rather than a create
    /// followed by three edits: a shot arriving over four history entries is four
    /// things to undo, and each of those edits would be quantised on the way in.
    /// </summary>
    private void PlaceCameraIfThereIsNone()
    {
        if (_context.Scene.ActiveCameraId != Guid.Empty)
            return;

        CameraView view = _scene.GetCameraView();
        Transform3D stand = Mathf.IsZeroApprox(view.Transform.Basis.Determinant())
            ? Transform3D.Identity
            : view.Transform;

        TransformValues values = TransformHelper.ToValues(stand);

        _context.Scene.CreateCamera(
            _context.Scene.Id,
            CameraName,
            values.Position,
            values.RotationDegrees,
            (CameraLens.Default with { FovDegrees = view.FovDegrees }).Clamped());
    }

    /// <summary>
    /// What a camera placed for somebody is called. Not composed against the names
    /// already in the scene: this only ever runs where there is no camera at all.
    /// </summary>
    private const string CameraName = "Camera";

    private static RenderSettings ToSettings(RenderDials dials, CameraLens lens) =>
        (RenderSettings.Default with
        {
            Width = dials.Width,
            Height = dials.Height,
            Samples = dials.Samples,
            Exposure = dials.Exposure,
            SunElevationDegrees = dials.SunElevationDegrees,
            SunAzimuthDegrees = dials.SunAzimuthDegrees,
            SunAngularRadiusDegrees = dials.SunSizeDegrees,
            Glare = dials.Glare,

            // The lens is the camera's, and the film is the card's. Everything
            // above came off a dial; everything here came out of the document.
            Aperture = lens.Aperture,
            FocusDistance = lens.FocusDistance,
            Projection = lens.Projection,
            OrthographicHeight = lens.OrthographicHeight,
            Backdrop = dials.Backdrop,
            GroundEnabled = dials.Ground
        }).Clamped();

    /// <summary>
    /// What the render could not say about the document. Empty when there is
    /// nothing to admit, which is the common case.
    /// </summary>
    private static string Describe(RenderScene scene)
    {
        if (scene.IsEmpty)
            return "Nothing visible to render: the picture is the sky and the floor.";

        var partsList = new List<string>();

        if (scene.OversizedElements.Count > 0)
        {
            partsList.Add(
                "Left out as too sparse to hold densely: "
                + string.Join(", ", scene.OversizedElements) + ".");
        }

        return string.Join(" ", partsList);
    }
}
