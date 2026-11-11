using System;
using System.Collections.Generic;
using System.Linq;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;
using static VoxelCAD.Commands.CommandCatalogHelper;

namespace VoxelCAD.Commands;

/// <summary>
/// The prefab library: made out of an element, instanced into scenes, edited in a
/// room of its own, unpacked and removed.
/// </summary>
/// <remarks>
/// An instance's contents are derived and refuse every edit. What edits them is
/// 'edit-prefab', which makes the library entry the active context — after that
/// every ordinary element command works on it, because a prefab's contents are a
/// scene like any other.
/// </remarks>
internal static class PrefabCommands
{
    public static void AddTo(CommandCatalogBuilder builder)
    {
        builder.Add("create-prefab",
            "Lifts an element into the prefab library and leaves an instance of it in its "
            + "place. The instance keeps the element's transform, so nothing appears to move.",
            (context, arguments) =>
            {
                Guid id = context.Scene.CreatePrefab(
                    context.ResolveElement(arguments.String("element")),
                    arguments.Has("name") ? arguments.String("name") : null);

                context.Project.TryGetPrefab(id, out IPrefab prefab);

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["id"] = id.ToString(),
                    ["name"] = prefab.Name
                });
            },
            Required("element", "Element to lift, by id or name."),
            Optional("name", "Name for the library entry. Defaults to the element's own, "
                + "numbered up if another prefab already has it."));

        builder.Add("instantiate-prefab",
            "Adds an instance of a prefab to the active scene. Its contents are derived "
            + "from the source and cannot be edited; edit the source with 'edit-prefab'.",
            (context, arguments) =>
            {
                Guid prefabId = context.ResolvePrefab(arguments.String("prefab"));
                Guid parentId = arguments.Has("parent")
                    ? context.ResolveElement(arguments.String("parent"))
                    : context.Scene.Id;

                Guid id = context.Scene.CreateInstance(
                    parentId, prefabId, arguments.Has("name") ? arguments.String("name") : null);

                context.Scene.TryGetElement(id, out IHierarchyElement element);

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["id"] = id.ToString(),
                    ["name"] = element.Name
                });
            },
            Required("prefab", "Prefab to place, by id or name."),
            Optional("parent", "Element to place it under, by id or name. Defaults to the "
                + "scene's top level."),
            Optional("name", "Name for the instance. Defaults to the prefab's own, numbered "
                + "up if the scene already has it."));

        builder.Add("unpack-prefab",
            "Cuts an instance's link to its source: the element stays as a group and its "
            + "contents become ordinary elements. One level — a nested instance stays one.",
            (context, arguments) =>
            {
                context.Scene.UnpackInstance(
                    context.ResolveElement(arguments.String("element")));

                return CommandResult.Success();
            },
            Required("element", "Instance to unpack, by id or name."));

        builder.Add("edit-prefab",
            "Opens a prefab for editing: its contents become the active context, so every "
            + "element command works on them. Go back with 'activate-scene'.",
            (context, arguments) =>
            {
                context.SetActiveScene(context.ResolvePrefab(arguments.String("prefab")));

                return CommandResult.Success(Describe(context));
            },
            Required("prefab", "Prefab to edit, by id or name."));

        builder.Add("rename-prefab",
            "Renames a library entry. Instances keep their own names; a name another "
            + "prefab already has is refused.",
            (context, arguments) =>
            {
                context.Project.RenamePrefab(
                    context.ResolvePrefab(arguments.String("prefab")),
                    arguments.String("name"));

                return CommandResult.Success();
            },
            Required("prefab", "Prefab to rename, by id or name."),
            Required("name", "New name."));

        builder.Add("delete-prefab",
            "Removes a prefab and unpacks every instance of it, in every scene and inside "
            + "every other prefab. Nothing is lost: an unpacked instance keeps its geometry.",
            (context, arguments) =>
            {
                Guid id = context.ResolvePrefab(arguments.String("prefab"));
                var unpacked = CountInstances(context, id);

                context.Project.DeletePrefab(id);

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["unpacked"] = unpacked
                });
            },
            Required("prefab", "Prefab to remove, by id or name."));

        builder.AddQuery("prefabs", "Lists the project's prefab library.",
            (context, _) => CommandResult.Success(context.Project.Prefabs
                .Select(prefab => (object?)new Dictionary<string, object?>
                {
                    ["id"] = prefab.Id.ToString(),
                    ["name"] = prefab.Name,
                    ["size"] = new[]
                    {
                        prefab.Content.Size.X, prefab.Content.Size.Y, prefab.Content.Size.Z
                    },
                    // True while its contents are the active context — the state
                    // 'edit-prefab' puts the editor into.
                    ["editing"] = prefab.Id == context.Scene.Id,
                    // What 'delete-prefab' would unpack, said before it is asked.
                    ["inUse"] = context.Project.IsPrefabInUse(prefab.Id),
                    ["elements"] = prefab.Content.Children.Count
                })
                .ToList()));
    }

    /// <summary>
    /// How many instances a deletion is about to unpack. Counted before rather than
    /// reported after, because afterwards there is nothing left to count.
    /// </summary>
    private static int CountInstances(CommandContext context, Guid prefabId)
    {
        var count = 0;

        foreach (IScene scene in context.Project.Scenes)
            count += CountInstances(scene.Children, prefabId);

        foreach (IPrefab prefab in context.Project.Prefabs)
            count += CountInstances(prefab.Content.Children, prefabId);

        return count;
    }

    private static int CountInstances(IReadOnlyList<IHierarchyElement> children, Guid prefabId)
    {
        var count = 0;

        foreach (IHierarchyElement child in children)
        {
            if (child.Component is IPrefabInstance instance
                && instance.SourceId == prefabId
                && child.PrefabInstanceId == null)
            {
                count++;
            }

            count += CountInstances(child.Children, prefabId);
        }

        return count;
    }
}
