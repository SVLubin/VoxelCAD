using VoxelCAD.CommonTypes;
using VoxelCAD.Model;

namespace VoxelCAD.Presenter;

/// <summary>
/// Holds the editor out of a tool that has nothing to act on: whenever the
/// selection or the tool changes, a tool without a subject falls back to the
/// cursor.
/// </summary>
/// <remarks>
/// The state this closes off is a silent one, which is why it is worth a rule
/// rather than a habit. A cell tool with nothing selected, or over a group, leaves
/// the viewport proposing no preview and answering no click — and nothing anywhere
/// says so, so it reads exactly like selection having stopped working. It is
/// reached without doing anything odd: deleting what was painted, clicking a
/// material row, switching scenes, or reaching for the brush before picking a
/// model.
/// <para>
/// In practice that means the cell tools, and that is not a special case: the
/// tools that select are never over nothing, because their click is what finds
/// the something. See <see cref="ToolSubjects.CanAct"/>.
/// </para>
/// <para>
/// The fallback goes through <see cref="EditorSession.ActiveTool"/> rather than
/// <see cref="EditorSession.RequestTool"/>, and the difference is the whole
/// reason those two exist: nobody asked for this tool, so the viewport must not
/// be handed the keyboard for it. A user whose selection just went away is
/// working somewhere else, and taking the keyboard off that panel would be a
/// second surprise on top of the first.
/// </para>
/// <para>
/// It is not a memory: a tool that fell back stays fallen when a subject turns
/// up again. Restoring the tool the user was last in would mean the same click
/// putting the editor into different modes depending on what happened before it.
/// </para>
/// </remarks>
internal sealed class ToolFallbackPresenter : PresenterBase
{
    private readonly IScene _scene;
    private readonly EditorSession _session;

    public ToolFallbackPresenter(IScene scene, EditorSession session)
    {
        _scene = scene;
        _session = session;

        _session.SelectionChanged += OnSessionChanged;
        _session.ToolChanged += OnSessionChanged;
        _scene.Changed += OnSceneChanged;

        // Like every other presenter's constructor: the rule holds from the first
        // frame, not from the first change. A document opened while a transform
        // tool is active starts with nothing selected.
        Apply();
    }

    protected override void OnDispose()
    {
        _session.SelectionChanged -= OnSessionChanged;
        _session.ToolChanged -= OnSessionChanged;
        _scene.Changed -= OnSceneChanged;
    }

    private void OnSessionChanged()
    {
        // A change can rebuild this presenter part-way through its own dispatch;
        // see PresenterBase.
        if (IsAlive)
            Apply();
    }

    /// <summary>
    /// A deletion is the one document change that can take a subject away without
    /// the selection moving: it goes on naming an element that is no longer there.
    /// </summary>
    private void OnSceneChanged(SceneChange change)
    {
        if (IsAlive && change.Kind == SceneChangeKind.ElementRemoved)
            Apply();
    }

    /// <summary>
    /// Re-entrant by construction and bounded by it: writing the tool raises
    /// <see cref="EditorSession.ToolChanged"/>, which lands back here — and the
    /// cursor can act on anything, so the second pass writes nothing and the
    /// setter's own equality check ends it.
    /// </summary>
    private void Apply() =>
        _session.ActiveTool = ToolSubjects.Resolve(
            _session.ActiveTool, ToolSubjects.FindSubject(_scene, _session.ActiveTarget));
}
