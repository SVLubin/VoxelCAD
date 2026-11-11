using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static VoxelCAD.Commands.CommandCatalogBuilder;
using static VoxelCAD.Commands.CommandCatalogHelper;

namespace VoxelCAD.Commands;

/// <summary>The scene list: created, removed, renamed, activated, and read back.</summary>
internal static class SceneCommands
{
    public static void AddTo(CommandCatalogBuilder builder)
    {
        builder.Add("create-scene", "Adds a scene to the project and makes it the active one.",
            (context, arguments) =>
            {
                Vector3I size = arguments.Has("size")
                    ? arguments.VectorI("size")
                    : context.Scene.Size;

                Guid id = context.Project.CreateScene(arguments.String("name"), size);

                // The same thing the editor does when the user adds one: a scene
                // nobody is looking at is not much use to whoever just made it.
                context.SetActiveScene(id);

                return CommandResult.Success(new Dictionary<string, object?> { ["id"] = id.ToString() });
            },
            Required("name", "Name of the new scene."),
            Optional("size", "Buildable region in cells, 'x,y,z'. Defaults to the active scene's."));

        builder.Add("delete-scene", "Removes a scene and everything in it. Refused for the last one.",
            (context, arguments) =>
            {
                context.Project.DeleteScene(context.ResolveScene(arguments.String("scene")));

                return CommandResult.Success();
            },
            Required("scene", "Scene to remove, by id or name."));

        builder.Add("duplicate-scene",
            "Copies a scene and everything in it, deeply — fresh ids throughout, and no "
            + "link to the original — and makes the copy the active one, the same way "
            + "'create-scene' does. Go back with 'activate-scene'.",
            (context, arguments) =>
            {
                Guid id = context.Project.PasteScene(
                    context.Project.CaptureScene(context.ResolveScene(arguments.String("scene"))),
                    arguments.Has("name") ? arguments.String("name") : null);

                // Said rather than left to happen. In the editor the history takes
                // the user to the scene a record belongs to, so the copy becomes the
                // active one whether or not this line is here; without it the console
                // and the editor would quietly disagree about where the caller is.
                context.SetActiveScene(id);

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["id"] = id.ToString(),
                    ["name"] = context.GetScene(id).Name
                });
            },
            Required("scene", "Scene to copy, by id or name."),
            Optional("name", "Name for the copy. Defaults to the original's where nothing "
                + "is using it, else the next free number from its own."));

        builder.Add("rename-scene", "Renames a scene.",
            (context, arguments) =>
            {
                context.Project.RenameScene(
                    context.ResolveScene(arguments.String("scene")),
                    arguments.String("name"));

                return CommandResult.Success();
            },
            Required("scene", "Scene to rename, by id or name."),
            Required("name", "New name."));

        builder.Add("activate-scene", "Makes a scene the one commands and the editor work on.",
            (context, arguments) =>
            {
                context.SetActiveScene(context.ResolveScene(arguments.String("scene")));

                return CommandResult.Success(Describe(context));
            },
            Required("scene", "Scene to work on, by id or name."));

        builder.AddQuery("scenes", "Lists the project's scenes.",
            (context, _) => CommandResult.Success(context.Project.Scenes
                .Select(scene => (object?)new Dictionary<string, object?>
                {
                    ["id"] = scene.Id.ToString(),
                    ["name"] = scene.Name,
                    ["size"] = new[] { scene.Size.X, scene.Size.Y, scene.Size.Z },
                    ["active"] = scene.Id == context.Scene.Id,
                    ["elements"] = scene.Children.Count
                })
                .ToList()));
    }
}
