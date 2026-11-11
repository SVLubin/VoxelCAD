namespace VoxelCAD.View;

/// <summary>
/// How much text the agent plaque can actually show, so the command surface can
/// recommend a length rather than let an agent find the limit by watching its own
/// words end in an ellipsis.
/// </summary>
/// <remarks>
/// Public for that one reason: the numbers are a fact about the plaque's layout —
/// its width and the size it draws the topic at — and the only alternative to
/// quoting them is writing a second copy of them into a tool description, where
/// nothing would keep the two in step.
/// <para>
/// They are measured, not derived: the topic is proportional text, so a line's
/// worth depends on which characters they are. The figures are the safe end of
/// what fit when looked at, which is what a recommendation should be.
/// </para>
/// </remarks>
public static class PlaqueBudget
{
    /// <summary>About what fits on one line of the topic beside the icon and the clock.</summary>
    public const int TopicLineCharacters = 40;

    /// <summary>
    /// About what fits before the topic is trimmed with an ellipsis. The topic gets
    /// two lines, and a third line below it belongs to the queue notice — so this is
    /// the budget whether or not anybody is waiting.
    /// </summary>
    public const int TopicCharacters = TopicLineCharacters * 2;
}
