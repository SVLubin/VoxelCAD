using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// One brush stroke, from the press that placed its first cell to the release
/// that closes it. On every frame the button is held the viewport asks the
/// hover which cell it proposes; this decides whether that cell is new to the
/// stroke, and remembers every cell the stroke has written so that the surface
/// the stroke paints on can go on being read as it stood when the stroke began.
/// A plain object rather than anything in the node, per the standing rule: the
/// decisions are testable, the node only applies them.
/// </summary>
/// <remarks>
/// The mode is latched at the press: a stroke places or it erases for its whole
/// length, whatever Shift does meanwhile. Changing mode is releasing and pressing
/// again — the press/release pair is the gesture's boundary, and one gesture is
/// one record.
/// <para>
/// The written cells are the stroke's own surface, and that is the one rule a
/// stroke needs beyond the hover's. Placing a cell puts a voxel exactly where the
/// pointer's ray was landing, so on the next frame the same ray meets the new
/// cell and the hover proposes the cell in front of <em>that</em>: a stroke moving
/// slowly across a floor would raise a ridge of towers under the hand. Erasing is
/// the mirror image — the ray reaches through the cleared cell to the one behind
/// it and digs a tunnel wherever the hand pauses. So the picker is told to read
/// the cells this stroke placed as still empty, and the cells it erased as still
/// filled: the stroke paints on the surface it started on, which is what every
/// reference editor's stroke does, and the hover's rule needs no second rule
/// beside it.
/// </para>
/// </remarks>
internal sealed class BrushStroke
{
    private readonly HashSet<Vector3I> _writtenHashSet = new();

    public BrushStroke(bool alternate) => Alternate = alternate;

    /// <summary>Erasing rather than placing, decided at the press and kept.</summary>
    public bool Alternate { get; }

    /// <summary>Every cell this stroke has written, the mirror images included.</summary>
    public IReadOnlySet<Vector3I> Written => _writtenHashSet;

    /// <summary>
    /// Whether the proposed cell is new to the stroke. A cell already written —
    /// the pointer resting, or coming back over its own track — is not written
    /// twice, and the folding in the model would make the second write a no-op
    /// anyway; refusing it here is what keeps the surface rule honest, since a
    /// re-proposed cell is exactly one the frozen surface is answering for.
    /// </summary>
    public bool IsNew(Vector3I cell) => !_writtenHashSet.Contains(cell);

    /// <summary>Records the cells one step of the stroke wrote.</summary>
    public void Record(IReadOnlyList<Vector3I> cells)
    {
        foreach (Vector3I cell in cells)
            _writtenHashSet.Add(cell);
    }
}
