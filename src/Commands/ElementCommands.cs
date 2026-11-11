using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;

namespace VoxelCAD.Commands;

/// <summary>The hierarchy: elements created, removed, renamed, reparented.</summary>
internal static class ElementCommands
{
    public static void AddTo(CommandCatalogBuilder builder)
    {
        builder.Add("create-element", "Adds an element to the scene.",
            (context, arguments) =>
            {
                Guid parentId = arguments.Has("parent")
                    ? context.ResolveElement(arguments.String("parent"))
                    : context.Scene.Id;

                var kind = arguments.Enum("kind", ComponentKind.None);

                // A reference with no file is nothing: it would stand in the scene
                // as a placeholder until somebody found the second command. The
                // import is one thing the user did and arrives already pointing at
                // something, which is why it is the only way in.
                if (ReferenceFormats.IsReference(kind))
                {
                    throw new CommandException(
                        $"A {kind} stands for a file, so it is made by naming one: "
                        + "'import-reference path=...'.");
                }

                Guid id = context.Scene.CreateElement(parentId, kind, arguments.String("name"));

                return CommandResult.Success(new Dictionary<string, object?> { ["id"] = id.ToString() });
            },
            Required("name", "Name of the new element."),
            Optional("kind",
                "None, VoxelModel or Camera. Defaults to None, a grouping element. A camera "
                + "holds no cells: it is a shot kept in the document, aimed with 'aim-camera' "
                + "and set with 'set-camera'. References are made by 'import-reference', "
                + "which names the file they stand for."),
            Optional("parent", "Parent element, by id or name. Defaults to the scene root."));

        builder.Add("delete-element", "Removes an element and everything under it.",
            (context, arguments) =>
            {
                context.Scene.DeleteElement(context.ResolveElement(arguments.String("element")));

                return CommandResult.Success();
            },
            Required("element", "Element to remove, by id or name."));

        builder.Add("duplicate-element",
            "Copies an element and everything under it, deeply: fresh ids, its own "
            + "cells, and no link of any kind to the original. Lands beside it by "
            + "default, or under a parent you name — in this scene or another one. "
            + "This is the whole of copy and paste in one step: the editor's "
            + "clipboard is session state, like the selection, and has no command "
            + "form. Materials are shared rather than copied, because voxels name "
            + "one by id and a registry is what that is for.",
            (context, arguments) =>
            {
                IScene source = context.Scene;
                Guid elementId = context.ResolveElement(arguments.String("element"));

                IScene target = arguments.Has("scene")
                    ? context.GetScene(context.ResolveScene(arguments.String("scene")))
                    : source;

                Guid id = target.PasteElement(
                    ResolveDestination(context, arguments, source, target, elementId),
                    source.CaptureElement(elementId),
                    arguments.Has("name") ? arguments.String("name") : null);

                target.TryGetElement(id, out IHierarchyElement copy);

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["id"] = id.ToString(),
                    ["name"] = copy.Name,
                    ["scene"] = target.Name
                });
            },
            Required("element", "Element to copy, by id or name."),
            Optional("parent", "Where the copy goes, by id or name — resolved in the "
                + "target scene. Defaults to the original's own parent, or the target "
                + "scene's root when 'scene' names another one."),
            Optional("scene", "Scene to copy into, by id or name. Defaults to the active one."),
            Optional("name", "Name for the copy. Defaults to the original's where nothing "
                + "is using it, else the next free number from its own."));

        builder.Add("rename-element", "Renames an element.",
            (context, arguments) =>
            {
                context.Scene.RenameElement(
                    context.ResolveElement(arguments.String("element")),
                    arguments.String("name"));

                return CommandResult.Success();
            },
            Required("element", "Element to rename, by id or name."),
            Required("name", "New name."));

        builder.Add("set-visible",
            "Shows or hides an element. A hidden element keeps everything it has — "
            + "cells, transform, place in the tree — and is simply not drawn and not "
            + "exported; what is nested under it is hidden with it. Reported by "
            + "'element' and 'tree' as 'visible'.",
            (context, arguments) =>
            {
                context.Scene.SetVisible(
                    context.ResolveElement(arguments.String("element")),
                    arguments.Bool("visible", fallback: true));

                return CommandResult.Success();
            },
            Required("element", "Element to show or hide, by id or name."),
            Optional("visible", "true or false. Defaults to true."));

        builder.Add("reparent-element", "Moves an element under another parent, keeping its world position.",
            (context, arguments) =>
            {
                Guid parentId = arguments.Has("parent")
                    ? context.ResolveElement(arguments.String("parent"))
                    : context.Scene.Id;

                context.Scene.ReparentElement(
                    context.ResolveElement(arguments.String("element")),
                    parentId,
                    arguments.Has("index") ? arguments.Int("index") : 0);

                return CommandResult.Success();
            },
            Required("element", "Element to move, by id or name."),
            Optional("parent", "New parent, by id or name. Defaults to the scene root."),
            Optional("index", "Position among siblings. Defaults to 0."));
    }

    /// <summary>
    /// Where a copy lands when nobody said. Beside the original is what "duplicate"
    /// means, and it is only available while the copy stays in the scene the
    /// original lives in — a parent from one scene names nothing in another.
    /// </summary>
    private static Guid ResolveDestination(
        CommandContext context,
        CommandArguments arguments,
        IScene source,
        IScene target,
        Guid elementId)
    {
        if (arguments.Has("parent"))
            return context.ResolveElement(target, arguments.String("parent"));

        if (target.Id != source.Id)
            return target.Id;

        return source.TryGetElement(elementId, out IHierarchyElement element)
            ? element.Parent.Id
            : source.Id;
    }
}
