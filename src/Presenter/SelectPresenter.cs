using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// Turns a cursor click in the viewport into a selection. The view reports which
/// elements the ray crossed; deciding that the nearest one is meant — and that a
/// repeat click in the same spot means the one behind it — happens here.
/// </summary>
/// <remarks>
/// A sibling of the brush and the gizmo on the same terms: three presenters share
/// the viewport and none of them knows the others. Which of them a plain left click
/// belongs to is not arbitrated between them — the active tool is session state, and
/// each listener checks it.
/// <para>
/// It writes <see cref="EditorSession.Selection"/> and nothing else, so the project
/// tree's row, the inspector, the selection border and the brush's target all follow
/// through the ordinary channel: clicking a model in the viewport is the same event
/// as clicking its row, arriving from the other side.
/// </para>
/// </remarks>
internal sealed class SelectPresenter : PresenterBase
{
    private readonly IScene _scene;
    private readonly ISceneView _view;
    private readonly EditorSession _session;
    private readonly SelectionCycle _cycle = new();

    /// <summary>
    /// Raised while this presenter writes the selection, so the change it causes is
    /// told apart from one made anywhere else.
    /// </summary>
    private bool _selecting;

    public SelectPresenter(IScene scene, ISceneView view, EditorSession session)
    {
        _scene = scene;
        _view = view;
        _session = session;

        _view.ElementPicked += OnElementPicked;
        _view.KeyboardLost += OnKeyboardLost;
        _session.SelectionChanged += OnSelectionChanged;
    }

    protected override void OnDispose()
    {
        _view.ElementPicked -= OnElementPicked;
        _view.KeyboardLost -= OnKeyboardLost;
        _session.SelectionChanged -= OnSelectionChanged;
    }

    /// <summary>
    /// A click that crossed nothing selects the scene, rather than clearing the
    /// selection to nothing at all. The scene is a row of the project tree like any
    /// other and the thing the click did land on — the room — so the panel keeps
    /// saying where the user is instead of going blank.
    /// </summary>
    /// <remarks>
    /// Answered under every tool that selects, not only the cursor
    /// (<see cref="SelectingTools"/>): under the transform tools a press reaches
    /// here only once the gizmo has refused it, so a click that missed the handles
    /// picks what is under it — the same gesture every editor with handles has.
    /// The tools that aim at cells are not among them, and the viewport reports
    /// them no pick at all.
    /// </remarks>
    private void OnElementPicked(ElementPick pick)
    {
        // A change can rebuild this presenter part-way through its own dispatch;
        // see PresenterBase.
        if (!IsAlive || !SelectingTools.IsSelectingTool(_session.ActiveTool))
            return;

        // What the ray crossed, mapped to what a click on it may act on: an
        // instance's contents stand for the instance.
        IReadOnlyList<Guid> elementsList = SelectableElements.Resolve(_scene, pick.Elements);

        // A plain click on what is already selected changes nothing. Most often it
        // is the click that takes the keyboard back from a panel to the scene, and
        // it must not collapse a selection of several to one. "Already selected" is
        // asked of what the click means — a model inside a prefab instance means
        // the instance, which is the thing that can be moved, so a model picked in
        // the tree does give way to it. The spot is remembered, so a second click
        // there still walks the stack from what is selected.
        if (pick.Intent == SelectionIntent.Replace
            && !_cycle.IsRepeat(pick.Position)
            && IsNearestSelected(pick, elementsList))
        {
            _cycle.Remember(pick.Position);

            return;
        }

        _selecting = true;

        try
        {
            if (ChooseElement(pick, elementsList) is not { } id)
            {
                // A modifier that named nothing leaves the selection alone: the hand
                // was adding to it and missed, which is not the same sentence as
                // "select the room".
                if (pick.Intent == SelectionIntent.Replace)
                    _session.SetSelection(new SelectionTarget(SelectionKind.Scene, _scene.Id));

                return;
            }

            _session.SetSelection(SelectionEdit.Apply(
                _session.Selection,
                new[] { new SelectionTarget(SelectionKind.Element, id) },
                new SelectionTarget(SelectionKind.Element, id),
                pick.Intent));
        }
        finally
        {
            _selecting = false;
        }
    }

    /// <summary>
    /// A selection made anywhere else — a row of the project tree, an undo, a
    /// deletion — ends the walk: the next click in the same spot is a first click,
    /// not a repeat of one made before the selection moved.
    /// </summary>
    private void OnSelectionChanged()
    {
        if (!_selecting)
            _cycle.Forget();
    }

    /// <summary>
    /// So does the keyboard leaving the viewport, which is what catches a click in
    /// the tree that changed nothing: the click that brings the keyboard back is a
    /// first click wherever it lands.
    /// </summary>
    private void OnKeyboardLost() => _cycle.Forget();

    /// <summary>
    /// Whether what the click means — the nearest thing under the pointer, as the
    /// instance it is the contents of when it is — is selected already.
    /// </summary>
    private bool IsNearestSelected(ElementPick pick, IReadOnlyList<Guid> elementsList) =>
        elementsList.Count > 0
        && _session.Selection.Contains(new SelectionTarget(SelectionKind.Element, elementsList[0]));

    /// <summary>
    /// Which of the elements under the pointer the click meant. Walking a stack by
    /// clicking the same pixel again is a <see cref="SelectionIntent.Replace"/>
    /// affair only: a modifier click means the thing you can see, and clicking it a
    /// second time with the command modifier has to take it back out of the
    /// selection rather than reach past it, or the modifier stops meaning toggle.
    /// </summary>
    private Guid? ChooseElement(ElementPick pick, IReadOnlyList<Guid> elementsList)
    {
        if (pick.Intent == SelectionIntent.Replace)
            return _cycle.Choose(elementsList, pick.Position, FindActiveElement());

        _cycle.Forget();

        return elementsList.Count > 0 ? elementsList[0] : null;
    }

    /// <summary>
    /// What the cycle steps from: the active element of this scene, as the stack of
    /// hits names it — a model inside a prefab instance is that instance there — or
    /// nothing, a scene or a material row being no place in a stack of hits.
    /// </summary>
    private Guid? FindActiveElement()
    {
        SelectionTarget selection = _session.ActiveTarget;

        if (selection.Kind != SelectionKind.Element
            || !_scene.TryGetElement(selection.Id, out _))
        {
            return null;
        }

        return SelectableElements.OwnerOf(_scene, selection.Id);
    }
}
