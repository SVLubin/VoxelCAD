using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Whether the handover dimmer owns a point, which is the decision that lets it
/// be solid to the machine and open to the agent at once. Solid everywhere, the
/// engine's mouse-over lands on the dimmer instead of on the panels — no
/// control lights up under the user's cursor and the cursor keeps its plain
/// arrow. Open in one place, and only while a gesture is being dispatched —
/// around the agent's own pointer, which the injector parks where it is about
/// to act before it acts there.
/// </summary>
/// <remarks>
/// The hole is <b>not</b> what keeps the user out. Their clicks are refused by
/// origin, at <c>ControlBanner._Input</c>, before GUI routing sees them: an
/// event without the synthetic mark is swallowed wherever it points, hole or no
/// hole. So a physical click that landed inside the hole is still refused, and
/// the two gates fail in opposite directions on purpose — a lost mark costs an
/// agent a gesture, never the user their protection.
/// <para>
/// Narrowing it is defence in depth rather than a fix. Both conditions have to
/// hold at once: an agent pointer to open around — absent for an agent that has
/// only issued semantic commands — and a gesture actually in flight, which is a
/// handful of frames per command. Between gestures the dimmer is solid glass.
/// What is left is a cosmetic residue, a physical cursor parked within a couple
/// of units of the agent's virtual one during those frames, where hover behaves
/// as if the dimmer were not there.
/// </para>
/// </remarks>
internal static class DimmerHitTest
{
    /// <summary>
    /// The open radius around the agent's pointer, in canvas units. Enough to
    /// absorb the float rounding between the injector's coordinate conversion
    /// and the engine's own, and small enough that the machine's cursor all but
    /// never stands inside it.
    /// </summary>
    public const float HoleRadius = 2f;

    public static bool Contains(Vector2 point, Vector2? agentPointer, bool gestureInFlight) =>
        !gestureInFlight
        || agentPointer == null
        || point.DistanceTo(agentPointer.Value) > HoleRadius;
}
