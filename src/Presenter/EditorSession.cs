using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// State of the editing session: what is selected, what the next voxel will be
/// made of. Not part of the document and never saved.
/// </summary>
/// <remarks>
/// This is the only thing sibling presenters share. The project tree writes
/// <see cref="Selection"/>, the inspector reacts to <see cref="SelectionChanged"/>,
/// and neither knows the other exists — which is what keeps the shell from
/// turning into a message router.
/// <para>
/// Which scene is being worked on is deliberately not here: an agent's
/// <c>activate-scene</c> and the user activating one have to be the same event,
/// so that answer lives in <see cref="Commands.CommandContext"/> beside which
/// document is open.
/// </para>
/// </remarks>
internal sealed class EditorSession
{
    private SelectionState _selection = SelectionState.Empty;
    private Guid _currentMaterialId;

    /// <summary>
    /// The tool a fresh window is holding, stated rather than inherited from the
    /// enum's zero: opening on the cursor is a decision about the product — a
    /// window that points at things before it edits them — and it should read as
    /// one here, not as a consequence of how <see cref="ToolKind"/> happens to be
    /// ordered.
    /// </summary>
    private ToolKind _activeTool = ToolKind.Select;

    private bool _referencesVisible = true;

    private IDisposable? _gesture;

    public event Action? SelectionChanged;

    public event Action? CurrentMaterialChanged;

    public event Action? ToolChanged;

    /// <summary>Raised when the whole-scene reference switch is turned.</summary>
    public event Action? ReferencesVisibleChanged;

    /// <summary>
    /// A tool was asked for — by a key in the viewport, or by one in a panel that
    /// has a row selected. Distinct from <see cref="ToolChanged"/>, and raised even
    /// when the tool asked for is the one already active: what follows a request is
    /// not only the tool being recorded but the viewport taking the keyboard, and
    /// asking for the tool you are already in is exactly the case where the second
    /// half is the whole point. Which tool it is stays where it always was, in
    /// <see cref="ActiveTool"/> — the event says only that one was asked for.
    /// </summary>
    public event Action? ToolRequested;

    /// <summary>
    /// Everything selected — a kind and an id apiece, because the tree holds
    /// scenes, elements and materials and an id alone would not say which. Always
    /// of one kind, and possibly empty.
    /// </summary>
    /// <remarks>
    /// The canon left the cardinality open and this is where it is settled: a
    /// selection is a set. What follows is that almost nothing downstream reads
    /// this — the inspector, the handles, the brush's target and the clipboard all
    /// read <see cref="ActiveTarget"/> — and the two surfaces that draw the
    /// selection read the set: the project tree's rows, and the viewport's borders.
    /// </remarks>
    public SelectionState Selection => _selection;

    /// <summary>
    /// The one the panels speak for: what the inspector shows, where the handles
    /// sit, which model the brush paints into. <see langword="default"/> when
    /// nothing is selected.
    /// </summary>
    /// <remarks>
    /// A selection of several still has exactly one of these, and that is not a
    /// placeholder for multi-editing later — it is what makes a set of parts still
    /// name a single thing to act on. Which one it is follows the last click, and
    /// <see cref="SelectionEdit"/> is what keeps it inside the set.
    /// </remarks>
    public SelectionTarget ActiveTarget => _selection.Active;

    /// <summary>Nothing selected — what a document change leaves behind.</summary>
    public void ClearSelection() => SetSelection(SelectionState.Empty);

    /// <summary>One thing selected, replacing whatever was.</summary>
    public void SetSelection(SelectionTarget target) =>
        SetSelection(SelectionState.Single(target));

    public void SetSelection(SelectionState selection)
    {
        if (_selection.Matches(selection))
            return;

        _selection = selection;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// What was last copied or cut, if anything. Session state like the selection,
    /// and never saved — a clipboard belongs to the person at the keyboard, not to
    /// the document.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> cleared when the document changes. A clip refers to
    /// nothing and carries the materials its cells wear, so copying in one project
    /// and pasting into another is not a special case to be defended against — it
    /// is the ordinary consequence of a copy being independent, and it is the one
    /// way this editor has of moving a model between files.
    /// </remarks>
    public ClipContent? Clipboard { get; set; }

    /// <summary>What the brush paints with. Editor state, not document data.</summary>
    public Guid CurrentMaterialId
    {
        get => _currentMaterialId;
        set
        {
            if (_currentMaterialId == value)
                return;

            _currentMaterialId = value;
            CurrentMaterialChanged?.Invoke();
        }
    }

    /// <summary>
    /// Which tool the viewport is in. Exclusive, so switching to one leaves every
    /// other off — the viewport reads this to decide what a plain click means.
    /// </summary>
    /// <remarks>
    /// Session state like the selection, and shared for the same reason: the
    /// viewport reports that a tool key was pressed and the gizmo answers by
    /// changing what it draws, without either knowing the other exists.
    /// </remarks>
    public ToolKind ActiveTool
    {
        get => _activeTool;
        set
        {
            if (_activeTool == value)
                return;

            _activeTool = value;
            ToolChanged?.Invoke();
        }
    }

    /// <summary>
    /// Whether references are drawn at all. One switch for the whole scene, beside
    /// the per-element eye every element already has.
    /// </summary>
    /// <remarks>
    /// Session state and deliberately not the document's: it is for looking at the
    /// model clean for a moment, so it is not written to the file, leaves no history
    /// record and touches no element's own hidden flag — putting it back shows
    /// exactly what was shown before. It also survives a document change, unlike
    /// the selection, because it names nothing in any document.
    /// </remarks>
    public bool ReferencesVisible
    {
        get => _referencesVisible;
        set
        {
            if (_referencesVisible == value)
                return;

            _referencesVisible = value;
            ReferencesVisibleChanged?.Invoke();
        }
    }

    /// <summary>
    /// Whether a gesture is in flight — a drag under way, a colour picker still
    /// open. What the recovery snapshot waits on: a mid-gesture write is a
    /// consistent document, but the record it would be taken at is the one the
    /// gesture is about to file.
    /// </summary>
    public bool HasGesture => _gesture != null;

    /// <summary>
    /// A gesture in flight was ended — by its own release, or by whatever else
    /// starts a new action first. What a panel still holding the gesture's
    /// <em>other</em> half does about it is its own: the brush closes its stroke,
    /// because a stroke whose record has been filed under it has nothing left to
    /// collect into.
    /// </summary>
    public event Action? GestureEnded;

    /// <summary>
    /// The one way a tool is asked for. Records it like the setter does, then says
    /// that it was asked for, so whatever else belongs to activating a tool happens
    /// once and in one place rather than at each keyboard that can reach one.
    /// </summary>
    public void RequestTool(ToolKind tool)
    {
        ActiveTool = tool;
        ToolRequested?.Invoke();
    }

    /// <summary>
    /// Takes over the gesture in flight — a drag, or an open colour picker —
    /// whose edits collapse into one history record when it ends.
    /// </summary>
    /// <remarks>
    /// Session state rather than a field in the panel that opened it, because
    /// **anything that starts a new action has to end the one in flight first**:
    /// an open scope goes on collecting, so a create or an undo landing inside it
    /// would be filed as part of somebody else's gesture. The shell needs to be
    /// able to end a gesture it knows nothing about, and this is the only thing
    /// panels and the shell share.
    /// </remarks>
    public void BeginGesture(IDisposable scope)
    {
        EndGesture();

        _gesture = scope;
    }

    /// <summary>
    /// Commits whatever is open, if anything. Committing rather than discarding:
    /// the edits already went through the model, and the user already watched
    /// them happen.
    /// </summary>
    public void EndGesture()
    {
        if (_gesture == null)
            return;

        _gesture.Dispose();
        _gesture = null;

        // After the field is cleared, so a listener that ends its own gesture in
        // answer — the brush closing its stroke — finds nothing left to end.
        GestureEnded?.Invoke();
    }
}
