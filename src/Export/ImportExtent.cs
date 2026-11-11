using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Export;

/// <summary>
/// The box an import's cells occupy, walked before anything is created.
/// </summary>
internal static class ImportExtent
{
    public static Vector3I Measure(IReadOnlyList<ImportedNode> rootsList, out Vector3I minimum)
    {
        var found = false;
        Vector3I lowest = Vector3I.Zero;
        Vector3I highest = Vector3I.Zero;

        Walk(rootsList, CellTransform.Identity, ref found, ref lowest, ref highest);

        minimum = lowest;

        return found ? highest - lowest + Vector3I.One : Vector3I.Zero;
    }

    private static void Walk(
        IReadOnlyList<ImportedNode> nodesList,
        CellTransform outer,
        ref bool found,
        ref Vector3I lowest,
        ref Vector3I highest)
    {
        foreach (ImportedNode node in nodesList)
        {
            CellTransform placement = outer.Compose(node.Placement);

            if (node.Volume != null)
            {
                foreach (ImportedCell cell in node.Volume.Cells)
                {
                    Vector3I world = placement.Apply(cell.Cell);

                    lowest = found ? Lower(lowest, world) : world;
                    highest = found ? Higher(highest, world) : world;
                    found = true;
                }
            }

            Walk(node.Children, placement, ref found, ref lowest, ref highest);
        }
    }

    private static Vector3I Lower(Vector3I first, Vector3I second) => new(
        Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Min(first.Z, second.Z));

    private static Vector3I Higher(Vector3I first, Vector3I second) => new(
        Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), Math.Max(first.Z, second.Z));
}
