using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;

namespace VoxelCAD.Commands;

/// <summary>The material registry: created, recoloured, renamed, removed.</summary>
internal static class MaterialCommands
{
    public static void AddTo(CommandCatalogBuilder builder)
    {
        builder.Add("create-material", "Adds a material to the project.",
            (context, arguments) =>
            {
                Guid id = context.Project.CreateMaterial(
                    arguments.String("name"),
                    arguments.Color("color"));

                return CommandResult.Success(new Dictionary<string, object?> { ["id"] = id.ToString() });
            },
            Required("name", "Name of the new material."),
            Required("color", "'r,g,b' or 'r,g,b,a', each from zero to one. Alpha is opacity."));

        builder.Add("duplicate-material",
            "Copies a material — its colour and its appearance — as a material in its "
            + "own right. Nothing is repainted: voxels name a material by id, and the "
            + "copy's id is not the one they name, which is what makes it a good way "
            + "to start a variant of an existing colour.",
            (context, arguments) =>
            {
                Guid id = context.Project.PasteMaterial(
                    context.Project.CaptureMaterial(
                        context.ResolveMaterial(arguments.String("material"))),
                    arguments.Has("name") ? arguments.String("name") : null);

                context.Project.TryGetMaterial(id, out IMaterial copy);

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["id"] = id.ToString(),
                    ["name"] = copy.Name
                });
            },
            Required("material", "Material to copy, by id or name."),
            Optional("name", "Name for the copy. Defaults to the original's where nothing "
                + "is using it, else the next free number from its own."));

        builder.Add("set-material-color", "Recolours a material, and every voxel using it.",
            (context, arguments) =>
            {
                context.Project.SetMaterialColor(
                    context.ResolveMaterial(arguments.String("material")),
                    arguments.Color("color"));

                return CommandResult.Success();
            },
            Required("material", "Material by id or name."),
            Required("color", "'r,g,b' or 'r,g,b,a'."));

        builder.Add("set-material-preset",
            "Applies a ready-made material: how it takes light, what facture it wears, "
            + "and its colour, in one undoable act. 'material-presets' lists them. The "
            + "colour is part of the bargain — a preset called Brick that left a material "
            + "grey would be a preset of nothing — so recolour afterwards if you want it "
            + "in another shade; which preset a material matches is asked of its "
            + "appearance alone, so a recoloured brick is still Brick.",
            (context, arguments) =>
            {
                context.Project.SetMaterialPreset(
                    context.ResolveMaterial(arguments.String("material")),
                    arguments.Enum<MaterialPreset>("preset"));

                return CommandResult.Success();
            },
            Required("material", "Material by id or name."),
            Required("preset", "Preset name, as listed by 'material-presets'."));

        builder.Add("set-material-appearance",
            "Tunes how a material takes light and what facture it wears. Every argument "
            + "is optional and what is omitted is left alone, so one call can nudge a "
            + "single value. Out-of-range numbers are clamped rather than refused. "
            + "'transparent' says whether the material is see-through at all; how much "
            + "is then the colour's alpha, through 'set-material-color'.",
            (context, arguments) =>
            {
                Guid id = context.ResolveMaterial(arguments.String("material"));

                context.Project.TryGetMaterial(id, out IMaterial material);
                MaterialAppearance current = material.Appearance;

                context.Project.SetMaterialAppearance(id, new MaterialAppearance(
                    Roughness: arguments.Float("roughness", current.Roughness),
                    Metallic: arguments.Float("metallic", current.Metallic),
                    Emission: arguments.Float("emission", current.Emission),
                    Finish: arguments.Enum("finish", current.Finish),
                    FinishScale: arguments.Float("scale", current.FinishScale),
                    FinishRelief: arguments.Float("relief", current.FinishRelief),
                    Transparent: arguments.Bool("transparent", current.Transparent)));

                return CommandResult.Success();
            },
            Required("material", "Material by id or name."),
            Optional("roughness", "0 a mirror, 1 chalk."),
            Optional("metallic", "0 a dielectric, 1 a metal. Full metal has no diffuse term, "
                + "so a face turned from every light goes black; the metal presets stop at 0.85."),
            Optional("emission", "Self-lit strength, 0 to 1. Zero is not self-lit at all."),
            Optional("finish", "The facture: 'smooth' for none. 'material-presets' names the rest."),
            Optional("scale", "How many voxels one UNIT of the facture spans — a brick "
                + "course, a floor board, a panel, a mosaic tile — 0.1 to 16. Comparable "
                + "across finishes: at the same number a board and a course and a panel "
                + "are the same size, so the ready-made materials differ by their own "
                + "numbers rather than by which pattern they happen to use."),
            Optional("relief", "How much of the facture's relief to apply, 0 to 2."),
            Optional("transparent", "Whether the material is see-through at all. Off, it is "
                + "opaque whatever its colour's alpha says; on, that alpha decides how much "
                + "shows through. Declared rather than guessed from the alpha, so a stray "
                + "0.99 cannot quietly put a material into the sorted pass and change which "
                + "faces of a model exist."));

        builder.Add("rename-material", "Renames a material.",
            (context, arguments) =>
            {
                context.Project.RenameMaterial(
                    context.ResolveMaterial(arguments.String("material")),
                    arguments.String("name"));

                return CommandResult.Success();
            },
            Required("material", "Material by id or name."),
            Required("name", "New name."));

        builder.Add("replace-material",
            "Gives every cell wearing one material another, as one undoable action "
            + "however many elements it reaches. 'scope' is the active scene by default; "
            + "'element' narrows it to one voxel model, 'project' widens it to every scene "
            + "and every prefab's contents — which is what makes the old material deletable, "
            + "since 'delete-material' refuses while any cell still wears it. Either "
            + "material may be a shipped one. Nothing wearing it records nothing.",
            (context, arguments) =>
            {
                Guid fromId = context.ResolveMaterial(arguments.String("from"));
                Guid toId = context.ResolveMaterial(arguments.String("to"));
                var scope = arguments.String("scope", "scene").ToLowerInvariant();

                // 'element' narrows a replace only under its own scope. Taken
                // silently anywhere else, a replace meant for one model would run
                // over the whole scene with nothing saying the argument was dropped.
                if (scope is "scene" or "project" && arguments.Has("element"))
                {
                    throw new CommandException(
                        $"Argument 'element' narrows a replace only under scope=element, and "
                        + $"scope is '{scope}'. Add scope=element to replace in that one voxel "
                        + $"model, or drop 'element' to replace across the {scope}.");
                }

                switch (scope)
                {
                    case "element":
                        if (!arguments.Has("element"))
                        {
                            throw new CommandException(
                                "scope=element needs 'element' — the voxel model to replace in.");
                        }

                        context.Scene.ReplaceMaterial(
                            fromId, toId, context.ResolveElement(arguments.String("element")));
                        break;

                    case "scene":
                        context.Scene.ReplaceMaterial(fromId, toId, null);
                        break;

                    case "project":
                        context.Project.ReplaceMaterial(fromId, toId);
                        break;

                    default:
                        throw new CommandException(
                            $"Argument 'scope' is element, scene or project — got '{scope}'.");
                }

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["stillInUse"] = context.Project.IsMaterialInUse(fromId)
                });
            },
            Required("from", "Material to replace, by id or name."),
            Required("to", "Material to give those cells, by id or name."),
            Optional("scope", "element, scene or project. Defaults to scene — the one being worked on."),
            Optional("element",
                "With scope=element: the voxel model to replace in, by id or name. Refused "
                + "under any other scope."));

        builder.Add("delete-material", "Removes a material. Refused while any voxel still uses it.",
            (context, arguments) =>
            {
                context.Project.DeleteMaterial(context.ResolveMaterial(arguments.String("material")));

                return CommandResult.Success();
            },
            Required("material", "Material by id or name."));
    }
}
