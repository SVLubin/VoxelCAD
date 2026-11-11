using System;
using Godot;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// What a transform drag is currently proposing, as against what the model made of
/// it. The answer to "why is nothing happening while I pull": under a quantised
/// channel the element only moves as the pointer crosses a lattice point, and this
/// is what says how far past the last one the hand has gone.
/// </summary>
/// <remarks>
/// Only a presenter can form this: the raw offset is the viewport's, the applied
/// value is the model's, and the field mode that separates them is the model's too.
/// The view renders it and decides nothing — the same bargain the history panel's
/// labels are built under.
/// </remarks>
/// <param name="Values">
/// The transform the drag is asking for, unquantised on the channel being dragged
/// and the element's own applied values on the other two.
/// </param>
/// <param name="Pending">
/// Whether that differs from what the model applied. False under a channel the
/// model takes literally, where the ghost would sit exactly on the element and add
/// nothing but a depth fight.
/// </param>
/// <param name="Readout">
/// One short line: the applied value of each axis the drag has moved, and how far
/// past it the pointer is. Built by the presenter, so localisation stays out of the
/// view — and empty until something has actually been pulled.
/// </param>
/// <param name="PivotPoint">
/// The element's pivot in its own cells. <see cref="Values"/>.Position names where
/// this point stands, so the view needs it to place the ghost's node — the same
/// composition every element node gets.
/// </param>
public readonly record struct DragPreview(
    Guid ElementId,
    TransformValues Values,
    bool Pending,
    string Readout,
    Vector3 PivotPoint);
