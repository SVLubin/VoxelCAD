using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Preferences;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Everything bound to one open document: the panel presenters, and whether it
/// has been edited since it was last written.
/// </summary>
/// <remarks>
/// It exists so that "panel presenters are recreated when the document changes"
/// is one object being replaced rather than five. The shell holds exactly one of
/// these and swaps it; the views, the session and the shell itself carry on.
/// <para>
/// Switching the active scene is the same move one level down: the presenters
/// bound to a scene are thrown away and rebuilt around the new one, while the
/// history panel — which belongs to the project, not to any scene — stays where
/// it is.
/// </para>
/// </remarks>
internal sealed class DocumentPresenter : PresenterBase
{
    private readonly IHierarchyView _hierarchyView;
    private readonly IInspectorView _inspectorView;
    private readonly ISceneView _sceneView;
    private readonly EditorSession _session;
    private readonly IDialogService _dialogs;

    // Only for the viewport, which draws a camera's frustum at the shape of the
    // picture that would be rendered — how big that picture is is the render
    // form's answer, and it lives in the preferences.
    private readonly IEditorPreferences _preferences;
    private readonly Action<Guid> _activateScene;
    private readonly HistoryPresenter _history;

    private HierarchyPresenter _hierarchy = null!;
    private InspectorPresenter _inspector = null!;
    private ViewportPresenter _viewport = null!;
    private BrushPresenter _brush = null!;
    private SelectPresenter _select = null!;
    private EyedropperPresenter _eyedropper = null!;
    private ExtrudePresenter _extrude = null!;
    private FillPresenter _fill = null!;
    private GizmoPresenter _gizmo = null!;
    private ToolFallbackPresenter _toolFallback = null!;

    /// <summary>
    /// The record the document was last written at, or null for a document that
    /// has never matched a file — a recovered snapshot, which stays modified
    /// through every undo until a save gives it a record to match.
    /// </summary>
    private Guid? _savedRecordId;

    public DocumentPresenter(
        IProject project,
        IScene scene,
        IHierarchyView hierarchyView,
        IInspectorView inspectorView,
        IHistoryView historyView,
        ISceneView sceneView,
        EditorSession session,
        IDialogService dialogs,
        IEditorPreferences preferences,
        Action<Guid> activateScene,
        bool openedModified = false)
    {
        Project = project;
        Scene = scene;

        _hierarchyView = hierarchyView;
        _inspectorView = inspectorView;
        _sceneView = sceneView;
        _session = session;
        _dialogs = dialogs;
        _preferences = preferences;
        _activateScene = activateScene;

        // Panel views, not the shell: a presenter down here has no business
        // reaching the menus or the window title.
        _history = new HistoryPresenter(project.History, historyView);

        BindScene();

        // A project that was just created or just loaded has an empty history, so
        // it starts out clean — unless it was recovered from a snapshot, in which
        // case nothing on disk matches it and it is born modified. Decided here, in
        // the one place the mark is first written, rather than by a second call
        // after the constructor: a value written on one path and defaulted on
        // another is the shape of bug the ledger keeps paying for.
        _savedRecordId = openedModified ? null : RecordIdentity.Current(Project.History);
    }

    public IProject Project { get; }

    /// <summary>The scene being worked on. The others are rows in the panel.</summary>
    public IScene Scene { get; private set; }

    /// <summary>
    /// Whether there is anything to lose. The model knows nothing about being
    /// dirty, so this is read off the history — but off the identity of the
    /// record the document currently sits on, never off its index. Undoing back
    /// to the saved point and then editing again lands on the same index with a
    /// different record, and an index comparison would call that saved.
    /// </summary>
    public bool IsModified => RecordIdentity.Current(Project.History) != _savedRecordId;

    public void MarkSaved() => _savedRecordId = RecordIdentity.Current(Project.History);

    /// <summary>
    /// Rebuilds the scene-bound panels around another scene of the same document.
    /// The selection goes with them: its ids address the scene being left.
    /// </summary>
    public void SetActiveScene(IScene scene)
    {
        if (ReferenceEquals(scene, Scene))
            return;

        UnbindScene();

        Scene = scene;
        _session.ClearSelection();

        BindScene();
    }

    protected override void OnDispose()
    {
        UnbindScene();
        _history.Dispose();
    }

    private void BindScene()
    {
        _hierarchy = new HierarchyPresenter(
            Project, Scene, _hierarchyView, _session, _dialogs, _activateScene);
        _inspector = new InspectorPresenter(
            Project, Scene, _inspectorView, _session, _preferences, _dialogs, _activateScene);
        _viewport = new ViewportPresenter(Project, Scene, _sceneView, _session, _preferences);
        _brush = new BrushPresenter(Project, Scene, _sceneView, _session);

        // Order-free: the cursor answers its own event rather than the click every
        // cell tool shares, so nothing can be offered a click that was meant for it.
        _select = new SelectPresenter(Scene, _sceneView, _session);

        // After the brush, and not by taste: a sample hands the viewport back to
        // the brush, and handlers of one click run in subscription order — a brush
        // bound second would be offered the very click that switched the tool, and
        // would paint over the voxel just read. EyedropperTests pins it.
        _eyedropper = new EyedropperPresenter(Scene, _sceneView, _session);

        // Order-free, unlike the pair above: neither the extruder nor the fill ever
        // switches the tool, so no other listener can be offered the click that
        // acted.
        _extrude = new ExtrudePresenter(Scene, _sceneView, _session);
        _fill = new FillPresenter(Scene, _sceneView, _session);

        // After the viewport: the gizmo's first push addresses nodes the viewport
        // has just built, and a highlight asked for before them lands on nothing.
        _gizmo = new GizmoPresenter(Project, Scene, _sceneView, _session);

        // Last, and it has to be: it may write the tool as it binds, and everything
        // that mirrors the tool is already listening by now. Bound to the scene like
        // the rest, because what a tool has to act on is a question about this
        // scene's elements.
        _toolFallback = new ToolFallbackPresenter(Scene, _session);
    }

    private void UnbindScene()
    {
        _toolFallback.Dispose();
        _gizmo.Dispose();
        _fill.Dispose();
        _extrude.Dispose();
        _eyedropper.Dispose();
        _select.Dispose();
        _brush.Dispose();
        _viewport.Dispose();
        _inspector.Dispose();
        _hierarchy.Dispose();
    }

}
