using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Presenter;

/// <summary>
/// Which of the elements under the pointer a click means. The nearest one, unless
/// the click repeats a click in the same spot — then the one behind whatever is
/// selected now, so a stack of parts is walked through by clicking the same pixel
/// again.
/// </summary>
/// <remarks>
/// It remembers the position and nothing else. What the previous click <em>chose</em>
/// is read from the live selection instead, so the cycle steps from what is selected
/// now, never from what this object last said. A selection changed elsewhere — the
/// project tree, an undo — ends the walk outright: its owner calls
/// <see cref="Forget"/>, because a click after it is a first click whatever pixel it
/// lands on. Its own memory is the one thing nobody else holds, which is the whole
/// reason it is an object rather than a function.
/// </remarks>
internal sealed class SelectionCycle
{
    /// <summary>
    /// How far a second click may land from the first and still count as the same
    /// spot, in the pixels input is aimed in. Not zero, because a hand holding a
    /// mouse still moves it by a pixel between two clicks meant for one place, and
    /// small, because two clicks a few pixels apart on a crowded model can honestly
    /// mean two different elements.
    /// </summary>
    private const float SameSpotPixels = 3f;

    private Vector2 _position;
    private bool _hasPosition;

    /// <summary>
    /// Drops the remembered spot, so the next click starts a walk rather than
    /// continuing one. What a modifier click needs: it means the thing under the
    /// pointer and never the thing behind it — clicking the same pixel twice with
    /// the command modifier has to take back what the first one added, or the
    /// modifier stops meaning toggle — and the plain click after it should then
    /// begin at the front rather than resume a walk nobody was on.
    /// </summary>
    public void Forget() => _hasPosition = false;

    /// <summary>Whether a click here repeats the remembered one. Records nothing.</summary>
    public bool IsRepeat(Vector2 position) =>
        _hasPosition && _position.DistanceTo(position) <= SameSpotPixels;

    /// <summary>
    /// Records a click that chose nothing new — one that landed on what was already
    /// selected — so a second click in the same spot walks on from there.
    /// </summary>
    public void Remember(Vector2 position)
    {
        _position = position;
        _hasPosition = true;
    }

    /// <summary>
    /// The element the click means, or null when it crossed nothing at all — which
    /// is an answer rather than an absence, and the caller has something to do with
    /// it.
    /// </summary>
    public Guid? Choose(IReadOnlyList<Guid> hits, Vector2 position, Guid? selected)
    {
        bool sameSpot = IsRepeat(position);

        Remember(position);

        if (hits.Count == 0)
            return null;

        if (!sameSpot || selected is not { } current)
            return hits[0];

        int index = IndexOf(hits, current);

        // Selected, but not by this ray — the pointer has been moved onto a
        // different stack, or the selection was made elsewhere. Start over from the
        // front rather than pretending to continue a walk through this one.
        return index < 0 ? hits[0] : hits[(index + 1) % hits.Count];
    }

    private static int IndexOf(IReadOnlyList<Guid> hits, Guid id)
    {
        for (var index = 0; index < hits.Count; index++)
        {
            if (hits[index] == id)
                return index;
        }

        return -1;
    }
}
