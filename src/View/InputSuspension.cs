namespace VoxelCAD.View;

/// <summary>
/// Whether one input event is swallowed while an agent holds the editor.
/// </summary>
/// <remarks>
/// The rule the banner enforces is <b>not</b> "no input while an agent works" —
/// it is <b>"no input from the user"</b>. Those were the same sentence for as
/// long as an agent could only reach the editor over the command surface, and
/// they stopped being the same the day it could drive the interface: the right to
/// inject exists precisely when input is blocked, because that is when an agent
/// holds control.
/// <para>
/// So synthetic events pass and physical ones do not, with one carve-out — the
/// banner's own button, which is how the user stops an agent and must keep taking
/// their click. The failure mode is deliberately one-sided: an event that has
/// lost its mark reads as physical and is suspended, so a slip costs the agent a
/// gesture rather than costing the user their protection.
/// </para>
/// </remarks>
internal static class InputSuspension
{
    /// <param name="holdsControl">Whether an agent holds the editor at all.</param>
    /// <param name="isSynthetic">Whether the event was injected rather than typed or clicked.</param>
    /// <param name="isOverAction">
    /// Whether the event has a position and that position is on the banner's own
    /// button. False for anything without a position — a key cannot be aimed at
    /// the plaque, and the button is a mouse target for exactly that reason.
    /// </param>
    public static bool ShouldSuspend(bool holdsControl, bool isSynthetic, bool isOverAction) =>
        holdsControl && !isSynthetic && !isOverAction;
}
