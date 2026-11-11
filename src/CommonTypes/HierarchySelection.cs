using System.Collections.Generic;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// What a click in the project tree named, and how. Reported rather than applied:
/// the panel says which rows the gesture picked out and what it is asking for, and
/// what the selection becomes is decided a layer up — by the same function the
/// viewport's own clicks go through.
/// </summary>
/// <remarks>
/// The panel deliberately does <b>not</b> report the resulting selection. It knows
/// which of its rows are drawn selected, so it could work one out, and then the
/// rule for what a toggle means would exist in two places that could come to
/// disagree — the same trap <see cref="SelectionIntent"/> exists to avoid.
/// </remarks>
/// <param name="Targets">
/// The rows the gesture named: one, or — under <see cref="SelectionIntent.Extend"/>
/// — the whole run from the anchor to the row clicked, in the order they are drawn.
/// </param>
/// <param name="Active">
/// The row actually clicked. It is what the panels downstream speak for, and it is
/// what decides the selection's kind: the tree holds scenes, elements and materials,
/// and a run that crossed from one category into another is not a selection anybody
/// could act on.
/// </param>
public readonly record struct HierarchySelection(
    IReadOnlyList<SelectionTarget> Targets,
    SelectionTarget Active,
    SelectionIntent Intent);
