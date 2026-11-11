using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The dimmer that suspends the interface while an agent holds the editor — and
/// the shield that keeps the machine's mouse-over off the panels underneath it.
/// It stops the mouse, so the engine's hover resolution lands here: nothing
/// lights up under the user's cursor, and the cursor stays the plain arrow
/// whatever field it is parked over. The agent is not stopped with them,
/// because the hit test opens around the injected pointer — see
/// <see cref="DimmerHitTest"/> — and the injector always moves that pointer to
/// where it is about to act.
/// </summary>
/// <remarks>
/// This used to be paint only, precisely because a hit test cannot ask where an
/// event came from and a solid shield would have eaten the agent's clicks with
/// the user's. The hole is what dissolves that argument: origin still cannot be
/// asked, but position can, and the one position that must stay open is the one
/// the injector is the sole writer of. Event suspension is unchanged — it stays
/// in <see cref="ControlBanner._Input"/>, which swallows the user's events
/// before GUI routing ever sees them; what the shield adds is the half no event
/// handler could reach, the engine's own per-frame hover and the cursor shape
/// that follows it.
/// </remarks>
internal sealed partial class AgentDimmer : ColorRect
{
    private readonly InjectedInputState _agent;

    /// <param name="agent">
    /// The agent's pointer, deliberately not <see cref="EditorInputState"/>: the
    /// hole must follow the injected pointer alone, whoever is driving — a
    /// machine-pointer hole would put the user's cursor through their own
    /// shield.
    /// </param>
    /// <summary>
    /// Whether it is refusing the mouse at all. Separate from being visible,
    /// because the two now end at different moments: the grey eases away over a
    /// fraction of a second and the editor comes back at once, and a user
    /// whose editor has just been handed back must not spend that fraction
    /// unable to touch it. Paint eases; refusal does not.
    /// </summary>
    public bool IsArmed { get; set; }

    public AgentDimmer(InjectedInputState agent)
    {
        _agent = agent;

        Name = "Dimmer";
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override bool _HasPoint(Vector2 point)
    {
        if (!IsArmed)
            return false;

        Vector2 canvasPoint = GetGlobalTransform() * point;

        return DimmerHitTest.Contains(
            canvasPoint,
            _agent.TryGetPointer(out Vector2 pointer) ? pointer : null,
            _agent.IsGestureInFlight);
    }
}
