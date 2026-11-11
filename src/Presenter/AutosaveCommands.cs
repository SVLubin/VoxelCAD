using System;
using System.Collections.Generic;
using VoxelCAD.Commands;

namespace VoxelCAD.Presenter;

/// <summary>
/// The agent surface of the recovery snapshots. Editor-only, like the camera and
/// control commands: the headless CLI has no clock and no editor. Reading is free;
/// writing a snapshot, recovering one and discarding one are gated on holding the
/// editor like every other edit.
/// </summary>
/// <remarks>
/// <c>autosave-discard</c> is held to the ledger's own bar for preference writes
/// — an irreversible act on files that outlive the session with nothing to put
/// them back — and so reaches only what this session's own commands orphaned. A
/// user's unsaved work from an earlier session is the one thing this feature
/// promises never to delete unasked.
/// </remarks>
internal static class AutosaveCommands
{
    public static IReadOnlyList<CommandDefinition> Create(AutosavePresenter autosave)
    {
        return new List<CommandDefinition>
        {
            Query("autosave-status",
                "Reports the recovery snapshot of the open document and every snapshot earlier "
                + "sessions left behind. A snapshot is never the document's own file; 'save' is.",
                (_, _) => CommandResult.Success(DescribeStatus(autosave))),

            Mutating("autosave-now",
                "Writes a recovery snapshot of the open document now rather than at the next "
                + "interval. Not a save: use 'save' to write the project where it lives. The "
                + "switch and the interval are preferences; read them with 'preferences'.",
                (_, _) =>
                {
                    if (!autosave.TryWriteNow(out string reason))
                    {
                        return CommandResult.Success(new Dictionary<string, object?>
                        {
                            ["written"] = false,
                            ["reason"] = reason
                        });
                    }

                    AutosaveStatus status = autosave.Describe();

                    return CommandResult.Success(new Dictionary<string, object?>
                    {
                        ["written"] = true,
                        ["id"] = status.SnapshotId?.ToString("N"),
                        ["path"] = status.SnapshotId is { } id ? autosave.SnapshotPath(id) : null,
                        ["milliseconds"] = status.LastWriteMilliseconds is { } milliseconds
                            ? Math.Round(milliseconds, 1)
                            : null
                    });
                }),

            Mutating("autosave-recover",
                "Opens a recovery snapshot as the document it came from: unsaved, under the "
                + "original path unless the file on disk has been written since the snapshot "
                + "was taken (its modification stamp differs from the one recorded), in which "
                + "case it opens untitled. The document it replaces keeps a snapshot of its own "
                + "unsaved work. Read the result with 'tree'.",
                (context, arguments) =>
                {
                    Guid id = ParseId(arguments.String("id"));

                    if (!autosave.TryRecover(id, out string error, out string note))
                        throw new CommandException(error);

                    return CommandResult.Success(new Dictionary<string, object?>
                    {
                        ["recovered"] = id.ToString("N"),
                        ["path"] = context.Path,
                        ["title"] = context.Path is { } path
                            ? System.IO.Path.GetFileName(path)
                            : WindowTitleHelper.UntitledName,
                        ["openedModified"] = true,
                        ["note"] = note
                    });
                },
                Required("id", "The snapshot's id, from 'autosave-status'.")),

            Mutating("autosave-discard",
                "Deletes a snapshot this session's own 'new', 'open' or 'autosave-recover' left "
                + "behind. Snapshots from earlier sessions belong to the user and are refused: "
                + "the launch card and File → Recover unsaved work… are their route.",
                (_, arguments) =>
                {
                    bool session = arguments.Bool("session", false);
                    bool byId = arguments.Has("id");

                    if (session == byId)
                        throw new CommandException("Pass exactly one of 'id' or 'session=true'.");

                    var deletedList = new List<string>();

                    if (session)
                    {
                        foreach (Guid id in autosave.DiscardSessionOrphans())
                            deletedList.Add(id.ToString("N"));
                    }
                    else
                    {
                        Guid id = ParseId(arguments.String("id"));

                        if (!autosave.TryDiscard(id, out string error))
                            throw new CommandException(error);

                        deletedList.Add(id.ToString("N"));
                    }

                    return CommandResult.Success(new Dictionary<string, object?>
                    {
                        ["deleted"] = deletedList
                    });
                },
                Optional("id", "The snapshot to delete, from 'autosave-status'."),
                Optional("session", "true: every snapshot this session's commands left behind."))
        };
    }

    private static Dictionary<string, object?> DescribeStatus(AutosavePresenter autosave)
    {
        AutosaveStatus status = autosave.Describe();
        var snapshotsList = new List<Dictionary<string, object?>>();

        foreach (AutosaveEntry entry in autosave.Scan())
        {
            snapshotsList.Add(new Dictionary<string, object?>
            {
                ["id"] = entry.Id.ToString("N"),
                ["title"] = entry.Title,
                ["originalPath"] = entry.OriginalPath,
                ["writtenAt"] = FormatUtc(entry.WrittenAt),
                ["records"] = entry.Records,
                ["original"] = Camel(entry.Original.ToString()),
                ["state"] = Camel(entry.State.ToString()),
                ["sessionOrphan"] = autosave.IsSessionOrphan(entry.Id),
                ["problem"] = entry.Problem.Length == 0 ? null : entry.Problem
            });
        }

        return new Dictionary<string, object?>
        {
            ["enabled"] = status.Enabled,
            ["intervalSeconds"] = status.IntervalSeconds,
            ["directory"] = status.Directory,
            ["document"] = new Dictionary<string, object?>
            {
                ["title"] = status.Title,
                ["originalPath"] = status.OriginalPath,
                ["snapshotId"] = status.SnapshotId?.ToString("N"),
                ["hasSnapshot"] = status.HasSnapshot,
                ["pendingChanges"] = status.PendingChanges,
                ["deferred"] = status.Deferred,
                ["writeInFlight"] = status.WriteInFlight,
                ["secondsUntilNextWrite"] = Math.Round(status.SecondsUntilNextWrite, 1),
                ["lastWrittenAt"] = status.LastWrittenAt is { } written ? FormatUtc(written) : null,
                ["lastWriteMilliseconds"] = status.LastWriteMilliseconds is { } milliseconds
                    ? Math.Round(milliseconds, 1)
                    : null,
                ["lastFailure"] = status.LastFailure
            },
            ["snapshots"] = snapshotsList,
            ["status"] = autosave.DescribeStatusLine(),
            ["note"] = "A snapshot is never the document's own file; 'save' is. Snapshots name "
                + "this machine's absolute paths and are local to it. 'autosave-now' writes one "
                + "at once; 'autosave-recover' opens one; 'autosave-discard' removes one this "
                + "session left."
        };
    }

    private static Guid ParseId(string text) =>
        Guid.TryParse(text, out Guid id)
            ? id
            : throw new CommandException($"'{text}' is not a snapshot id.");

    private static string FormatUtc(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o");

    private static string Camel(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static CommandParameter Optional(string name, string summary) =>
        new(name, summary, false);

    private static CommandParameter Required(string name, string summary) =>
        new(name, summary, true);

    private static CommandDefinition Mutating(
        string name,
        string summary,
        Func<CommandContext, CommandArguments, CommandResult> handler,
        params CommandParameter[] parameters) =>
        new(new CommandDescriptor(name, summary, parameters), handler);

    private static CommandDefinition Query(
        string name,
        string summary,
        Func<CommandContext, CommandArguments, CommandResult> handler,
        params CommandParameter[] parameters) =>
        new(new CommandDescriptor(name, summary, parameters, mutates: false), handler);
}
