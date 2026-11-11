using System.Collections.Generic;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// What one reading of the control tree found: the controls themselves, in draw
/// order, and how many were in scope before the cap was applied.
/// </summary>
/// <remarks>
/// The count is carried separately so the layer that refuses an over-large answer
/// can name the arguments that would window it — those are the command surface's
/// words, and the view that did the walking has no business knowing them.
/// </remarks>
/// <param name="Controls">
/// In draw order: a control listed later is drawn over one listed earlier, which
/// is what makes the last match the topmost one at a point.
/// </param>
/// <param name="TotalInScope">
/// How many controls matched the options. Larger than <see cref="Controls"/> only
/// when the cap cut the answer short.
/// </param>
public readonly record struct UiSnapshot(
    IReadOnlyList<UiControl> Controls,
    int TotalInScope,
    int Limit);
