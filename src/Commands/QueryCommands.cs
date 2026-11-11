using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.Geometry;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;
using static VoxelCAD.Commands.CommandCatalogHelper;

namespace VoxelCAD.Commands;

/// <summary>
/// The document read back: the tree, one element, a model's bounds, one layer as
/// a character grid, and the material list. Reading is half the surface — an
/// agent that acts like a user also has to see like one.
/// </summary>
internal static class QueryCommands
{
    /// <summary>
    /// How many shipped materials one answer describes. Three hundred of them
    /// with their appearances is 64 KB, which is not a thing to read; fifty is a
    /// list you can look down.
    /// </summary>
    private const int StandardMaterialPage = 50;

    public static void AddTo(CommandCatalogBuilder builder)
    {
        builder.AddQuery("tree", "Describes the scene hierarchy.",
            (context, _) => CommandResult.Success(Describe(context)));

        builder.AddQuery("element", "Describes one element in full.",
            (context, arguments) =>
            {
                Guid id = context.ResolveElement(arguments.String("element"));
                context.Scene.TryGetElement(id, out IHierarchyElement element);

                return CommandResult.Success(DescribeElement(element, deep: false));
            },
            Required("element", "Element by id or name."));

        builder.AddQuery("bounds", "Reports the box an element's voxels occupy, in its own coordinates.",
            (context, arguments) =>
            {
                IVoxelModel model = GetVoxelModel(context, arguments, out IHierarchyElement element);
                VoxelExtent extent = VoxelExtent.Measure(model.Voxels.Keys);

                var description = new Dictionary<string, object?>
                {
                    ["element"] = element.Id.ToString(),
                    ["name"] = element.Name,
                    ["voxels"] = model.Voxels.Count
                };

                if (extent.IsEmpty)
                    return CommandResult.Success(description);

                description["min"] = ToArray(extent.Min);
                description["max"] = ToArray(extent.Max);
                description["size"] = ToArray(extent.Size);

                return CommandResult.Success(description);
            },
            Required("element", "Voxel model to measure, by id or name."));

        builder.AddQuery("slice", "Reads one layer of a voxel model as a grid of characters.",
            (context, arguments) =>
            {
                IVoxelModel model = GetVoxelModel(context, arguments, out IHierarchyElement element);

                return CommandResult.Success(VoxelSliceHelper.Describe(
                    element,
                    model,
                    context.Project,
                    arguments.Enum<Vector3.Axis>("axis"),
                    arguments.Int("index"),
                    arguments.Has("from") ? arguments.VectorI("from") : null,
                    arguments.Has("to") ? arguments.VectorI("to") : null));
            },
            Required("element", "Voxel model to read, by id or name."),
            Required("axis", "X, Y or Z — the axis the layer is cut across."),
            Required("index", "Which layer along that axis, in the model's own coordinates."),
            Optional("from", "One corner of the part to read, 'x,y,z'; the 'axis' component is "
                + "ignored. Defaults to the whole model."),
            Optional("to", "The opposite corner, 'x,y,z'; the 'axis' component is ignored."));

        builder.AddQuery("materials",
            "Lists the materials this project defines. The ones the editor ships with are "
            + "not among them and are listed by 'standard-materials'; both are accepted "
            + "wherever a material is named.",
            (context, _) => CommandResult.Success(context.Project.Materials
                .Select(material => (object?)new Dictionary<string, object?>
                {
                    ["id"] = material.Id.ToString(),
                    ["name"] = material.Name,
                    ["color"] = new[] { material.Color.R, material.Color.G, material.Color.B, material.Color.A },
                    ["appearance"] = DescribeAppearance(material.Appearance),
                    // Which ready-made material these values are, or 'custom'.
                    ["preset"] = MaterialPresetTable.Match(material.Appearance)?.ToString() ?? "custom",
                    // Why 'delete-material' would refuse, said before it is asked.
                    ["inUse"] = context.Project.IsMaterialInUse(material.Id)
                })
                .ToList()));

        builder.AddQuery("standard-materials",
            "Lists the materials the editor ships with. Every project has all of them, no "
            + "project saves any of them, and any command taking a material accepts one by "
            + "name. There are hundreds, so an answer holds "
            + StandardMaterialPage + " of them at a time: narrow it with 'contains', or walk "
            + "it with 'from'.",
            (context, arguments) =>
            {
                string filter = arguments.String("contains", string.Empty);
                int from = Math.Max(0, arguments.Int("from", 0));

                List<IMaterial> matchesList = context.Project.StandardMaterials
                    .Where(material => filter.Length == 0
                        || material.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Described a page at a time rather than all at once. Unbounded,
                // the whole library answered with 64 KB of JSON — past what a
                // client will carry, so the default call was the one that could
                // not be made. It also asked 'in use' three hundred times, and
                // that walks every cell of every scene: measured at 46 ms on a
                // 166k-cell model, all of it for an answer nobody could read.
                List<IMaterial> pageList = matchesList
                    .Skip(from)
                    .Take(StandardMaterialPage)
                    .ToList();

                var answer = new Dictionary<string, object?>
                {
                    ["matched"] = matchesList.Count,
                    ["of"] = context.Project.StandardMaterials.Count,
                    ["from"] = from,
                    ["count"] = pageList.Count,
                    ["materials"] = pageList
                        .Select(material => (object?)new Dictionary<string, object?>
                        {
                            ["id"] = material.Id.ToString(),
                            ["name"] = material.Name,
                            ["appearance"] = DescribeAppearance(material.Appearance),
                            ["inUse"] = context.Project.IsMaterialInUse(material.Id)
                        })
                        .ToList()
                };

                // Said out loud, never left to be inferred from a count: a page
                // that stopped short in silence reads as the whole library.
                if (from + pageList.Count < matchesList.Count)
                {
                    answer["more"] = $"{matchesList.Count - from - pageList.Count} more — "
                        + $"ask again with from={from + pageList.Count}, or narrow with 'contains'.";
                }

                return CommandResult.Success(answer);
            },
            Optional("contains", "Only the materials whose name holds this, ignoring case."),
            Optional("from", "Skip this many of the matches, to walk a long list a page at "
                + "a time. Defaults to 0."));

        builder.AddQuery("material-presets",
            "Lists the ready-made materials 'set-material-preset' takes, with what each "
            + "one is. The finishes named here are also the values 'set-material-appearance' "
            + "accepts for its 'finish' argument.",
            (_, _) => CommandResult.Success(MaterialPresetTable.All
                .Select(preset =>
                {
                    MaterialPresetEntry entry = MaterialPresetTable.Get(preset);

                    return (object?)new Dictionary<string, object?>
                    {
                        ["preset"] = preset.ToString(),
                        ["group"] = entry.Group.ToString(),
                        ["color"] = new[] { entry.Color.R, entry.Color.G, entry.Color.B, entry.Color.A },
                        ["appearance"] = DescribeAppearance(entry.Appearance)
                    };
                })
                .ToList()));
    }

    /// <summary>
    /// One shape for an appearance wherever it is reported, so a preset and a
    /// material are read the same way round.
    /// </summary>
    private static Dictionary<string, object?> DescribeAppearance(MaterialAppearance appearance) =>
        new()
        {
            ["roughness"] = appearance.Roughness,
            ["metallic"] = appearance.Metallic,
            ["emission"] = appearance.Emission,
            ["finish"] = appearance.Finish.ToString(),
            ["scale"] = appearance.FinishScale,
            ["relief"] = appearance.FinishRelief,
            ["transparent"] = appearance.Transparent
        };
}
