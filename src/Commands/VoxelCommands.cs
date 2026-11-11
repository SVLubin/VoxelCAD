using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Geometry;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;
using static VoxelCAD.Commands.CommandCatalogHelper;

namespace VoxelCAD.Commands;

/// <summary>The cells themselves: painted, filled, and mirrored as they are written.</summary>
internal static class VoxelCommands
{
    /// <summary>
    /// A ceiling on how many cells one <c>fill-box</c> may cover, so an out-of-range
    /// corner cannot allocate an unbounded edit list. A million comfortably fills any
    /// plausible model — a 100-cell cube is already at the limit — while turning a
    /// mistyped corner into a refusal rather than an out-of-memory crash. Unlike
    /// <c>slice</c>'s cap this is about bounding allocation, not readability.
    /// </summary>
    private const long MaximumFillCells = 1_000_000;

    public static void AddTo(CommandCatalogBuilder builder)
    {
        builder.Add("set-voxels", "Fills or clears cells of a voxel model, as one undoable action.",
            (context, arguments) =>
            {
                Guid elementId = context.ResolveElement(arguments.String("element"));
                Guid? materialId = ResolveOptionalMaterial(context, arguments);

                List<VoxelEdit> editsList = arguments.Cells("cells")
                    .Select(cell => new VoxelEdit(cell, materialId))
                    .ToList();

                context.Scene.SetVoxels(elementId, editsList);

                return CommandResult.Success(CountVoxels(context, elementId));
            },
            Required("element", "Voxel model to edit, by id or name."),
            Required("cells", "'x,y,z;x,y,z' in the model's own coordinates."),
            Optional("material", "Material by id or name. Omit, or pass 'none', to erase."));

        builder.Add("fill-box", "Fills or clears a solid box of cells, as one undoable action.",
            (context, arguments) =>
            {
                Guid elementId = context.ResolveElement(arguments.String("element"));
                Guid? materialId = ResolveOptionalMaterial(context, arguments);

                Vector3I from = arguments.VectorI("from");
                Vector3I to = arguments.VectorI("to");

                // The corners are in unbounded local coordinates, and nothing below
                // bounds them either — the region does not limit where cells go —
                // so a typo like 'to 1000,1000,1000' would ask for a billion edits
                // and exhaust memory. Refuse an absurd box up front, naming its
                // size, the way 'slice' caps a read. The dimensions are guarded
                // before the volume is formed so the product cannot overflow.
                long width = Math.Abs((long)to.X - from.X) + 1;
                long height = Math.Abs((long)to.Y - from.Y) + 1;
                long depth = Math.Abs((long)to.Z - from.Z) + 1;

                if (width > MaximumFillCells || height > MaximumFillCells || depth > MaximumFillCells
                    || width * height * depth > MaximumFillCells)
                {
                    throw new CommandException(
                        $"That box is {width} by {height} by {depth} cells, over the "
                        + $"{MaximumFillCells} 'fill-box' will place at once. Check 'from' and "
                        + "'to', or fill the region in parts.");
                }

                var editsList = new List<VoxelEdit>((int)(width * height * depth));

                for (var x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
                {
                    for (var y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
                    {
                        for (var z = Math.Min(from.Z, to.Z); z <= Math.Max(from.Z, to.Z); z++)
                            editsList.Add(new VoxelEdit(new Vector3I(x, y, z), materialId));
                    }
                }

                context.Scene.SetVoxels(elementId, editsList);

                return CommandResult.Success(CountVoxels(context, elementId));
            },
            Required("element", "Voxel model to edit, by id or name."),
            Required("from", "One corner, 'x,y,z', inclusive."),
            Required("to", "The opposite corner, 'x,y,z', inclusive."),
            Optional("material", "Material by id or name. Omit, or pass 'none', to erase."));

        builder.Add("extrude",
            "Pushes a face of a voxel model out by whole layers, or carves it in. The face "
            + "is the connected run of surface the named cell shows in that direction — the "
            + "same patch the extrude tool takes hold of — and the new cells carry that "
            + "cell's own material, so an extrusion continues the surface it grew from. "
            + "Mirror planes apply as they do to the brush. Read the result with 'slice'.",
            (context, arguments) =>
            {
                IVoxelModel model = GetVoxelModel(context, arguments, out IHierarchyElement element);
                Vector3I cell = arguments.VectorI("cell");
                IReadOnlyList<int> faces = ParseFaceNames(arguments.String("face"));
                int layers = arguments.Int("layers");

                if (faces.Count != 1)
                {
                    throw new CommandException(
                        "Argument 'face' names one face — the one to take hold of — "
                        + $"got {faces.Count}.");
                }

                if (!model.Voxels.TryGetValue(cell, out Guid material))
                {
                    throw new CommandException(
                        $"'{element.Name}' has no cell at {cell.X},{cell.Y},{cell.Z}, so there "
                        + "is no face there to extrude. Find the surface with 'slice'.");
                }

                SymmetryValues symmetry = model.Symmetry;

                IReadOnlyList<Vector3I> patch =
                    VoxelFacePatch.CollectPatch(model.Voxels, cell, faces[0]);

                if (patch.Count == 0)
                {
                    throw new CommandException(
                        $"The {FaceNames[faces[0]]} face of {cell.X},{cell.Y},{cell.Z} is "
                        + "buried — there is another cell against it, so it is not a face of "
                        + "the model at all.");
                }

                // The same guard 'fill-box' puts on its corners, for the same
                // reason: a mistyped layer count must be a refusal naming its
                // size, not an out-of-memory crash. Mirrors multiply the write,
                // so they multiply the estimate — refused before anything the
                // size of the answer is allocated.
                var mirrorFactor = 1;

                if (symmetry.X.Enabled)
                    mirrorFactor *= 2;

                if (symmetry.Y.Enabled)
                    mirrorFactor *= 2;

                if (symmetry.Z.Enabled)
                    mirrorFactor *= 2;

                long requested = (long)patch.Count * Math.Abs((long)layers) * mirrorFactor;

                if (requested > MaximumFillCells)
                {
                    throw new CommandException(
                        $"That face is {patch.Count} cells, so {layers} layers would write "
                        + $"up to {requested} cells — over the {MaximumFillCells} 'extrude' "
                        + "will place at once. Reduce 'layers'.");
                }

                IReadOnlyList<Vector3I> cells = VoxelFacePatch.CollectExtrusion(
                    model.Voxels,
                    cell,
                    faces[0],
                    layers,
                    symmetry.X.Enabled ? symmetry.X.PlaneDoubled : null,
                    symmetry.Y.Enabled ? symmetry.Y.PlaneDoubled : null,
                    symmetry.Z.Enabled ? symmetry.Z.PlaneDoubled : null);

                // A carve clears; a push fills with the face's own material.
                Guid? written = layers < 0 ? null : material;
                var editsList = new List<VoxelEdit>(cells.Count);

                foreach (Vector3I target in cells)
                    editsList.Add(new VoxelEdit(target, written));

                context.Scene.SetVoxels(element.Id, editsList);

                Dictionary<string, object?> result = CountVoxels(context, element.Id);
                result["patchCells"] = patch.Count;
                result["writtenCells"] = cells.Count;

                return CommandResult.Success(result);
            },
            Required("element", "Voxel model to edit, by id or name."),
            Required("cell", "'x,y,z' — a filled cell of the face, in the model's own coordinates."),
            Required("face", "Which of its faces to take hold of: +x, -x, +y, -y, +z or -z."),
            Required("layers",
                "Whole layers to push the face out by. Negative carves the same face in, "
                + "removing that many layers of cells."));

        builder.Add("fill-connected",
            "Recolours the connected volume one cell belongs to: every filled cell "
            + "reachable from it across shared faces without leaving cells of its own "
            + "material, given the named material as one undoable action. Empty cells "
            + "are never part of it — a fill does not pour into a cavity; 'fill-box' does "
            + "that. Mirror planes apply: each image of the named cell that wears the same "
            + "material seeds a volume of its own, and an image on another material or on "
            + "an empty cell seeds nothing, so this is exactly what the Fill tool's click "
            + "does. Read the result with 'slice'.",
            (context, arguments) =>
            {
                IVoxelModel model = GetVoxelModel(context, arguments, out IHierarchyElement element);
                Vector3I seed = arguments.VectorI("at");
                Guid materialId = context.ResolveMaterial(arguments.String("material"));

                if (!model.Voxels.ContainsKey(seed))
                {
                    throw new CommandException(
                        $"'{element.Name}' has no cell at {seed.X},{seed.Y},{seed.Z}, so there is "
                        + "no volume there to fill. Find one with 'slice'.");
                }

                SymmetryValues symmetry = model.Symmetry;

                IReadOnlyList<Vector3I> volume = VoxelFlood.CollectOrbitConnected(
                    model.Voxels,
                    seed,
                    symmetry.X.Enabled ? symmetry.X.PlaneDoubled : null,
                    symmetry.Y.Enabled ? symmetry.Y.PlaneDoubled : null,
                    symmetry.Z.Enabled ? symmetry.Z.PlaneDoubled : null);

                var editsList = new List<VoxelEdit>(volume.Count);

                foreach (Vector3I cell in volume)
                    editsList.Add(new VoxelEdit(cell, materialId));

                context.Scene.SetVoxels(element.Id, editsList);

                Dictionary<string, object?> result = CountVoxels(context, element.Id);
                result["volumeCells"] = volume.Count;

                return CommandResult.Success(result);
            },
            Required("element", "Voxel model to edit, by id or name."),
            Required("at", "'x,y,z' — a filled cell of the volume, in the model's own coordinates."),
            Required("material", "Material to give the volume, by id or name."));

        builder.Add("mirror-element",
            "Writes the mirror image of every cell across one of the model's own mirror "
            + "planes, wherever the image lands on an empty cell, as one undoable action. "
            + "A cell already standing where an image would go wins and is left alone, so "
            + "an already symmetric model gains nothing. One-shot and literal: this is the "
            + "retroactive half of symmetry, which as a brush modifier mirrors nothing "
            + "already built. Read the result with 'slice'.",
            (context, arguments) =>
            {
                IVoxelModel model = GetVoxelModel(context, arguments, out IHierarchyElement element);
                var axis = arguments.Enum<Vector3.Axis>("axis");

                // The plane the inspector's row shows for that axis, whether or not the
                // slot is on — a disabled slot keeps its position — unless one is named.
                SymmetryPlane slot = SymmetrySlotOf(model.Symmetry, axis);

                // 'off' is 'set-symmetry''s word and means nothing here: this is a
                // one-shot, so there is no slot to switch off, and taking it as a
                // plane mirrored across the stored local origin — a point nobody
                // chose — rather than across anything a surface ever showed.
                if (arguments.Has("plane")
                    && string.Equals(
                        arguments.String("plane"), "off", StringComparison.OrdinalIgnoreCase))
                {
                    throw new CommandException(
                        "Argument 'plane' is an offset from the pivot; 'off' belongs to "
                        + "'set-symmetry', which switches a mirror slot off. Mirroring is "
                        + "one-shot: omit 'plane' to use the plane the element's own slot "
                        + "on that axis holds, whether or not it is on.");
                }

                int planeDoubled = arguments.Has("plane")
                    ? ParseSymmetryPlane(arguments, slot, element, axis).PlaneDoubled
                    : SymmetryUnits.ShownPlaneDoubled(model.Symmetry, element.PivotPoint, (int)axis);

                IReadOnlyList<(Vector3I Cell, Guid Material)> images =
                    VoxelSymmetry.CollectMirrorImages(model.Voxels, (int)axis, planeDoubled);

                var editsList = new List<VoxelEdit>(images.Count);

                foreach ((Vector3I cell, Guid material) in images)
                    editsList.Add(new VoxelEdit(cell, material));

                context.Scene.SetVoxels(element.Id, editsList);

                Dictionary<string, object?> result = CountVoxels(context, element.Id);
                result["writtenCells"] = images.Count;
                result["plane"] = SymmetryUnits.ToOffsetDoubled(
                    planeDoubled, element.PivotPoint, (int)axis) / 2f;

                return CommandResult.Success(result);
            },
            Required("element", "Voxel model to mirror, by id or name."),
            Required("axis", "X, Y or Z — the local axis the mirror plane is normal to."),
            Optional("plane",
                "Offset from the element's pivot along that axis, a multiple of 0.5 — the "
                + "same unit 'set-symmetry' takes. Defaults to the plane the model's own slot "
                + "on that axis holds, on or off, which is what the inspector's Mirror button "
                + "uses."));

        builder.Add("set-symmetry",
            "Sets one of a voxel model's three mirror planes. A brush modifier, not a change "
            + "to cells: the viewport's brush writes the whole orbit of a click while a plane "
            + "is on, and 'set-voxels' stays literal either way.",
            (context, arguments) =>
            {
                IVoxelModel model = GetVoxelModel(context, arguments, out IHierarchyElement element);
                var axis = arguments.Enum<Vector3.Axis>("axis");

                context.Scene.SetSymmetry(
                    element.Id,
                    WithSymmetrySlot(
                        model.Symmetry,
                        axis,
                        ParseSymmetryPlane(
                            arguments, SymmetrySlotOf(model.Symmetry, axis), element, axis)));

                return CommandResult.Success(DescribeSymmetry(model.Symmetry, element.PivotPoint));
            },
            Required("element", "Voxel model to change, by id or name."),
            Required("axis", "X, Y or Z — the local axis the mirror plane is normal to."),
            Required("plane",
                "Offset from the element's pivot along that axis, a multiple of 0.5 — 0 puts "
                + "the mirror exactly through the pivot — or 'off' to disable the slot where "
                + "it stands. Moving the pivot later does not move the plane; it re-labels "
                + "this offset."));
    }

    private static Guid? ResolveOptionalMaterial(CommandContext context, CommandArguments arguments)
    {
        var text = arguments.String("material", "none");

        return string.Equals(text, "none", StringComparison.OrdinalIgnoreCase)
            ? null
            : context.ResolveMaterial(text);
    }

    private static Dictionary<string, object?> CountVoxels(CommandContext context, Guid elementId)
    {
        context.Scene.TryGetElement(elementId, out IHierarchyElement element);

        return new Dictionary<string, object?>
        {
            ["voxels"] = element.Component is IVoxelModel model ? model.Voxels.Count : 0
        };
    }

    /// <summary>
    /// 'off' keeps the slot's position — switching the plane back on finds it
    /// where it was left. Anything else is an <b>offset from the pivot</b> on the
    /// half-cell lattice, converted to the absolute plane the model stores: the
    /// surfaces speak in pivot units, the cells do not move when the pivot does.
    /// </summary>
    private static SymmetryPlane ParseSymmetryPlane(
        CommandArguments arguments,
        SymmetryPlane slot,
        IHierarchyElement element,
        Vector3.Axis axis)
    {
        var text = arguments.String("plane");

        if (string.Equals(text, "off", StringComparison.OrdinalIgnoreCase))
            return slot with { Enabled = false };

        float doubled = arguments.Float("plane") * 2f;

        if (doubled != MathF.Round(doubled) || Math.Abs(doubled) > int.MaxValue)
        {
            throw new CommandException(
                $"Argument 'plane' must be a multiple of 0.5 — an offset from the pivot, "
                + $"0 lying exactly on it — got '{text}'.");
        }

        return new SymmetryPlane(
            true,
            SymmetryUnits.ToAbsoluteDoubled((int)doubled, element.PivotPoint, (int)axis));
    }

    private static SymmetryPlane SymmetrySlotOf(SymmetryValues values, Vector3.Axis axis) =>
        axis switch
        {
            Vector3.Axis.X => values.X,
            Vector3.Axis.Y => values.Y,
            Vector3.Axis.Z => values.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };

    private static SymmetryValues WithSymmetrySlot(
        SymmetryValues values,
        Vector3.Axis axis,
        SymmetryPlane slot) =>
        axis switch
        {
            Vector3.Axis.X => values with { X = slot },
            Vector3.Axis.Y => values with { Y = slot },
            Vector3.Axis.Z => values with { Z = slot },
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };
}
