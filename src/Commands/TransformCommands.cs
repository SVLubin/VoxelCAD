using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using static VoxelCAD.Commands.CommandCatalogBuilder;
using static VoxelCAD.Commands.CommandCatalogHelper;

namespace VoxelCAD.Commands;

/// <summary>An element's transform: the three channels, their modes, and the pivot.</summary>
internal static class TransformCommands
{
    public static void AddTo(CommandCatalogBuilder builder)
    {
        builder.Add("set-transform",
            "Sets one transform channel of an element. Position is where the element's pivot "
            + "stands in the parent frame; rotation and scale act about that point and leave it "
            + "where it is.",
            (context, arguments) =>
            {
                context.Scene.SetTransform(
                    context.ResolveElement(arguments.String("element")),
                    arguments.Enum<TransformChannel>("channel"),
                    arguments.Vector("value"));

                return CommandResult.Success();
            },
            Required("element", "Element to change, by id or name."),
            Required("channel", "Position, Rotation or Scale."),
            Required("value",
                "'x,y,z'. Rotation is in degrees. A position lands on the integer lattice "
                + "shifted by the pivot's own fraction — with the pivot on a half cell, "
                + "asking for 5 gives 5.5 — because the cells must stay on the world grid."));

        builder.Add("set-field-mode", "Sets how a transform channel is constrained.",
            (context, arguments) =>
            {
                context.Scene.SetFieldMode(
                    context.ResolveElement(arguments.String("element")),
                    arguments.Enum<TransformChannel>("channel"),
                    arguments.Enum<FieldMode>("mode"));

                return CommandResult.Success();
            },
            Required("element", "Element to change, by id or name."),
            Required("channel", "Position, Rotation or Scale."),
            Required("mode", "Float, Integer, or Snap90 for rotation only."));

        builder.Add("set-pivot",
            "Moves the point an element's position names and its handles act about, in its own "
            + "cells. The geometry does not move — what changes is which point the position "
            + "channel reports, so 'element' shows a new position and the same cells.",
            (context, arguments) =>
            {
                Guid elementId = context.ResolveElement(arguments.String("element"));

                context.Scene.TryGetElement(elementId, out IHierarchyElement element);

                Vector3 point;

                if (string.Equals(
                    arguments.String("point"), "center", StringComparison.OrdinalIgnoreCase))
                {
                    if (!PivotCenter.TryCalculate(element, out point))
                    {
                        throw new CommandException(
                            "Nothing to centre on: the element holds no voxels and nothing "
                            + "under it does either. Give an explicit point instead.");
                    }
                }
                else
                {
                    // The offset-from-centre unit every surface speaks; 'center'
                    // above is the same thing spelled without arithmetic.
                    point = PivotUnits.ToAbsolute(arguments.Vector("point"), element);
                }

                context.Scene.SetPivot(elementId, point);

                context.Scene.TryGetElement(elementId, out IHierarchyElement after);

                return CommandResult.Success(new Dictionary<string, object?>
                {
                    ["pivot"] = ToArray(PivotUnits.ToOffset(after.PivotPoint, after)),
                    ["position"] = ToArray(after.Transform.Position)
                });
            },
            Required("element", "Element to change, by id or name."),
            Required("point",
                "'x,y,z' as an offset from the middle of what the element holds, rounded to "
                + "the half-cell lattice — 0,0,0 is the centre of the model — or 'center', "
                + "the same thing spelled out. Measured against the cells as they stand now: "
                + "later painting re-labels the offset and moves nothing."));
    }
}
