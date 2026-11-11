using System;
using System.Collections.Generic;

namespace VoxelCAD.Presenter;

/// <summary>
/// What one agent is told about the editor's control, in its own terms. The
/// arbiter's state is global; this is the caller's view of it, and the two differ
/// in exactly the place that matters: an editor granted to somebody else must
/// never read as granted.
/// </summary>
/// <remarks>
/// The words are the wire contract, so they live in one function that both
/// <c>control-status</c> and a resolved wait answer through — a second copy would
/// be a second vocabulary. <c>youHoldIt</c> is the field to branch on: it is the
/// one that says whether editing is allowed, and it cannot be misread the way a
/// status string can.
/// </remarks>
internal static class ControlStatusReport
{
    /// <summary>The status an agent that holds the editor is told.</summary>
    public const string Granted = "Granted";

    /// <summary>Its own request is being negotiated with the user.</summary>
    public const string Pending = "Pending";

    /// <summary>Another agent has it, and this one is standing in line for it.</summary>
    public const string Waiting = "Waiting";

    /// <summary>Another agent has it, and this one is not in the line.</summary>
    public const string HeldByAnotherAgent = "HeldByAnotherAgent";

    /// <summary>The user has it and nobody is asking.</summary>
    public const string Available = "Available";

    public const string Denied = "Denied";
    public const string TimedOut = "TimedOut";

    public static Dictionary<string, object?> Build(
        ControlArbiter arbiter,
        string clientId,
        double heldSeconds,
        ControlQueueSnapshot queue)
    {
        string status = GetStatus(arbiter, clientId, queue);
        bool youHoldIt = status == Granted;
        bool someoneHolds = arbiter.Status == ControlStatus.Granted;

        var report = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["youHoldIt"] = youHoldIt,
            ["agent"] = arbiter.AgentName,
            ["topic"] = arbiter.Topic,
            ["hint"] = GetHint(status, arbiter.AgentName, queue)
        };

        // Only when somebody actually holds it: a clock on an editor nobody has is
        // a number about nothing.
        if (someoneHolds)
            report["heldSeconds"] = (int)Math.Floor(Math.Max(0.0, heldSeconds));

        // Reported to everyone: how long the line is decides whether a newcomer wants
        // to join it at all, and where a caller already in it stands decides whether
        // it keeps waiting.
        if (queue.Count > 0)
        {
            report["waitingAgents"] = queue.Count;
            report["nextInLine"] = queue.NextAgent;
        }

        if (queue.Position > 0)
            report["queuePosition"] = queue.Position;

        return report;
    }

    private static string GetStatus(
        ControlArbiter arbiter,
        string clientId,
        ControlQueueSnapshot queue)
    {
        if (arbiter.IsHeldBy(clientId))
            return Granted;

        if (arbiter.IsClaimedByAnother(clientId))
            return queue.Position > 0 ? Waiting : HeldByAnotherAgent;

        // Pending and mine — the request is in the countdown.
        if (arbiter.HasAgent)
            return Pending;

        // A verdict belongs to the agent it was aimed at. To anybody else the editor
        // is simply the user's again, which is what it is.
        if (arbiter.Status is ControlStatus.Denied or ControlStatus.TimedOut
            && arbiter.IsVerdictFor(clientId))
        {
            return arbiter.Status == ControlStatus.Denied ? Denied : TimedOut;
        }

        return Available;
    }

    private static string GetHint(string status, string agentName, ControlQueueSnapshot queue) =>
        status switch
        {
            Granted =>
                "You hold the editor. Hand it back with 'release-control' as soon as you are done.",

            Pending =>
                "The user is being asked. Poll 'control-status', or call 'take-control' with "
                + "'wait' to be told the moment it is decided.",

            Waiting =>
                $"{Name(agentName)} holds the editor and you are {Place(queue.Position)} in line. "
                + "Your place stays as long as you keep asking: call 'take-control' with 'wait' "
                + "again, or give it up with 'release-control'.",

            HeldByAnotherAgent when queue.Count > 0 =>
                $"{Name(agentName)} holds the editor and {Waiters(queue.Count)} waiting for it. "
                + "Call 'take-control' with 'wait=<seconds>' to take your place behind them.",

            HeldByAnotherAgent =>
                $"{Name(agentName)} holds the editor. Call 'take-control' with 'wait=<seconds>' "
                + "to take your turn next and be told when it is yours.",

            Denied =>
                "The user refused. That is their decision — do not ask again unless they say so.",

            TimedOut =>
                "You lost control by falling silent. Ask again with 'take-control' if you still "
                + "need the editor.",

            _ => "The editor is the user's. Ask for it with 'take-control'."
        };

    private static string Name(string agentName) =>
        agentName.Length > 0 ? $"'{agentName}'" : "Another agent";

    private static string Place(int position) => position switch
    {
        1 => "next",
        2 => "second",
        3 => "third",
        _ => $"{position}th"
    };

    private static string Waiters(int count) =>
        count == 1 ? "one agent is" : $"{count} agents are";
}
