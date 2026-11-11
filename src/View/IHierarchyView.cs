using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The project panel. Knows nothing about documents, scenes or materials — it is
/// fed one change at a time and reports rows back as bare targets.
/// </summary>
/// <remarks>
/// Rows are addressed by <see cref="SelectionTarget"/> rather than by id, because
/// the tree holds more than one sort of thing and the category rows are not things
/// at all. What a row means is entirely the presenter's business.
/// </remarks>
public interface IHierarchyView
{
    /// <summary>
    /// Rows were picked out — by click, by a modifier click, or by an arrow key.
    /// What the selection becomes is decided a layer up, by the same function the
    /// viewport's own clicks go through; see <see cref="HierarchySelection"/>.
    /// </summary>
    event Action<HierarchySelection> SelectionRequested;

    /// <summary>
    /// A row was double-clicked, or Enter was pressed on it. The panel does not
    /// know what activating something means; it only reports that it was asked.
    /// </summary>
    event Action<SelectionTarget> ItemActivated;

    /// <summary>
    /// A tool key was pressed while this panel held the keyboard. Reported exactly
    /// as the viewport reports its own: the panel knows neither which tools exist
    /// beyond their names, nor whether the selected row is something a tool could
    /// act on, so what it means is decided a layer up.
    /// </summary>
    event Action<ToolKind> ToolRequested;

    /// <summary>
    /// The eye at the right edge of a row was clicked. Reported with the same
    /// ignorance as every other row event: the panel knows that the eye was
    /// pressed, not what the row's element is currently set to, so what the new
    /// state should be is worked out a layer up.
    /// </summary>
    event Action<SelectionTarget> VisibilityToggled;

    /// <summary>
    /// The bin at the right edge of a row was clicked. What it means is a layer
    /// up, like every other row event — the panel has never heard of a scene, an
    /// element or a material, let alone of what removing one costs.
    /// </summary>
    event Action<SelectionTarget> DeleteRequested;

    /// <summary>
    /// A row was dragged onto a place in the tree. Reported like every other row
    /// event and with the same ignorance: the panel says what landed where, and
    /// whether that is a legal thing to do to the document is decided a layer up.
    /// </summary>
    event Action<HierarchyDrop> ItemDropped;

    /// <summary>
    /// Whether a drop would be allowed, asked while the row is still in the air.
    /// A drag has to show the user its answer before the button comes up, and the
    /// panel cannot work it out — an element may not live under a material, and
    /// the panel has never heard of either.
    /// </summary>
    /// <remarks>
    /// A property rather than an event because it is a question, not news: exactly
    /// one answer, needed synchronously. <c>null</c> refuses every drop, which is
    /// the right default for a panel nobody has bound yet.
    /// </remarks>
    Func<HierarchyDrop, bool>? CanDrop { get; set; }

    /// <summary>
    /// Whether a row may be picked up at all, asked before a drag starts. Without
    /// it every row started one — a material rode the pointer under a forbidden
    /// cursor to a drop that could never land, which is a promise the panel had
    /// no business making. Same terms as <see cref="CanDrop"/>: a question, one
    /// answer, <c>null</c> refuses everything.
    /// </summary>
    Func<SelectionTarget, bool>? CanDrag { get; set; }

    /// <summary>
    /// Whether the row's bin would do anything, asked as the pointer arrives on
    /// the row and the bin is about to be drawn. A "no" is drawn greyed rather
    /// than hidden: a bin that vanished on the rows that refuse it would leave
    /// the user guessing whether the panel had noticed the row at all.
    /// </summary>
    /// <remarks>
    /// A question rather than pushed state, for a reason the other two do not
    /// have: the answer changes with things nothing reports. Whether a material is
    /// still painted onto a voxel moves with every brush stroke, and refreshing
    /// every material row on every voxel edit — to redraw a bin that is not on
    /// screen — is worse than asking once when the pointer arrives. Same terms as
    /// <see cref="CanDrop"/>: one answer, needed synchronously, <c>null</c>
    /// refuses everything.
    /// </remarks>
    Func<SelectionTarget, bool>? CanDelete { get; set; }

    void Clear();

    void SetRoot(SelectionTarget target, string name);

    /// <summary>
    /// Adds a row. <paramref name="component"/> says what occupies an element's
    /// component slot — it is what tells a grouping row from a voxel model, which
    /// the target's kind alone cannot; rows that are not elements pass
    /// <see cref="ComponentKind.None"/>.
    /// </summary>
    void AddItem(
        SelectionTarget target,
        SelectionTarget parent,
        string name,
        int index,
        ComponentKind component);

    /// <summary>Removes the row and whatever is nested under it.</summary>
    void RemoveItem(SelectionTarget target);

    void RenameItem(SelectionTarget target, string name);

    /// <summary>
    /// Scrolls a row into view. Called for a row just added so the newest one is
    /// visible without the user reaching for the scrollbar; a no-op if the row does
    /// not exist or already fits on screen.
    /// </summary>
    void ScrollTo(SelectionTarget target);

    /// <summary>
    /// Draws a row as the current one. Used for the scene being worked on, which
    /// is the only row whose contents are shown.
    /// </summary>
    void SetItemEmphasized(SelectionTarget target, bool emphasized);

    /// <summary>Gives a row a colour chip — a material is largely what it looks like.</summary>
    void SetItemSwatch(SelectionTarget target, Color color);

    /// <summary>
    /// Gives a row an eye at its right edge and says how far what it names
    /// reaches. This is <b>not</b> about the row: the row is drawn either way. A
    /// crossed eye is drawn always, an open one only while the pointer is on the
    /// row — the first is a state, the second an offer — and clicking either
    /// reports <see cref="VisibilityToggled"/>. A row that reaches nothing, by its
    /// own flag or an ancestor's, recedes.
    /// </summary>
    /// <remarks>
    /// One call rather than two setters, because the eye and the recession are
    /// two readings of one fact and a row caught between them would be a lie.
    /// Called only for rows that stand for something hideable, which is the
    /// presenter's judgement: a category row is the panel's own structure and a
    /// material is not somewhere geometry lives.
    /// </remarks>
    void SetItemVisibility(SelectionTarget target, RowVisibility visibility);

    /// <summary>
    /// Gives a row a bin, to the right of whatever else it carries. Drawn only
    /// while the pointer is on the <b>row</b> — not on the bin, which would be a
    /// thing nobody could find — and greyed there when <see cref="CanDelete"/>
    /// says no. Clicking it reports <see cref="DeleteRequested"/>.
    /// </summary>
    /// <remarks>
    /// Says whether the row stands for something that can be deleted <em>at
    /// all</em>, which is a fact about what kind of row it is: the project's own
    /// row and the three category rows are the panel's structure and stand for
    /// nothing that could go. Whether the bin is live when the pointer arrives is
    /// the separate, changing question <see cref="CanDelete"/> answers.
    /// </remarks>
    void SetItemDeletable(SelectionTarget target, bool deletable);

    /// <summary>
    /// Draws these rows as the selected ones and no others.
    /// <paramref name="active"/> is the one a Shift range is measured from, and it
    /// is expected to be among <paramref name="targets"/>; rows the panel does not
    /// hold are ignored, which is what a selection naming another scene's elements
    /// comes to.
    /// </summary>
    void SetSelection(IReadOnlyList<SelectionTarget> targets, SelectionTarget active);
}
