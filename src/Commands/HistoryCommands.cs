using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;

namespace VoxelCAD.Commands;

/// <summary>The one project-wide history: walked, replayed, and read back.</summary>
internal static class HistoryCommands
{
    public static void AddTo(CommandCatalogBuilder builder)
    {
        builder.Add("undo", "Reverses the last action.",
            (context, _) =>
            {
                if (!context.Project.History.CanUndo)
                    return CommandResult.Failure("Nothing to undo.");

                context.Project.History.Undo();

                return CommandResult.Success();
            });

        builder.Add("redo", "Replays the last undone action.",
            (context, _) =>
            {
                if (!context.Project.History.CanRedo)
                    return CommandResult.Failure("Nothing to redo.");

                context.Project.History.Redo();

                return CommandResult.Success();
            });

        builder.Add("undo-to", "Walks the history to a given point.",
            (context, arguments) =>
            {
                context.Project.History.UndoTo(arguments.Int("index"));

                return CommandResult.Success();
            },
            Required("index", "Index of the last record to keep applied; -1 undoes everything."));

        builder.AddQuery("history", "Lists the undo history.",
            (context, _) =>
            {
                IHistory history = context.Project.History;

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["currentIndex"] = history.CurrentIndex,
                    ["records"] = history.Records
                        .Select((record, index) => (object?)new Dictionary<string, object?>
                        {
                            ["index"] = index,
                            ["kind"] = record.Kind.ToString(),
                            ["target"] = record.TargetName,
                            // Empty for a project-level action, which belongs to
                            // no scene — a material edit, say.
                            ["scene"] = record.SceneId == Guid.Empty
                                ? null
                                : record.SceneId.ToString(),
                            ["by"] = record.Actor.Kind.ToString(),
                            ["byName"] = record.Actor.Name,
                            ["applied"] = index <= history.CurrentIndex
                        })
                        .ToList()
                });
            });
    }
}
