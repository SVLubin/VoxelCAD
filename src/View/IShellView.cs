using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The editor chrome: docks, toolbar, and the panels living in them. Panel
/// presenters receive only the panel they drive, never this, so they cannot reach
/// the menus or the window title.
/// </summary>
/// <remarks>
/// It carries <see cref="IDialogService"/> because the shell is what owns a
/// window to hang dialogs off. Presenters that only need to ask a question are
/// handed that interface alone.
/// </remarks>
public interface IShellView : IControlView, IDialogService
{
    event Action NewRequested;

    event Action OpenRequested;

    /// <summary>
    /// The user asked to see the recovery snapshots earlier sessions left behind —
    /// the File menu's row, offered while there are any. What is offered and in
    /// what order is the presenter's; the row only says it was pressed.
    /// </summary>
    event Action RecoverRequested;

    event Action SaveRequested;

    event Action SaveAsRequested;

    /// <summary>
    /// Somebody else's voxel file read into the open document. Distinct from
    /// <see cref="OpenRequested"/>, which replaces the document: an import lands
    /// beside what is already there.
    /// </summary>
    event Action ImportRequested;

    /// <summary>
    /// A model export was asked for. The shell picks a path and format and runs the
    /// <c>export</c> command — there is no separate export logic here.
    /// </summary>
    event Action ExportRequested;

    /// <summary>
    /// The window was asked to close. Nothing has closed yet — the shell decides
    /// whether anything would be lost, then calls <see cref="Close"/>.
    /// </summary>
    event Action CloseRequested;

    event Action UndoRequested;

    event Action RedoRequested;

    /// <summary>
    /// Something new was asked for. Which kinds make sense depends on what is
    /// selected in the project tree, and the shell says so through
    /// <see cref="SetCreateEnabled"/> rather than the panel working it out.
    /// </summary>
    event Action<CreateKind> CreateRequested;

    /// <summary>
    /// Files were dropped onto the window. Reported raw, every path of them: what
    /// a dropped file means is the presenter's, and the window has no idea what
    /// this editor can stand for.
    /// </summary>
    event Action<IReadOnlyList<string>> FilesDropped;

    /// <summary>
    /// The whole-scene reference switch was pressed. Reported rather than acted
    /// on, the tool strip's own rule: what it becomes comes back through
    /// <see cref="SetReferencesVisible"/>, so the button can never disagree with
    /// the editor about which way it stands.
    /// </summary>
    event Action<bool> ReferencesVisibleRequested;

    event Action DeleteSelectionRequested;

    /// <summary>
    /// The selected row was asked to be copied. What may be copied, and what a
    /// paste then does with it, is the shell presenter's — this only reports the
    /// chord.
    /// </summary>
    event Action CopyRequested;

    /// <summary>Copy and remove, in that order and only if the removal is allowed.</summary>
    event Action CutRequested;

    event Action PasteRequested;

    /// <summary>Copy and paste in one press, leaving the clipboard alone.</summary>
    event Action DuplicateRequested;

    /// <summary>
    /// The project tree's "Replace uses…" on a material row, answered: give every
    /// cell wearing the first material the second. The menu names both; what that
    /// reaches is the presenter's.
    /// </summary>
    event Action<Guid, Guid> ReplaceMaterialUsesRequested;

    /// <summary>The application settings were asked for. Nothing is open yet.</summary>
    event Action PreferencesRequested;

    /// <summary>A render was asked for. Nothing is compiled or traced yet.</summary>
    event Action RenderRequested;

    /// <summary>
    /// A pin or fold click changed where a panel stands. The whole list, already
    /// applied on screen; the shell presenter's part is to persist it — the
    /// stored answer comes back through <see cref="SetPanelLayout"/>.
    /// </summary>
    event Action<IReadOnlyList<PanelPlacement>> PanelLayoutEdited;

    /// <summary>
    /// A resize drag let go: the side widths as the hand left them. Stored like
    /// a placement edit, and pushed back through <see cref="SetPanelWidths"/>.
    /// </summary>
    event Action<PanelWidths> PanelWidthsEdited;

    /// <summary>
    /// The materials a "Replace uses…" picker may offer — the project's own, as
    /// they stand at the moment the card is opened. The view lists names and
    /// reports an id, and never learns what a material is.
    /// </summary>
    Func<IReadOnlyList<MaterialChoice>>? MaterialChoices { get; set; }

    /// <summary>
    /// Whether a material row can have its uses replaced at all: a material some
    /// cell wears where the replace would reach, with another material to give
    /// them.
    /// </summary>
    /// <remarks>
    /// A question rather than pushed state, on <see cref="IHierarchyView.CanDelete"/>'s
    /// own terms and for a second reason of its own: the answer walks cells, and
    /// pushing it would run that walk on every history record and every frame of
    /// a colour drag to answer a menu nobody has opened. Asked while the menu is
    /// being built, it runs once per press.
    /// </remarks>
    Func<SelectionTarget, bool>? CanReplaceMaterialUses { get; set; }

    ISceneView Scene { get; }

    IHierarchyView Hierarchy { get; }

    IInspectorView Inspector { get; }

    IHistoryView History { get; }

    ISettingsView Settings { get; }

    IRenderView Render { get; }

    /// <summary>
    /// The chrome as something to read and to drive — the debug instrument the
    /// editor exercises its own interface with, not a way to work. See
    /// <see cref="IUiDriver"/>.
    /// </summary>
    IUiDriver UiDriver { get; }

    /// <summary>Already composed — the view does not know what a file name is.</summary>
    void SetWindowTitle(string title);

    void SetUndoState(bool canUndo, bool canRedo);

    void SetCreateEnabled(CreateKind kind, bool enabled);

    void SetDeleteEnabled(bool enabled);

    /// <summary>
    /// Whether the File menu's recovery row is live: it is while a snapshot
    /// nobody holds is waiting, and greyed otherwise, so a menu never offers a
    /// question with nothing behind it.
    /// </summary>
    void SetRecoverEnabled(bool enabled);

    /// <summary>Whether references are drawn, as the switch should show it.</summary>
    void SetReferencesVisible(bool visible);

    /// <summary>
    /// Whether the chrome animates at all, and how long its transitions take.
    /// Applied like the cube's corner: pushed whole on every preferences change,
    /// because comparing would be keeping a second copy of what they hold.
    /// </summary>
    void SetAnimation(bool enabled, AnimationSpeed speed);

    /// <summary>
    /// Which way round the chrome is drawn, and the colour it picks things out
    /// in. Pushed whole on every preferences change like the rest of the display
    /// settings; the view does nothing when the answer is the one already on
    /// screen.
    /// </summary>
    void SetSkin(EditorTheme theme, string accent);

    /// <summary>
    /// Whether the chrome's surfaces read the screen behind them and blur it. Off,
    /// they are one flat colour and the screen is not read at all.
    /// </summary>
    void SetGlassPanels(bool frosted);

    /// <summary>
    /// Which corner of the viewport the navigation cube sits in. Layout, so it is
    /// the shell's: the cube itself knows what it draws and nothing about where on
    /// screen it belongs.
    /// </summary>
    void SetViewCubeCorner(ViewCubeCorner corner);

    /// <summary>
    /// Where each chrome panel stands — see <c>docs/panels.md</c>. Applied like
    /// the cube's corner: pushed whole on every preferences change, because
    /// comparing would be keeping a second copy of what the preferences hold.
    /// </summary>
    void SetPanelLayout(IReadOnlyList<PanelPlacement> placements);

    /// <summary>The stored side widths, applied like the layout beside them.</summary>
    void SetPanelWidths(PanelWidths widths);

    /// <summary>Lets the window go, once the shell is satisfied nothing is lost.</summary>
    void Close();
}
