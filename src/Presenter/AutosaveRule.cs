namespace VoxelCAD.Presenter;

/// <summary>
/// The whole trigger decision as a pure function, so the three rules the user
/// set — an interval, only when something changed, never mid-gesture — and the
/// two that follow from them are a table test rather than a timer test.
/// </summary>
/// <remarks>
/// First match wins, in this order:
/// <list type="number">
/// <item>Off: nothing is written and the clock restarts — switching back on
/// starts a whole interval rather than writing at once.</item>
/// <item>Not due: wait.</item>
/// <item>Not modified: the document equals its file (an undo back to the saved
/// point), so a snapshot would only put undone work back — drop one if there
/// is one, else restart.</item>
/// <item>Modified but the record has not moved since the last snapshot (an undo
/// and redo landing on the same record counts as not moved): restart.</item>
/// <item>Blocked — a gesture in flight or a busy user — and not yet overdue:
/// wait. The deferral is bounded: past one further interval the write goes
/// through the gesture without ending it, because every edit already went
/// through the model and a colour picker left open must not leave an hour of
/// work unprotected.</item>
/// <item>Otherwise: write.</item>
/// </list>
/// </remarks>
internal static class AutosaveRule
{
    /// <summary>How many intervals a deferral may last before the write goes through anyway.</summary>
    public const double OverdueIntervals = 2.0;

    public static AutosaveAction Decide(
        bool enabled,
        bool due,
        bool isModified,
        bool recordMoved,
        bool blocked,
        bool overdue,
        bool hasSnapshot)
    {
        if (!enabled)
            return AutosaveAction.Rearm;

        if (!due)
            return AutosaveAction.Wait;

        if (!isModified)
            return hasSnapshot ? AutosaveAction.Drop : AutosaveAction.Rearm;

        if (!recordMoved)
            return AutosaveAction.Rearm;

        if (blocked && !overdue)
            return AutosaveAction.Wait;

        return AutosaveAction.Write;
    }
}
