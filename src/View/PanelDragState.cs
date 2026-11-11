using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>What the end of a press turned out to mean.</summary>
internal enum PanelDragOutcome
{
    /// <summary>Nothing was held.</summary>
    None,

    /// <summary>Held and let go without travelling: an ordinary click.</summary>
    Click,

    /// <summary>Held, carried, and put down somewhere.</summary>
    Drop
}

/// <summary>
/// A tab being carried: pressed, perhaps dragged, then let go. Every decision
/// about which of those it is lives here rather than in the node, so the one
/// thing that separates a click from a drag is a number a test can reach.
/// </summary>
/// <remarks>
/// Fed by polling <see cref="IInputState"/> on a frame tick, not by Godot's own
/// drag and drop. Two reasons, and the second is the one that settles it: the
/// engine's `Tree` clears its drop flags after every gesture, which this
/// codebase has already paid for once (see the ledger); and a polled gesture is
/// one an agent can perform with `input-drag`, where a `_GetDragData` handshake
/// is reachable only by a real mouse.
/// </remarks>
internal sealed class PanelDragState
{
    /// <summary>
    /// How far the pointer must travel before a press stops being a click. A
    /// hand that presses and lets go rarely holds perfectly still, and a tab
    /// that opened *or* moved depending on a pixel would be a coin toss.
    /// </summary>
    public const float Threshold = 4f;

    private Vector2 _origin;

    public PanelKind? Held { get; private set; }

    public bool IsDragging { get; private set; }

    /// <summary>Where the pointer is now, which is where a carried tab rides.</summary>
    public Vector2 Pointer { get; private set; }

    /// <summary>How far the tab has been carried from where it was picked up.</summary>
    public Vector2 Travel => Pointer - _origin;

    public void Press(PanelKind kind, Vector2 at)
    {
        Held = kind;
        IsDragging = false;
        _origin = at;
        Pointer = at;
    }

    public void MoveTo(Vector2 at)
    {
        if (Held is null)
            return;

        Pointer = at;

        // Past the threshold it is a drag and stays one, however far back
        // towards the start the hand then comes: a gesture that changed its
        // mind about what it was halfway through would be unusable.
        if (!IsDragging && (at - _origin).Length() > Threshold)
            IsDragging = true;
    }

    public PanelDragOutcome Release()
    {
        if (Held is null)
            return PanelDragOutcome.None;

        PanelDragOutcome outcome =
            IsDragging ? PanelDragOutcome.Drop : PanelDragOutcome.Click;

        Cancel();

        return outcome;
    }

    public void Cancel()
    {
        Held = null;
        IsDragging = false;
    }
}
