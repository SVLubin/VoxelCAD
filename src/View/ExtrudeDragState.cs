using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Geometry;

namespace VoxelCAD.View;

/// <summary>
/// One extrude gesture, from the press that grabbed a face to the release that
/// answers with a layer count. The viewport projects the pointer onto the face's
/// normal and feeds the offsets in; this decides what they mean — which is what
/// keeps the deciding out of the node, per the standing rule.
/// </summary>
/// <remarks>
/// A gesture and a click are told apart by whether the pull ever reached a whole
/// layer, and the distinction is latched: a drag pulled out and brought home
/// resolves to <b>zero</b> — the hand changed its mind, and the honest answer is
/// nothing — while a press that never travelled resolves to the click's own
/// ±1. Without the latch the two are indistinguishable, and a cancelled drag
/// would land as the very click it was retreating from.
/// <para>
/// Offsets arrive in <b>cells</b>: the viewport projects against the unnormalized
/// world vector of one cell step along the face normal, the gizmo's own
/// convention, so a scaled element needs no correction here or anywhere else.
/// </para>
/// </remarks>
internal sealed class ExtrudeDragState
{
    private readonly float _baseOffset;
    private bool _becameGesture;

    /// <param name="pick">The press that opened the gesture: seed cell, face, modifier.</param>
    /// <param name="baseOffset">The projection of the press itself, in cells.</param>
    public ExtrudeDragState(ScenePick pick, float baseOffset)
    {
        Pick = pick;
        _baseOffset = baseOffset;
    }

    public ScenePick Pick { get; }

    /// <summary>What the pull currently asks for, in whole layers. Zero until it reaches one.</summary>
    public int PendingLayers { get; private set; }

    /// <summary>
    /// Feeds the pointer's current projection in, and answers whether the pending
    /// count changed — which is when the preview is worth rebuilding, and the only
    /// time it is.
    /// </summary>
    public bool Update(float offsetCells)
    {
        // Explicit away-from-zero: Godot's own rounding has been caught doing
        // banker's halves before (see the tests ledger), and a drag that snapped
        // at 2.5 cells one way and 3.5 the other would read as haunted.
        var pending = (int)MathF.Round(offsetCells - _baseOffset, MidpointRounding.AwayFromZero);

        if (pending != 0)
            _becameGesture = true;

        if (pending == PendingLayers)
            return false;

        PendingLayers = pending;

        return true;
    }

    /// <summary>
    /// The gesture's answer, read at release. A pull that reached a layer means
    /// what it says — including zero for one brought home. A press that never
    /// travelled is the click, one layer, the modifier picking the direction.
    /// </summary>
    public int ResolveLayers()
    {
        if (_becameGesture)
            return PendingLayers;

        return Pick.Alternate ? -VoxelFacePatch.ClickLayers : VoxelFacePatch.ClickLayers;
    }
}
