using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// Where a cursor click landed, reported rather than interpreted: the viewport
/// says which elements the ray crossed and where the click was, and a presenter
/// decides which of them the click meant.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="ScenePick"/> one level up: that one answers "which
/// cell of the target", this one "which element of the scene". They are separate
/// events rather than one because they are separate questions — a cell tool needs
/// the cell and never the list, and the cursor needs the list and never the cell.
/// </remarks>
/// <param name="Elements">
/// Every element the ray crossed that is actually on screen, nearest first. Empty
/// when the click landed on nothing — the walls, or the void past them.
/// </param>
/// <param name="Position">
/// Where the click was, in the coordinates input is aimed in. Carried because a
/// second click in the same spot means "the next one down", and only the party
/// holding that memory can tell one click from two.
/// </param>
/// <param name="Intent">
/// What the click's modifiers asked of the selection. Read off the event where it
/// arrived and carried rather than polled, because it is a fact about <em>this</em>
/// click: by the time a presenter answers, the key may well be up.
/// </param>
public readonly record struct ElementPick(
    IReadOnlyList<Guid> Elements,
    Vector2 Position,
    SelectionIntent Intent);
