using System;
using System.Collections.Generic;
using VoxelCAD.Commands;
using VoxelCAD.View;

namespace VoxelCAD.Presenter;

/// <summary>
/// The commands an agent uses to negotiate control. They reach the session, not
/// the document, which is why they live here and are added to the base catalog
/// rather than baked into it — a headless run has no user to take control from.
/// </summary>
/// <remarks>
/// Built per transport client, because who is asking is the whole question these
/// four answer: the client id is closed over rather than passed as an argument, so
/// no agent can name another one. Everything they decide is the control presenter's
/// — these are a surface over it, and deliberately hold no policy of their own.
/// </remarks>
internal static class ControlCommands
{
    /// <summary>
    /// How long a topic may be before the plaque trims it. A recommendation rather
    /// than a limit — nothing refuses a long topic, it is simply not all read — and
    /// quoted from the plaque's own numbers so the advice cannot drift from the
    /// layout that gives rise to it.
    /// </summary>
    private static readonly string TopicLengthAdvice =
        $"Aim for about {PlaqueBudget.TopicLineCharacters} characters and no more than "
        + $"{PlaqueBudget.TopicCharacters}: the plaque gives it two lines at that width and "
        + "trims what is past them with an ellipsis.";

    /// <param name="clientId">
    /// The transport client these commands speak for. See
    /// <see cref="ControlGatedDispatcher"/> for where it comes from.
    /// </param>
    public static IReadOnlyList<CommandDefinition> Create(ControlPresenter control, string clientId)
    {
        return new List<CommandDefinition>
        {
            Query("take-control",
                "Asks the user for control of the editor, optionally waiting for your turn.",
                (_, arguments) => control.RequestControl(
                    clientId,
                    arguments.String("name", string.Empty),
                    arguments.String("topic", string.Empty),
                    arguments.Has("wait") ? arguments.Float("wait") : 0f),
                new CommandParameter(
                    "name",
                    "How to name the agent in the plaque and history. Without it the user "
                    + "sees a generic \"An agent\", so introduce yourself.",
                    false),
                new CommandParameter(
                    "topic",
                    "What you are here to do, in one short line — shown to the user under "
                    + "your name while you ask for and hold the editor. Update it with "
                    + "'report-topic' as the work moves on. " + TopicLengthAdvice,
                    false),
                new CommandParameter(
                    "wait",
                    "Seconds to stay blocked waiting for the answer instead of returning at "
                    + "once. Only one agent may work at a time, so with another agent holding "
                    + "the editor this is what puts you in line for it: agents are served in "
                    + "the order they asked, and the call comes back the moment the editor is "
                    + "yours, or when the user refuses. Capped at 120s per call — your place "
                    + "outlives the call, so keep asking to keep waiting, for an hour if that "
                    + "is what it takes, and 'control-status' reports where you stand. Give "
                    + "the place up with 'release-control'. Without 'wait', an editor another "
                    + "agent holds is simply a refusal.",
                    false)),

            Query("report-topic", "Tells the user what you are working on right now.",
                (_, arguments) => control.ReportTopic(clientId, arguments.String("topic")),
                new CommandParameter(
                    "topic",
                    "One short line for the plaque. The user reads it to understand what "
                    + "is happening without having to follow every command. " + TopicLengthAdvice,
                    true)),

            Query("release-control",
                "Hands control back to the user — or gives up your place in line, if you were "
                + "waiting for a turn rather than holding the editor.",
                (_, arguments) => control.ReleaseControl(clientId, arguments.Bool("keep-camera", false)),
                new CommandParameter(
                    "keep-camera",
                    "Keep your camera view instead of putting the user's back. Defaults to false, "
                    + "which restores it. Set it to leave the user looking at what you built.",
                    false)),

            Query("control-status",
                "Reports who holds the editor — you, the user, or another agent.",
                (_, _) => control.GetStatus(clientId))
        };
    }

    private static CommandDefinition Query(
        string name,
        string summary,
        Func<CommandContext, CommandArguments, CommandResult> handler,
        params CommandParameter[] parameters)
    {
        return new CommandDefinition(
            new CommandDescriptor(name, summary, parameters, mutates: false),
            handler);
    }
}
