using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>What one tick of the pointer's world asks the shell to do.</summary>
internal readonly record struct HoverDecision(PanelKind? Open, bool Close);

/// <summary>
/// The Arc-style hover rules of <c>docs/panels.md</c>, as a pure per-tick
/// machine: a dwell on a tab means "open", leaving the panel and its tab for a
/// grace period means "close", and nothing closes while the hand is still in
/// it. Fed from <see cref="IInputState"/> on a frame tick, never from engine
/// hover — the engine's follows the machine's cursor, which would let a
/// bystander's mouse close a driving agent's panel.
/// </summary>
internal sealed class PanelHoverIntent
{
    /// <summary>Long enough that crossing a tab on the way elsewhere opens nothing.</summary>
    public const float OpenDwellSeconds = 0.2f;

    /// <summary>Generous, so grazing the panel's edge does not snatch it away.</summary>
    public const float CloseGraceSeconds = 0.35f;

    /// <summary>
    /// The editor's pace, which these two thresholds are measured at.
    /// </summary>
    /// <remarks>
    /// A dwell is not a transition, and it was left out of the pace setting for
    /// that reason — wrongly, because it is the one thing left in this chrome
    /// that the eye reads as the interface taking its time. With animation
    /// switched off every surface lands at once and then a fifth of a second
    /// passes before a glance is believed, which is exactly the "there are
    /// still delays somewhere" the setting exists to answer. So both go through
    /// <see cref="MotionTiming.ScaleFixed"/> like the view cube's turn: nought
    /// when animation is off, and longer at the slower paces, where an editor
    /// that moves deliberately should also make up its mind deliberately.
    /// <para>
    /// The cost of nought is real and is the point: with animation off, sweeping
    /// the pointer down a rail opens each tab it crosses for the frame or two it
    /// is over it. That is what "no dwell" means, and a floor here would be a
    /// number nobody chose standing in the way of a setting somebody did.
    /// </para>
    /// </remarks>
    private readonly MotionTiming _timing;

    private PanelKind? _dwellKind;
    private float _dwellSeconds;
    private float _awaySeconds;
    private PanelKind? _suppressed;

    public PanelHoverIntent(MotionTiming timing) => _timing = timing;

    /// <summary>
    /// Holds hover off one panel until the pointer leaves its tab and comes
    /// back. A click is a decision, and the pointer is still sitting on the
    /// button that answered it: without this, unpinning a panel hands it
    /// straight back as a hover glance, and the click reads as having done
    /// nothing. Cleared by leaving, never by time — the hand has to move.
    /// </summary>
    public void SuppressUntilPointerLeaves(PanelKind kind) => _suppressed = kind;

    /// <param name="hoveredTab">The unpinned tab under the pointer, if any.</param>
    /// <param name="openTemp">The temporary panel currently open, if any.</param>
    /// <param name="pointerOnOpenPanel">
    /// Whether the pointer stands on the open panel itself or on its own tab.
    /// </param>
    /// <param name="interactionHeld">
    /// Whether the hand is still in the panel by any other measure — keyboard
    /// focus inside it, a mouse button down, a picker or dialog up. While this
    /// holds, the panel never closes: a retreat mid-edit is worse than a panel
    /// that overstays.
    /// </param>
    public HoverDecision Tick(
        float deltaSeconds,
        PanelKind? hoveredTab,
        PanelKind? openTemp,
        bool pointerOnOpenPanel,
        bool interactionHeld)
    {
        PanelKind? open = TickDwell(deltaSeconds, hoveredTab, openTemp);
        bool close = TickAway(deltaSeconds, hoveredTab, openTemp, pointerOnOpenPanel, interactionHeld);

        // An open decided this tick supersedes a close: switching tabs is one
        // motion, not a close and a separate open.
        return new HoverDecision(open, close && open is null);
    }

    private PanelKind? TickDwell(float deltaSeconds, PanelKind? hoveredTab, PanelKind? openTemp)
    {
        if (_suppressed is PanelKind held)
        {
            if (hoveredTab == held)
                return null;

            _suppressed = null;
        }

        if (hoveredTab is not PanelKind kind || hoveredTab == openTemp)
        {
            _dwellKind = null;
            _dwellSeconds = 0f;

            return null;
        }

        if (_dwellKind != kind)
        {
            _dwellKind = kind;
            _dwellSeconds = 0f;
        }

        _dwellSeconds += deltaSeconds;

        if (_dwellSeconds < _timing.ScaleFixed(OpenDwellSeconds))
            return null;

        _dwellKind = null;
        _dwellSeconds = 0f;

        return kind;
    }

    private bool TickAway(
        float deltaSeconds,
        PanelKind? hoveredTab,
        PanelKind? openTemp,
        bool pointerOnOpenPanel,
        bool interactionHeld)
    {
        if (openTemp is null)
        {
            _awaySeconds = 0f;

            return false;
        }

        bool inside = pointerOnOpenPanel || hoveredTab == openTemp || interactionHeld;

        if (inside)
        {
            _awaySeconds = 0f;

            return false;
        }

        _awaySeconds += deltaSeconds;

        if (_awaySeconds < _timing.ScaleFixed(CloseGraceSeconds))
            return false;

        _awaySeconds = 0f;

        return true;
    }
}
