using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Preferences;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Owns the session and the open document, and handles the operations that belong
/// to no single panel — new, open, save, close.
/// </summary>
/// <remarks>
/// It must never relay messages between panels — that is how a shell turns into
/// a god object. Panels coordinate through <see cref="EditorSession"/>; anything
/// two of them need to agree on is state, and belongs there.
/// <para>
/// Which document is open is <see cref="CommandContext"/>'s answer rather than a
/// field here, so the user's File menu and an agent's <c>open</c> are the same
/// event arriving from two directions, and neither can leave the other looking at
/// a project that is no longer loaded.
/// </para>
/// </remarks>
internal sealed class ShellPresenter : PresenterBase, IEditorShell
{
    /// <summary>The shipped sample a first launch opens; see <c>assets/samples</c>.</summary>
    internal const string FirstRunSample = "castle";

    /// <summary>
    /// What a material starts out looking like. A UI default rather than a model
    /// one: the model takes whatever colour it is given.
    /// </summary>
    private static readonly Color NewMaterialColor = new(0.8f, 0.8f, 0.8f);

    private readonly IShellView _view;
    private readonly EditorSession _session = new();
    private readonly ControlArbiter _arbiter = new();
    private readonly CommandContext _context = new();
    private readonly ICommandDispatcher _commands;
    private readonly ControlPresenter _control;
    private readonly AutosavePresenter _autosave;
    private readonly SettingsPresenter _settings;
    private readonly RenderPresenter _render;
    private readonly IEditorPreferences _preferences;

    private DocumentPresenter? _document;
    private IScene? _boundScene;
    private bool _operationRunning;

    /// <summary>
    /// Whether the launch has asked about the snapshots earlier sessions left.
    /// Asked on the first tick rather than in the constructor: a question needs a
    /// window to stand in, and the constructor runs before there is one.
    /// </summary>
    private bool _recoveryOffered;

    /// <summary>
    /// The sample a first launch opened, until the first tick has framed it. The
    /// view a new document starts on is pitched at an empty room's floor, and the
    /// sample stands taller than that — the camera would start inside a tower.
    /// </summary>
    private IProject? _firstRunProject;

    public ShellPresenter(IShellView view, IEditorPreferences preferences)
        : this(view, preferences, new AutosaveStore(preferences.AutosaveDirectory))
    {
    }

    /// <summary>
    /// The store is a parameter so a test can hand in one with a hand on its
    /// worker; the editor builds the ordinary one from the preferences.
    /// </summary>
    internal ShellPresenter(IShellView view, IEditorPreferences preferences, AutosaveStore autosaveStore)
    {
        _view = view;
        _preferences = preferences;

        // The user's own commands over the same document, ungated because the user
        // always holds control. Export runs the user through this, the same surface
        // an agent drives — the GUI adds no export logic of its own.
        _commands = CommandFactory.CreateDispatcher(_context);

        // A first launch opens a sample rather than an empty room, so the first thing
        // a new user meets is a model to turn round, take apart and render. It
        // arrives untitled and clean: closing it asks nothing, and saving asks where.
        IProject project = preferences.IsFirstRun
            ? ModelFactory.OpenSample(FirstRunSample)
            : ModelFactory.CreateProject();

        if (preferences.IsFirstRun)
            _firstRunProject = project;

        // Both of these outlive the document: preferences are not part of a
        // project, and a handover in progress is not interrupted by opening one.
        _control = new ControlPresenter(_arbiter, view, view.Scene, project.History, preferences);

        // The recovery snapshot outlives documents too: it is bound to each one as
        // it opens and keeps what an agent's 'new' would otherwise throw away.
        _autosave = new AutosavePresenter(
            autosaveStore,
            _context,
            _session,
            _arbiter,
            view,
            preferences);
        _settings = new SettingsPresenter(view.Settings, preferences, _autosave.DescribeStatusLine);

        // Outlives documents like the two above it: a render is a picture of
        // whatever was open when it was asked for, and the card is not a panel.
        _render = new RenderPresenter(
            view.Render,
            view.Scene,
            _context,
            view,
            preferences,
            RenderJobs.Default);

        _context.ProjectChanged += OnProjectChanged;
        _context.ProjectSaved += OnProjectSaved;
        _context.ActiveSceneChanged += OnActiveSceneChanged;

        _view.NewRequested += OnNewRequested;
        _view.OpenRequested += OnOpenRequested;
        _view.RecoverRequested += OnRecoverRequested;
        _autosave.PendingChanged += RefreshRecoverState;
        _autosave.StatusChanged += _settings.RefreshAutosaveStatus;
        _view.SaveRequested += OnSaveRequested;
        _view.SaveAsRequested += OnSaveAsRequested;
        _view.ImportRequested += OnImportRequested;
        _view.ExportRequested += OnExportRequested;
        _view.CloseRequested += OnCloseRequested;
        _view.UndoRequested += OnUndoRequested;
        _view.RedoRequested += OnRedoRequested;
        _view.CreateRequested += OnCreateRequested;
        _view.FilesDropped += OnFilesDropped;
        _view.ReferencesVisibleRequested += OnReferencesVisibleRequested;
        _session.ReferencesVisibleChanged += PushReferencesVisible;
        _view.DeleteSelectionRequested += OnDeleteSelectionRequested;
        _view.CopyRequested += OnCopyRequested;
        _view.CutRequested += OnCutRequested;
        _view.PasteRequested += OnPasteRequested;
        _view.DuplicateRequested += OnDuplicateRequested;
        _view.ReplaceMaterialUsesRequested += OnReplaceMaterialUsesRequested;
        _view.MaterialChoices = CollectMaterialChoices;
        _view.CanReplaceMaterialUses = CanReplaceMaterial;
        _view.PreferencesRequested += OnPreferencesRequested;
        _view.RenderRequested += OnRenderRequested;
        _view.PanelLayoutEdited += OnPanelLayoutEdited;
        _view.PanelWidthsEdited += OnPanelWidthsEdited;
        _session.SelectionChanged += RefreshCommandState;
        _preferences.Changed += OnPreferencesChanged;

        // Preferences outlive documents, so what they say about the chrome is
        // applied once here and then on every change, rather than being rebuilt
        // with the document like everything below.
        ApplyDisplayPreferences();

        // The first document arrives the same way every later one does.
        _context.SetProject(project, null);

        RefreshRecoverState();
    }

    /// <summary>
    /// The dispatchers an agent transport drives — gated on control, and carrying
    /// the commands that negotiate it. Everything they do lands in the same
    /// project, history and undo the user sees.
    /// </summary>
    /// <remarks>
    /// One per client rather than one for everybody, because control is exclusive to
    /// an agent and not merely to "an agent": the commands that negotiate it have to
    /// know which one is calling. See <see cref="AgentDispatcherProvider"/>.
    /// </remarks>
    public IAgentDispatcherProvider CreateAgentDispatchers() =>
        new AgentDispatcherProvider(_arbiter, _control, _session, BuildAgentDispatcher);

    /// <summary>
    /// The whole agent surface for one client: the document commands, the host
    /// commands that need a window, and the control commands bound to that client.
    /// </summary>
    private ICommandDispatcher BuildAgentDispatcher(
        IReadOnlyList<CommandDefinition> controlCommands)
    {
        var hostCommands = new List<CommandDefinition>(controlCommands);
        hostCommands.AddRange(CameraCommands.Create(_view.Scene));
        hostCommands.AddRange(ReferenceSwitchCommands.Create(_session));
        hostCommands.AddRange(InterfaceCommands.Create(_view.Scene, _view.UiDriver));
        hostCommands.AddRange(RenderCommands.Create(_render));
        hostCommands.AddRange(InputCommands.Create(_view.UiDriver, _preferences));
        hostCommands.AddRange(PreferencesCommands.Create(_preferences));
        hostCommands.AddRange(AutosaveCommands.Create(_autosave));

        return CommandFactory.CreateDispatcher(_context, hostCommands);
    }

    public void Tick(double delta)
    {
        _control.Tick(delta);
        _render.Tick(delta);
        _autosave.Tick(delta);
        _settings.Tick(delta);

        // On a tick rather than in the constructor: the camera has no window to fit
        // anything to until there is one. And only if the sample is still what is
        // open — an agent may have replaced it in the frame before.
        if (_firstRunProject != null)
        {
            if (_context.Project == _firstRunProject)
                _view.Scene.FocusOn(_context.Scene.Id);

            _firstRunProject = null;
        }

        // Once, and through the same gate every file operation runs behind, so
        // the launch card cannot stack on a question somebody else is asking.
        if (!_recoveryOffered && !_operationRunning)
        {
            _recoveryOffered = true;
            OnRecoverRequested();
        }
    }

    /// <summary>
    /// The endpoint went down under the shell. Dropping the handover is the
    /// arbiter's to do — the control presenter reacts to it and clears the banner —
    /// so the shell only forwards the news.
    /// </summary>
    public void ReleaseAgentControl() => _control.Abandon();

    public void ReportAgentEndpoint(string status) => _settings.SetEndpointStatus(status);

    protected override void OnDispose()
    {
        _context.ProjectChanged -= OnProjectChanged;
        _context.ProjectSaved -= OnProjectSaved;
        _context.ActiveSceneChanged -= OnActiveSceneChanged;

        _view.NewRequested -= OnNewRequested;
        _view.OpenRequested -= OnOpenRequested;
        _view.RecoverRequested -= OnRecoverRequested;
        _autosave.PendingChanged -= RefreshRecoverState;
        _autosave.StatusChanged -= _settings.RefreshAutosaveStatus;
        _view.SaveRequested -= OnSaveRequested;
        _view.SaveAsRequested -= OnSaveAsRequested;
        _view.ImportRequested -= OnImportRequested;
        _view.ExportRequested -= OnExportRequested;
        _view.CloseRequested -= OnCloseRequested;
        _view.UndoRequested -= OnUndoRequested;
        _view.RedoRequested -= OnRedoRequested;
        _view.CreateRequested -= OnCreateRequested;
        _view.FilesDropped -= OnFilesDropped;
        _view.ReferencesVisibleRequested -= OnReferencesVisibleRequested;
        _session.ReferencesVisibleChanged -= PushReferencesVisible;
        _view.DeleteSelectionRequested -= OnDeleteSelectionRequested;
        _view.CopyRequested -= OnCopyRequested;
        _view.CutRequested -= OnCutRequested;
        _view.PasteRequested -= OnPasteRequested;
        _view.DuplicateRequested -= OnDuplicateRequested;
        _view.ReplaceMaterialUsesRequested -= OnReplaceMaterialUsesRequested;

        // The view outlives this presenter, and an answer given after it is gone
        // would be about a document nobody has open.
        _view.MaterialChoices = null;
        _view.CanReplaceMaterialUses = null;
        _view.PreferencesRequested -= OnPreferencesRequested;
        _view.RenderRequested -= OnRenderRequested;
        _view.PanelLayoutEdited -= OnPanelLayoutEdited;
        _view.PanelWidthsEdited -= OnPanelWidthsEdited;
        _session.SelectionChanged -= RefreshCommandState;
        _preferences.Changed -= OnPreferencesChanged;

        UnbindDocument();
        _autosave.Dispose();
        _settings.Dispose();
        _render.Dispose();
        _control.Dispose();
    }

    // --- document lifecycle ---

    /// <summary>
    /// A different document is open, however it got there. Everything bound to the
    /// previous one is thrown away and rebuilt: a panel presenter's constructor
    /// fills its view from scratch, which is what makes rebuilding cheaper than
    /// teaching five presenters to re-bind.
    /// </summary>
    private void OnProjectChanged()
    {
        IProject project = _context.Project;

        UnbindDocument();

        // Ids from the previous document address nothing in this one.
        _session.ClearSelection();
        _session.CurrentMaterialId = project.Materials[0].Id;

        _document = new DocumentPresenter(
            project,
            _context.Scene,
            _view.Hierarchy,
            _view.Inspector,
            _view.History,
            _view.Scene,
            _session,
            _view,
            _preferences,
            _context.SetActiveScene,
            _autosave.HasPendingRecovery || ArrivedWithEdits(project));

        project.History.Changed += OnHistoryChanged;
        project.Changed += OnProjectContentChanged;
        BindScene(_document.Scene);
        _control.SetHistory(project.History);

        // Before the title: a recovered document is bound modified, and the
        // title is what says so.
        _autosave.Bind(_document);

        RefreshCommandState();
        RefreshTitle();
    }

    /// <summary>
    /// Whether this document turned up already carrying work that nothing on disk
    /// matches — so it is born modified, and closing it asks.
    /// </summary>
    /// <remarks>
    /// A document created or loaded has an empty history and starts clean; one
    /// opened from somebody else's voxel file arrives with the import already in
    /// it and no path of its own, because adopting the path would write this
    /// editor's format over their file. A recovered snapshot is the other half of
    /// the same idea and keeps its own flag: it does have a path, and is modified
    /// all the same.
    /// </remarks>
    private bool ArrivedWithEdits(IProject project) =>
        _context.Path == null && project.History.Records.Count > 0;

    /// <summary>
    /// Another scene of the same document is now the one being worked on — the
    /// user activated its row, an agent asked for it, or the one before it was
    /// deleted out from under everybody.
    /// </summary>
    private void OnActiveSceneChanged()
    {
        if (_document is not { } document)
            return;

        UnbindScene();
        document.SetActiveScene(_context.Scene);
        BindScene(document.Scene);

        RefreshCommandState();
    }

    private void OnProjectSaved()
    {
        _document?.MarkSaved();
        _autosave.OnSaved();
        RefreshTitle();
    }

    private void UnbindDocument()
    {
        if (_document == null)
            return;

        // First, while the document is still whole: unsaved work that moved since
        // the last snapshot is written on the way out.
        _autosave.Unbind();

        _document.Project.History.Changed -= OnHistoryChanged;
        _document.Project.Changed -= OnProjectContentChanged;
        UnbindScene();
        _document.Dispose();
        _document = null;
    }

    /// <summary>
    /// The shell watches the active scene for one thing only: a selection whose
    /// element has stopped existing. Which scene that is changes under it, so the
    /// subscription moves with it.
    /// </summary>
    private void BindScene(IScene scene)
    {
        _boundScene = scene;
        scene.Changed += OnSceneChanged;
    }

    private void UnbindScene()
    {
        if (_boundScene != null)
            _boundScene.Changed -= OnSceneChanged;

        _boundScene = null;
    }

    // --- file operations ---

    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync())
            return;

        _context.SetProject(ModelFactory.CreateProject(), null);
    }

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync())
            return;

        var filtersList = new List<FileDialogFilter>
        {
            new(ProjectFormat.Filter, ProjectFormat.Label)
        };

        // Somebody else's voxel file opens too, as a new document read from it.
        // Offering it here rather than only under Import is what every editor
        // does, and it is the gesture somebody arriving from MagicaVoxel reaches
        // for first.
        filtersList.AddRange(GetImportFilters());

        string? path = await _view.AskPathAsync(new FileDialogRequest(
            FileDialogMode.Open,
            "Open project",
            filtersList,
            _context.Path ?? string.Empty));

        if (!IsAlive || path == null)
            return;

        // A voxel file is opened by the command surface, which starts a document
        // sized to what arrived and leaves it untitled. Same command the console
        // runs, so the two routes cannot come to disagree about what a damaged
        // file does.
        CommandResult opened = _commands.Execute(
            "open", new Dictionary<string, string> { ["path"] = path });

        if (!opened.Ok)
        {
            // The path names the file the user picked, which is worth saying back:
            // a picker can be pointed at something that has moved since it was
            // last opened, and the reason alone does not say which file it is.
            _view.ShowError(
                "Cannot open",
                $"{path}\n\n{opened.Error ?? "The file did not open."}");
        }
    }

    private Task<bool> SaveAsync()
    {
        if (_document is not { } document)
            return Task.FromResult(false);

        return _context.Path is { } path
            ? Task.FromResult(TrySave(document, path))
            : SaveAsAsync();
    }

    private async Task<bool> SaveAsAsync()
    {
        if (_document is not { } document)
            return false;

        string? path = await _view.AskPathAsync(new FileDialogRequest(
            FileDialogMode.Save,
            "Save project as",
            new[] { new FileDialogFilter(ProjectFormat.Filter, ProjectFormat.Label) },
            _context.Path ?? string.Empty));

        // The document can be swapped from under a dialog — an agent's open runs
        // on the same project the picker was raised for.
        if (path == null || !IsCurrent(document))
            return false;

        return TrySave(document, EnsureExtension(path));
    }

    private bool TrySave(DocumentPresenter document, string path)
    {
        try
        {
            document.Project.Save(path);
        }
        catch (Exception exception)
        {
            ShowFailure("Cannot save the project", path, exception);
            return false;
        }

        _context.MarkSaved(path);

        return true;
    }

    private async Task CloseAsync()
    {
        if (!await ConfirmDiscardAsync())
            return;

        _view.Close();
    }

    /// <summary>
    /// Reads somebody else's voxel file into the open document, beside whatever
    /// is already there. One history record, so a user who did not mean it has
    /// one undo to press. The formats offered are the registry's, read through
    /// <c>import-formats</c>, so the GUI adds no knowledge of what can be read.
    /// </summary>
    private async Task ImportAsync()
    {
        if (_document is not { } document)
            return;

        IReadOnlyList<FileDialogFilter> filters = GetImportFilters();

        if (filters.Count == 0)
        {
            _view.ShowError("Cannot import", "No import formats are available.");
            return;
        }

        string? path = await _view.AskPathAsync(new FileDialogRequest(
            FileDialogMode.Open,
            "Import model",
            filters,
            string.Empty));

        // The document can be swapped from under the picker, the same way a save
        // can — and an import that landed in the wrong one would be worse than
        // one that did not happen.
        if (path == null || !IsCurrent(document))
            return;

        CommandResult result = _commands.Execute(
            "import",
            new Dictionary<string, string> { ["path"] = path });

        if (!result.Ok)
            _view.ShowError("Cannot import", result.Error ?? "The import did not complete.");
    }

    /// <summary>
    /// Picks a path and a format, then runs the <c>export</c> command — the same one
    /// an agent runs. The formats offered are the registry's, read through
    /// <c>export-formats</c>, so the GUI adds no knowledge of what can be written.
    /// </summary>
    private async Task ExportAsync()
    {
        if (_document is not { } document)
            return;

        IReadOnlyList<FileDialogFilter> filters = GetExportFilters();

        if (filters.Count == 0)
        {
            _view.ShowError("Cannot export", "No export formats are available.");
            return;
        }

        string? path = await _view.AskPathAsync(new FileDialogRequest(
            FileDialogMode.Save,
            "Export model",
            filters,
            SuggestExportPath()));

        // The document can be swapped from under the picker, the same way a save can.
        if (path == null || !IsCurrent(document))
            return;

        CommandResult result = _commands.Execute(
            "export",
            new Dictionary<string, string> { ["path"] = path });

        if (!result.Ok)
            _view.ShowError("Cannot export", result.Error ?? "The export did not complete.");
    }

    /// <summary>
    /// The registry's formats as dialog filters, read off the command surface so the
    /// shell never learns what an exporter can produce.
    /// </summary>
    private IReadOnlyList<FileDialogFilter> GetImportFilters() => GetFilters("import-formats");

    private IReadOnlyList<FileDialogFilter> GetExportFilters() => GetFilters("export-formats");

    /// <summary>
    /// What a picker offers, asked of the command surface rather than of the
    /// format layer directly: the shell is not entitled to say what this editor
    /// can read or write.
    /// </summary>
    private IReadOnlyList<FileDialogFilter> GetFilters(string command)
    {
        CommandResult result = _commands.Execute(command, new Dictionary<string, string>());
        var filtersList = new List<FileDialogFilter>();

        if (result.Value is not IEnumerable<object?> rows)
            return filtersList;

        foreach (object? row in rows)
        {
            if (row is IReadOnlyDictionary<string, object?> fields
                && fields.TryGetValue("extension", out object? extension) && extension is string ext
                && fields.TryGetValue("label", out object? label) && label is string name)
            {
                filtersList.Add(new FileDialogFilter("*" + ext, name));
            }
        }

        return filtersList;
    }

    /// <summary>
    /// The project's own name without its extension, so the chosen format's filter
    /// settles the new one. Empty for a document never written to disk.
    /// </summary>
    private string SuggestExportPath() =>
        _context.Path is { } path ? Path.ChangeExtension(path, null) : string.Empty;

    /// <summary>
    /// Whether it is all right to lose what is open. False also when this
    /// presenter or its document did not survive the question — the caller wants
    /// to stop either way.
    /// </summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (_document is not { } document)
            return IsAlive;

        if (!document.IsModified)
            return true;

        ConfirmResult result = await _view.AskConfirmAsync(new ConfirmRequest(
            "Unsaved changes",
            $"Save the changes to {GetDocumentName()} first?",
            "Save",
            "Discard",
            "Cancel"));

        if (!IsCurrent(document))
            return false;

        return result switch
        {
            ConfirmResult.Confirm => await SaveAsync(),
            ConfirmResult.Deny => Forget(document),
            _ => false
        };
    }

    /// <summary>
    /// The user said Discard: the recovery snapshot goes with the work, at the
    /// moment it is said. A decision made stands even if the operation it was
    /// made for is cancelled at the next picker.
    /// </summary>
    private bool Forget(DocumentPresenter document)
    {
        if (!IsCurrent(document))
            return false;

        _autosave.Forget();

        return true;
    }

    /// <summary>
    /// The one guard every continuation needs: this presenter is still the live
    /// one, and it is still showing the document the operation started on.
    /// </summary>
    private bool IsCurrent(DocumentPresenter document) =>
        IsAlive && ReferenceEquals(_document, document);

    /// <summary>
    /// A save dialog can come back with a bare name. Which extension that means is
    /// the model's to say, and an unsupported one it typed out in full is left
    /// alone so the model can refuse it by name.
    /// </summary>
    private static string EnsureExtension(string path) =>
        Path.HasExtension(path) ? path : path + ProjectFormat.Extension;

    private string GetDocumentName() =>
        _context.Path is { } path ? Path.GetFileName(path) : WindowTitleHelper.UntitledName;

    private void ShowFailure(string title, string path, Exception exception) =>
        _view.ShowError(title, $"{path}\n\n{exception.Message}");

    // --- view events ---

    // Each of these is an async void boundary: an exception escaping one has no
    // caller left to catch it and would take the process down, so it stops here.

    private async void OnNewRequested() => await RunAsync(NewAsync, "New failed");

    private async void OnOpenRequested() => await RunAsync(OpenAsync, "Open failed");

    /// <summary>
    /// The launch's own question and File → Recover unsaved work… are one flow.
    /// Behind the file-operation gate like an open, because a recovery replaces
    /// the document and asks the same question first.
    /// </summary>
    private async void OnRecoverRequested() =>
        await RunAsync(() => _autosave.OfferRecoveryAsync(ConfirmDiscardAsync), "Recovery failed");

    private void RefreshRecoverState() => _view.SetRecoverEnabled(_autosave.PendingCount > 0);

    private async void OnSaveRequested() => await RunAsync(() => SaveAsync(), "Save failed");

    private async void OnSaveAsRequested() => await RunAsync(() => SaveAsAsync(), "Save failed");

    private async void OnImportRequested() => await RunAsync(ImportAsync, "Import failed");

    private async void OnExportRequested() => await RunAsync(ExportAsync, "Export failed");

    private async void OnCloseRequested() => await RunAsync(CloseAsync, "Close failed");

    /// <summary>
    /// Runs one file operation at a time. A second request while a dialog is up
    /// would stack a second dialog on top of it and ask the same question twice.
    /// The operation is passed unstarted, so a refused one never begins.
    /// </summary>
    private async Task RunAsync(Func<Task> operation, string failureTitle)
    {
        if (_operationRunning)
            return;

        _session.EndGesture();
        _operationRunning = true;

        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            if (IsAlive)
                _view.ShowError(failureTitle, exception.ToString());
        }
        finally
        {
            _operationRunning = false;
        }
    }

    /// <summary>
    /// Every one of these is the user starting a new action, and a gesture in
    /// flight has to be closed first: an open history scope goes on collecting,
    /// so this edit would otherwise be filed as part of a drag somebody else
    /// started — and an undo would step over both at once.
    /// </summary>
    private void OnUndoRequested()
    {
        _session.EndGesture();
        _document?.Project.History.Undo();
    }

    private void OnRedoRequested()
    {
        _session.EndGesture();
        _document?.Project.History.Redo();
    }

    /// <summary>
    /// Preferences reached disk. Nothing here decides whether anything actually
    /// changed: pushing the same corner again costs a relayout of one widget, and
    /// comparing would be keeping a second copy of a value the preferences already
    /// hold.
    /// </summary>
    private void OnPreferencesChanged() => ApplyDisplayPreferences();

    /// <summary>
    /// The settings that say what the chrome looks like rather than how the
    /// endpoint behaves — the theme, the accent, the pace, the cube's corner and
    /// the panel layout. The day this grows a decision of its own rather than a
    /// list of pushes, it becomes a presenter.
    /// </summary>
    private void ApplyDisplayPreferences()
    {
        // First, so the layout that follows seeds its motions knowing the pace.
        _view.SetAnimation(_preferences.AnimationsEnabled, _preferences.AnimationSpeed);
        _view.SetSkin(_preferences.Theme, _preferences.AccentColor);
        _view.SetGlassPanels(_preferences.GlassPanels);
        _view.SetViewCubeCorner(_preferences.ViewCubeCorner);
        _view.SetPanelWidths(_preferences.PanelWidths);
        _view.SetPanelLayout(_preferences.PanelPlacements);
    }

    /// <summary>
    /// A pin or fold click, already applied on screen; what is left is making it
    /// true tomorrow. The write raises <c>Changed</c>, which pushes the stored
    /// list back through <see cref="ApplyDisplayPreferences"/> — the same value,
    /// re-applied, which is the loop settling rather than a second edit.
    /// </summary>
    private void OnPanelLayoutEdited(IReadOnlyList<PanelPlacement> placements) =>
        _preferences.ApplyPanelPlacements(placements);

    /// <summary>A resize let go, on the placement edit's own terms.</summary>
    private void OnPanelWidthsEdited(PanelWidths widths) =>
        _preferences.ApplyPanelWidths(widths);

    /// <summary>
    /// The same rule as every other action the shell starts: a gesture in flight
    /// ends first, so nothing the settings form leads to is filed inside a drag
    /// somebody else began.
    /// </summary>
    private void OnPreferencesRequested()
    {
        _session.EndGesture();
        _settings.Open();
    }

    /// <summary>
    /// A render is asked for. The gesture in flight ends first for the same reason
    /// the settings form ends one: nothing that follows should be filed inside a
    /// drag somebody else began.
    /// </summary>
    private void OnRenderRequested()
    {
        _session.EndGesture();
        _render.Open();
    }

    private void OnCreateRequested(CreateKind kind)
    {
        if (_document is not { } document)
            return;

        _session.EndGesture();

        switch (kind)
        {
            case CreateKind.Group:
                CreateElement(document, ComponentKind.None, "Group");
                break;

            case CreateKind.VoxelModel:
                CreateElement(document, ComponentKind.VoxelModel, "Model");
                break;

            case CreateKind.Camera:
                CreateElement(document, ComponentKind.Camera, "Camera");
                break;

            case CreateKind.Scene:
                CreateScene(document);
                break;

            case CreateKind.Material:
                CreateMaterial(document);
                break;

            case CreateKind.PrefabInstance:
                CreateInstance(document);
                break;

            case CreateKind.Reference:
                ImportReference(document);
                break;
        }
    }

    /// <summary>
    /// Places an instance of the selected prefab in the scene being worked on, and
    /// selects it — the copy is what the user is about to move.
    /// </summary>
    /// <remarks>
    /// At the top level rather than under whatever else might be selected: what is
    /// selected here <em>is</em> the prefab, so there is nothing else the press
    /// could mean by "under".
    /// </remarks>
    private void CreateInstance(DocumentPresenter document)
    {
        if (!CanCreateInstance())
            return;

        _session.SetSelection(new SelectionTarget(
            SelectionKind.Element,
            document.Scene.CreateInstance(document.Scene.Id, _session.ActiveTarget.Id)));
    }

    /// <summary>
    /// Asks for a file and puts it in the scene as a reference. The one create that
    /// runs a picker, because a reference with no file is a placeholder standing in
    /// the scene until somebody finds the second command.
    /// </summary>
    private async void ImportReference(DocumentPresenter document)
    {
        if (!CanCreateElement())
            return;

        string? path = await _view.AskPathAsync(
            ReferenceDialogs.Open("Put a reference in the scene"));

        // A picker outlives what raised it: the document may have been closed or
        // another one opened while it was up.
        if (path == null || !IsAlive || !ReferenceEquals(_document, document))
            return;

        PlaceReference(document, ParentForCreate(document), path);
    }

    /// <summary>
    /// Written to the session rather than acted on here, so the button and the
    /// command surface's own switch are one answer — and the button is then told
    /// what it became rather than assuming it.
    /// </summary>
    private void OnReferencesVisibleRequested(bool visible) =>
        _session.ReferencesVisible = visible;

    private void PushReferencesVisible() =>
        _view.SetReferencesVisible(_session.ReferencesVisible);

    /// <summary>
    /// Files dropped on the window: every one this editor can stand for goes in as
    /// a reference, and what is left over is named in one message rather than
    /// silently dropped — somebody meant something by dragging it here.
    /// </summary>
    private void OnFilesDropped(IReadOnlyList<string> pathsList)
    {
        if (_document is not { } document || !CanCreateElement())
            return;

        _session.EndGesture();

        // Read once, before anything is placed: each reference becomes the
        // selection as it lands, so re-reading it would nest the second drop
        // inside the first.
        Guid parentId = ParentForCreate(document);
        var refusedList = new List<string>();

        foreach (string path in pathsList)
        {
            if (ReferenceFormats.TryGetKind(path, out _))
                PlaceReference(document, parentId, path);
            else
                refusedList.Add(Path.GetFileName(path));
        }

        if (refusedList.Count > 0)
        {
            _view.ShowError(
                "Nothing here can stand for that",
                $"{string.Join(", ", refusedList)} — a reference is a picture "
                + $"({string.Join(", ", ReferenceFormats.ImageExtensions)}) or a 3D model "
                + $"({string.Join(", ", ReferenceFormats.ModelExtensions)}).");
        }
    }

    /// <summary>
    /// Under whatever is selected, like every other create — and selected
    /// afterwards, because the reference is what the user is about to move into
    /// place.
    /// </summary>
    private void PlaceReference(DocumentPresenter document, Guid parentId, string path)
    {
        try
        {
            _session.SetSelection(new SelectionTarget(
                SelectionKind.Element,
                document.Scene.CreateReference(parentId, DefaultReferenceName(path), path)));
        }
        catch (ArgumentException error)
        {
            // The picker's filters say which files it offers, and a path typed into
            // it may still be anything at all.
            _view.ShowError("Cannot use that file", error.Message);
        }
    }

    private Guid ParentForCreate(DocumentPresenter document)
    {
        SelectionTarget selection = _session.ActiveTarget;

        return selection.Kind == SelectionKind.Element ? selection.Id : document.Scene.Id;
    }

    private static string DefaultReferenceName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);

        return string.IsNullOrWhiteSpace(name) ? "Reference" : name;
    }

    private void CreateElement(DocumentPresenter document, ComponentKind kind, string name)
    {
        if (!CanCreateElement())
            return;

        _session.SetSelection(new SelectionTarget(
            SelectionKind.Element,
            document.Scene.CreateElement(ParentForCreate(document), kind, name)));
    }

    /// <summary>
    /// A new scene is opened straight away — the same thing an agent's
    /// <c>create-scene</c> does, and the only reading of "add a scene" that leaves
    /// the user somewhere they can work.
    /// </summary>
    private void CreateScene(DocumentPresenter document)
    {
        Guid id = document.Project.CreateScene(
            ComposeName("Scene", document.Project.Scenes.Select(scene => scene.Name)),
            document.Scene.Size);

        // After activating, not before: switching scenes drops the selection.
        _context.SetActiveScene(id);
        _session.SetSelection(new SelectionTarget(SelectionKind.Scene, id));
    }

    private void CreateMaterial(DocumentPresenter document)
    {
        Guid id = document.Project.CreateMaterial(
            ComposeName("Material", document.Project.Materials.Select(material => material.Name)),
            NewMaterialColor);

        _session.SetSelection(new SelectionTarget(SelectionKind.Material, id));
    }

    /// <summary>
    /// Removes what is selected — <b>all of it</b>, as one history record, so undo
    /// takes back the gesture the user watched rather than one element of it.
    /// </summary>
    /// <remarks>
    /// A single target still goes through the plain path and leaves a plain record:
    /// a batch of one would say "Delete Alpha and 0 more" and buy nothing. The
    /// refusals are collected rather than shown one at a time — three dialogs for
    /// three materials still in use is a worse answer than one.
    /// </remarks>
    private void OnDeleteSelectionRequested()
    {
        if (_document is not { } document || !CanDelete())
            return;

        _session.EndGesture();

        // Snapshotted before anything goes: each deletion prunes the selection,
        // and this is the list the gesture was aimed at.
        SelectionTarget[] targets = _session.Selection.Targets.ToArray();

        if (targets.Length <= 1)
        {
            TryDelete(document, _session.ActiveTarget, "Cannot delete");

            return;
        }

        DeleteAll(document, targets);
    }

    private void DeleteAll(DocumentPresenter document, IReadOnlyList<SelectionTarget> targets)
    {
        SelectionTarget active = _session.ActiveTarget;
        string? failure = null;

        using (document.Project.History.CreateBatch(
            ToDeleteKind(active.Kind),
            active.Kind == SelectionKind.Element ? document.Scene.Id : Guid.Empty,
            active.Id,
            ActionLabelHelper.DescribeTargets(DescribeTarget(document, active), targets.Count)))
        {
            for (var index = 0; index < targets.Count; index++)
            {
                // A subtree goes with its root, so a selected child may already be
                // gone by the time its own turn comes — that is not a refusal.
                if (!Exists(document, targets[index]))
                    continue;

                if (!TryRemove(document, targets[index], out string message))
                    failure ??= message;
            }
        }

        // After the scope, never inside it: a dialog raised mid-batch would leave
        // the gesture open across a question the user has to answer.
        if (failure != null)
            _view.ShowError("Cannot delete", failure);
    }

    /// <summary>
    /// Removes one thing, and says whether it went. Shared with the cut, which is
    /// this plus a clipboard — and which needs the answer, because a cut that could
    /// not remove anything must not claim to have taken it.
    /// </summary>
    private bool TryDelete(DocumentPresenter document, SelectionTarget selection, string failureTitle)
    {
        if (TryRemove(document, selection, out string message))
            return true;

        _view.ShowError(failureTitle, message);

        return false;
    }

    private static bool TryRemove(
        DocumentPresenter document, SelectionTarget selection, out string message)
    {
        message = string.Empty;

        try
        {
            switch (selection.Kind)
            {
                case SelectionKind.Element:
                    document.Scene.DeleteElement(selection.Id);
                    break;

                case SelectionKind.Scene:
                    document.Project.DeleteScene(selection.Id);
                    break;

                case SelectionKind.Material:
                    document.Project.DeleteMaterial(selection.Id);
                    break;
            }
        }
        catch (InvalidOperationException exception)
        {
            // The model refuses a deletion that would leave the document broken —
            // a material still painted onto voxels, the last scene. That is an
            // answer to show the user, not a crash.
            message = exception.Message;

            return false;
        }

        return true;
    }

    private static bool Exists(DocumentPresenter document, SelectionTarget target) =>
        target.Kind switch
        {
            SelectionKind.Element => document.Scene.TryGetElement(target.Id, out _),
            SelectionKind.Scene => document.Project.TryGetScene(target.Id, out _),
            SelectionKind.Material => document.Project.TryGetMaterial(target.Id, out _),
            _ => false
        };

    /// <summary>
    /// What to call a target in a history record. A name rather than an id, and
    /// snapshotted now because the thing is about to stop existing.
    /// </summary>
    private static string DescribeTarget(DocumentPresenter document, SelectionTarget target) =>
        target.Kind switch
        {
            SelectionKind.Element =>
                document.Scene.TryGetElement(target.Id, out IHierarchyElement element)
                    ? element.Name
                    : string.Empty,

            SelectionKind.Scene =>
                document.Project.TryGetScene(target.Id, out IScene scene)
                    ? scene.Name
                    : string.Empty,

            SelectionKind.Material =>
                document.Project.TryGetMaterial(target.Id, out IMaterial material)
                    ? material.Name
                    : string.Empty,

            _ => string.Empty
        };

    private static ActionKind ToDeleteKind(SelectionKind kind) => kind switch
    {
        SelectionKind.Scene => ActionKind.DeleteScene,
        SelectionKind.Material => ActionKind.DeleteMaterial,
        _ => ActionKind.DeleteElement
    };

    // --- the clipboard ---

    private void OnCopyRequested()
    {
        if (_document is not { } document || Capture(document, _session.ActiveTarget) is not { } clip)
            return;

        _session.Clipboard = clip;
    }

    /// <summary>
    /// Copy, then remove — and the clipboard is written only once the removal has
    /// actually happened. A refused cut must leave both the document and the
    /// clipboard as they were, rather than half of a gesture in each.
    /// </summary>
    private void OnCutRequested()
    {
        if (_document is not { } document
            || !CanDelete()
            || Capture(document, _session.ActiveTarget) is not { } clip)
        {
            return;
        }

        _session.EndGesture();

        if (TryDelete(document, _session.ActiveTarget, "Cannot cut"))
            _session.Clipboard = clip;
    }

    /// <summary>
    /// Puts the clip where its kind belongs: an element under the selected element,
    /// a scene and a material in the project, which is the only place either can
    /// live. Whatever lands is selected, so the next thing the user does is about
    /// the copy.
    /// </summary>
    /// <remarks>
    /// A paste is never refused for want of a matching selection. With a material
    /// selected and an element on the clipboard there is no sensible reading of
    /// "under here", so the element goes to the scene's root instead — the paste
    /// happening somewhere obvious beats nothing happening at all.
    /// </remarks>
    private void OnPasteRequested()
    {
        if (_document is not { } document || _session.Clipboard is not { } clip)
            return;

        _session.EndGesture();

        switch (clip)
        {
            case ElementClip element:
                _session.SetSelection(new SelectionTarget(
                    SelectionKind.Element,
                    document.Scene.PasteElement(ResolvePasteParent(document), element)));
                break;

            case SceneClip scene:
                PasteScene(document, scene);
                break;

            case MaterialClip material:
                _session.SetSelection(new SelectionTarget(
                    SelectionKind.Material, document.Project.PasteMaterial(material)));
                break;
        }
    }

    /// <summary>
    /// Copy and paste in one press, and deliberately without touching the
    /// clipboard: a duplicate is about the row that is selected, so losing a clip
    /// somebody put there on purpose would be a second thing happening that nobody
    /// asked for. An element lands beside the original rather than inside it.
    /// </summary>
    private void OnDuplicateRequested()
    {
        if (_document is not { } document)
            return;

        SelectionTarget selection = _session.ActiveTarget;

        if (Capture(document, selection) is not { } clip)
            return;

        _session.EndGesture();

        switch (clip)
        {
            case ElementClip element
                when document.Scene.TryGetElement(selection.Id, out IHierarchyElement original):
                _session.SetSelection(new SelectionTarget(
                    SelectionKind.Element,
                    document.Scene.PasteElement(original.Parent.Id, element)));
                break;

            case SceneClip scene:
                PasteScene(document, scene);
                break;

            case MaterialClip material:
                _session.SetSelection(new SelectionTarget(
                    SelectionKind.Material, document.Project.PasteMaterial(material)));
                break;
        }
    }

    /// <summary>
    /// A copied scene becomes the one being worked on, exactly as a created one
    /// does — a scene nobody is looking at is not much use to whoever just made it.
    /// </summary>
    /// <remarks>
    /// Said here rather than left to happen: the history already takes the editor to
    /// the scene a record belongs to, so the copy would become active anyway, and a
    /// selection set before that switch would be dropped by it. Activating first and
    /// selecting after is the order <see cref="CreateScene"/> uses for the same
    /// reason.
    /// </remarks>
    private void PasteScene(DocumentPresenter document, SceneClip clip)
    {
        Guid id = document.Project.PasteScene(clip);

        _context.SetActiveScene(id);
        _session.SetSelection(new SelectionTarget(SelectionKind.Scene, id));
    }

    /// <summary>
    /// What a copy of the selection would be, or null where there is nothing to
    /// copy — a category row, the project's own row, or nothing selected. Also null
    /// for a row naming something that has since gone, which is why each arm asks
    /// the document rather than trusting the id.
    /// </summary>
    private static ClipContent? Capture(DocumentPresenter document, SelectionTarget selection) =>
        selection.Kind switch
        {
            SelectionKind.Element when document.Scene.TryGetElement(selection.Id, out _) =>
                document.Scene.CaptureElement(selection.Id),

            SelectionKind.Scene when document.Project.TryGetScene(selection.Id, out _) =>
                document.Project.CaptureScene(selection.Id),

            SelectionKind.Material when document.Project.TryGetMaterial(selection.Id, out _) =>
                document.Project.CaptureMaterial(selection.Id),

            _ => null
        };

    /// <summary>
    /// Under the selected element, or the scene's own root for anything else.
    /// Prefab content falls back too: it is read-only wherever it appears, and a
    /// paste into it would be refused by the model rather than by the chord.
    /// </summary>
    private Guid ResolvePasteParent(DocumentPresenter document)
    {
        SelectionTarget selection = _session.ActiveTarget;

        if (selection.Kind != SelectionKind.Element
            || !document.Scene.TryGetElement(selection.Id, out IHierarchyElement element)
            || element.PrefabInstanceId != null
            || element.ComponentKind == ComponentKind.PrefabInstance)
        {
            return document.Scene.Id;
        }

        return selection.Id;
    }

    /// <summary>
    /// Selection is session state, not document data, so nothing in the model
    /// clears it when a selected element stops existing — including through undo.
    /// What went is dropped and the rest stands: the other parts a user picked out
    /// are still perfectly good, and losing them with the one that was deleted
    /// would be a second thing happening that nobody asked for.
    /// </summary>
    /// <remarks>
    /// The whole selection is walked rather than matched against the change,
    /// because deleting a group removes its entire subtree while naming only its
    /// root — a selected child would otherwise survive its own deletion. Nothing is
    /// allocated unless something is actually missing, which is what makes this
    /// affordable on a change raised by every voxel edit.
    /// </remarks>
    private void OnSceneChanged(SceneChange change)
    {
        if (_document is not { } document)
            return;

        SelectionState selection = _session.Selection;
        IReadOnlyList<SelectionTarget> targets = selection.Targets;

        for (var index = targets.Count - 1; index >= 0; index--)
        {
            SelectionTarget target = targets[index];

            if (target.Kind == SelectionKind.Element
                && !document.Scene.TryGetElement(target.Id, out _))
            {
                selection = SelectionEdit.Remove(selection, target);
            }
        }

        _session.SetSelection(selection);
    }

    /// <summary>
    /// The same rule one level up: a scene or a material can go while it is what
    /// the session is pointing at. The current brush material is not a selection
    /// but has the same problem, and a project always keeps one to fall back to.
    /// </summary>
    private void OnProjectContentChanged(ProjectChange change)
    {
        if (_document is not { } document)
            return;

        if (change.Kind is ProjectChangeKind.SceneRemoved or ProjectChangeKind.MaterialRemoved)
        {
            _session.SetSelection(SelectionEdit.Remove(
                _session.Selection,
                new SelectionTarget(
                    change.Kind == ProjectChangeKind.SceneRemoved
                        ? SelectionKind.Scene
                        : SelectionKind.Material,
                    change.TargetId)));
        }

        if (change.Kind == ProjectChangeKind.MaterialRemoved
            && _session.CurrentMaterialId == change.TargetId)
        {
            _session.CurrentMaterialId = document.Project.Materials[0].Id;
        }

        RefreshCommandState();
    }

    private void OnHistoryChanged(HistoryChange change)
    {
        FollowScene(change);
        RefreshCommandState();
        RefreshTitle();
    }

    /// <summary>
    /// Undo reverses the last thing that happened, whichever scene it happened
    /// in — so the editor goes there. Without this, undoing an edit in a scene
    /// nobody is looking at appears to do nothing at all.
    /// </summary>
    private void FollowScene(HistoryChange change)
    {
        if (change.SceneId == Guid.Empty || _document is not { } document)
            return;

        // Undoing a scene's creation names a scene that no longer exists; the
        // fallback has already happened by then.
        if (change.SceneId == document.Scene.Id || !Exists(document, change.SceneId))
            return;

        _context.SetActiveScene(change.SceneId);
    }

    /// <summary>
    /// Whether a record's scene is still somewhere the editor can be taken. A
    /// prefab's contents are stamped with the prefab's own id, so this is also what
    /// makes undoing an edit made inside one open that prefab again — and undoing
    /// past it, out of one and back into an ordinary scene.
    /// </summary>
    private static bool Exists(DocumentPresenter document, Guid sceneId) =>
        document.Project.TryGetScene(sceneId, out _)
        || document.Project.TryGetPrefab(sceneId, out _);

    private void RefreshCommandState()
    {
        IHistory? history = _document?.Project.History;
        bool canCreateElement = CanCreateElement();

        _view.SetUndoState(history?.CanUndo ?? false, history?.CanRedo ?? false);
        _view.SetCreateEnabled(CreateKind.Group, canCreateElement);
        _view.SetCreateEnabled(CreateKind.VoxelModel, canCreateElement);
        _view.SetCreateEnabled(CreateKind.Camera, canCreateElement);
        _view.SetCreateEnabled(CreateKind.Scene, _document != null);
        _view.SetCreateEnabled(CreateKind.Material, _document != null);
        _view.SetCreateEnabled(CreateKind.PrefabInstance, CanCreateInstance());
        _view.SetCreateEnabled(CreateKind.Reference, canCreateElement);
        _view.SetDeleteEnabled(CanDelete());
    }

    /// <summary>The project's own materials, as a picker may offer them; nothing without a document.</summary>
    private IReadOnlyList<MaterialChoice> CollectMaterialChoices()
    {
        if (_document is not { } document)
            return Array.Empty<MaterialChoice>();

        var choicesList = new List<MaterialChoice>(document.Project.Materials.Count);

        foreach (IMaterial material in document.Project.Materials)
            choicesList.Add(new MaterialChoice(material.Id, material.Name));

        return choicesList;
    }

    /// <summary>
    /// Whether "Replace uses…" has anything to do for a material row: some cell
    /// of the scene being worked on wears it, and the project has another
    /// material to give those cells. A shipped material is not on the row's list,
    /// so it never comes here.
    /// </summary>
    /// <remarks>
    /// Asked as the menu is built rather than pushed, because the second half of
    /// it walks every cell of the scene. Pushed, it ran on every history record
    /// and on every frame of a colour-picker drag, to answer a menu that was not
    /// open.
    /// </remarks>
    private bool CanReplaceMaterial(SelectionTarget target)
    {
        if (_document is not { } document || target.Kind != SelectionKind.Material)
            return false;

        return document.Project.Materials.Count > 1
            && IsWornIn(document.Scene, target.Id);
    }

    /// <summary>
    /// Whether a replace over a scene would change anything: some voxel model in
    /// it that may be written to wears the material. Prefab-instance contents are
    /// passed over exactly as the replace passes them over — they are derived
    /// from a source, and the source's own scene is where they are replaced.
    /// </summary>
    private static bool IsWornIn(IHierarchyContainer container, Guid materialId)
    {
        foreach (IHierarchyElement element in container.Children)
        {
            if (element.PrefabInstanceId != null)
                continue;

            if (element.Component is IVoxelModel model && IsWornBy(model, materialId))
                return true;

            if (IsWornIn(element, materialId))
                return true;
        }

        return false;
    }

    private static bool IsWornBy(IVoxelModel model, Guid materialId)
    {
        foreach (Guid worn in model.Voxels.Values)
        {
            if (worn == materialId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Give every cell wearing one material another, across the scene being
    /// worked on: the menu's reach is what the user is looking at. The other
    /// scenes and the prefabs' own contents are the <c>replace-material</c>
    /// command's <c>scope=project</c>. One record; the model files it.
    /// </summary>
    private void OnReplaceMaterialUsesRequested(Guid fromId, Guid toId)
    {
        if (_document is not { } document
            || !document.Project.TryGetMaterial(toId, out _)
            || fromId == toId)
        {
            return;
        }

        _session.EndGesture();
        document.Scene.ReplaceMaterial(fromId, toId, null);
    }

    /// <summary>
    /// Elements are made in the scene being worked on, so the selection has to be
    /// somewhere in it — one of its elements, its own row, or nothing in
    /// particular. A material or another scene is not a place to put an element,
    /// and the buttons say so rather than quietly creating one elsewhere.
    /// </summary>
    private bool CanCreateElement()
    {
        if (_document is not { } document)
            return false;

        return _session.ActiveTarget.Kind switch
        {
            SelectionKind.Element => true,
            SelectionKind.Scene => _session.ActiveTarget.Id == document.Scene.Id,
            SelectionKind.None or SelectionKind.Project or SelectionKind.ScenesCategory => true,
            _ => false
        };
    }

    /// <summary>
    /// Whether the selected prefab could be placed here: something has to be
    /// picked out of the library for the press to mean anything, and a prefab
    /// cannot be put inside itself — which is the model's own rule, asked rather
    /// than restated, so a live button and a refusal cannot disagree.
    /// </summary>
    private bool CanCreateInstance()
    {
        if (_document is not { } document)
            return false;

        SelectionTarget selection = _session.ActiveTarget;

        return selection.Kind == SelectionKind.Prefab
            && document.Project.TryGetPrefab(selection.Id, out _)
            && document.Project.CanContainPrefab(document.Scene.Id, selection.Id);
    }

    /// <summary>
    /// Asked in both places that act on a deletion, so the button's state and
    /// what the button does cannot disagree. The last scene and the last material
    /// stay: a project is not a project without them.
    /// </summary>
    private bool CanDelete()
    {
        if (_document is not { } document)
            return false;

        return _session.ActiveTarget.Kind switch
        {
            SelectionKind.Element => true,
            SelectionKind.Scene => document.Project.Scenes.Count > 1,
            SelectionKind.Material => document.Project.Materials.Count > 1,
            _ => false
        };
    }

    /// <summary>
    /// "Scene", then "Scene 2" — enough that a name stays usable as an address
    /// for an agent, without pretending names are unique keys.
    /// </summary>
    private static string ComposeName(string prefix, IEnumerable<string> taken)
    {
        var takenHashSet = new HashSet<string>(taken);

        if (!takenHashSet.Contains(prefix))
            return prefix;

        for (var i = 2; ; i++)
        {
            var candidate = $"{prefix} {i}";

            if (!takenHashSet.Contains(candidate))
                return candidate;
        }
    }

    private void RefreshTitle() =>
        _view.SetWindowTitle(
            WindowTitleHelper.Compose(_context.Path, _document?.IsModified ?? false));
}
