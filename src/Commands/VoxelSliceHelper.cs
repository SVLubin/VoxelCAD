using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using VoxelCAD.Geometry;
using VoxelCAD.Model;

namespace VoxelCAD.Commands;

/// <summary>
/// Renders one layer of a voxel model as a character grid. This is how an agent
/// reads back what it built: a count says a model exists, a slice says what shape
/// it is.
/// </summary>
/// <remarks>
/// The output is shaped for a reader that has to be certain, not for one skimming.
/// Characters come from the material's own name — <c>S</c> for Stone — so a grid
/// is legible before the legend is consulted, and they are chosen over the whole
/// model rather than the current layer, so the same material keeps the same
/// character across every slice of it. Empty cells are a full stop: whatever is
/// filled should be what stands out.
/// </remarks>
internal static class VoxelSliceHelper
{
    /// <summary>What an empty cell reads as.</summary>
    public const char EmptyCharacter = '.';

    /// <summary>
    /// Labels for materials whose own names are used up or collide, in order. Any
    /// past the end share <see cref="OverflowCharacter"/> — a legend that shows
    /// the collision beats a read that fails on a model nobody will build.
    /// </summary>
    private const string FallbackCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789#@%&*+=<>~";

    private const char OverflowCharacter = '?';

    /// <summary>
    /// A cap on one answer, in cells. A 32-wide slice is a thousand characters and
    /// costs nothing; a 256-wide one is sixty-five thousand, which is no longer
    /// something to read — it is something to window into.
    /// </summary>
    private const int MaximumCells = 4096;

    public static Dictionary<string, object?> Describe(
        IHierarchyElement element,
        IVoxelModel model,
        IProject project,
        Vector3.Axis axis,
        int index,
        Vector3I? windowFrom,
        Vector3I? windowTo)
    {
        (Vector3.Axis columnAxis, Vector3.Axis rowAxis, bool rowDescending) = GetPlane(axis);

        var description = new Dictionary<string, object?>
        {
            ["element"] = element.Id.ToString(),
            ["name"] = element.Name,
            ["axis"] = axis.ToString(),
            ["index"] = index,
            ["emptyChar"] = EmptyCharacter.ToString()
        };

        VoxelExtent extent = VoxelExtent.Measure(model.Voxels.Keys);

        if (extent.IsEmpty && windowFrom == null)
        {
            // Nothing has been built yet. Inventing a window over an empty model
            // would answer with a wall of full stops that says the same thing.
            description["filled"] = 0;
            description["grid"] = new List<object?>();

            return description;
        }

        (int columnFrom, int columnTo) = GetRange(extent, columnAxis, windowFrom, windowTo);
        (int rowFrom, int rowTo) = GetRange(extent, rowAxis, windowFrom, windowTo);

        var columnCount = columnTo - columnFrom + 1;
        var rowCount = rowTo - rowFrom + 1;

        if ((long)columnCount * rowCount > MaximumCells)
        {
            throw new CommandException(
                $"That slice is {columnCount} by {rowCount} = {(long)columnCount * rowCount} cells, "
                + $"over the {MaximumCells} an answer carries. Pass 'from' and 'to' to read part "
                + "of it, or 'bounds' first to see where the model actually is.");
        }

        IReadOnlyDictionary<Guid, char> charactersByMaterialDictionary = AssignCharacters(model, project);
        var countsByMaterialDictionary = new Dictionary<Guid, int>();
        var gridList = new List<object?>(rowCount);
        var builder = new StringBuilder(columnCount);
        var filled = 0;

        for (var rowStep = 0; rowStep < rowCount; rowStep++)
        {
            int row = rowDescending ? rowTo - rowStep : rowFrom + rowStep;
            builder.Clear();

            for (int column = columnFrom; column <= columnTo; column++)
            {
                Vector3I cell = default;
                cell[(int)axis] = index;
                cell[(int)columnAxis] = column;
                cell[(int)rowAxis] = row;

                if (!model.TryGetVoxel(cell, out Guid materialId))
                {
                    builder.Append(EmptyCharacter);
                    continue;
                }

                builder.Append(charactersByMaterialDictionary.TryGetValue(materialId, out char label)
                    ? label
                    : OverflowCharacter);

                countsByMaterialDictionary.TryGetValue(materialId, out int count);
                countsByMaterialDictionary[materialId] = count + 1;
                filled++;
            }

            gridList.Add(builder.ToString());
        }

        description["columnAxis"] = columnAxis.ToString();
        description["columnFrom"] = columnFrom;
        description["columnTo"] = columnTo;
        description["rowAxis"] = rowAxis.ToString();

        // First row first, whichever way that runs: an elevation reads right way up
        // when Y descends, and a plan reads front to back when Z ascends. Saying
        // both ends beats asking the reader to remember which is which.
        description["rowFrom"] = rowDescending ? rowTo : rowFrom;
        description["rowTo"] = rowDescending ? rowFrom : rowTo;
        description["filled"] = filled;
        description["legend"] = ComposeLegend(project, charactersByMaterialDictionary, countsByMaterialDictionary);
        description["grid"] = gridList;

        return description;
    }

    /// <summary>
    /// The two axes a slice is drawn on, and which way its rows run. Columns always
    /// ascend; rows descend when the row axis is up, so a model does not come out
    /// standing on its head.
    /// </summary>
    private static (Vector3.Axis Column, Vector3.Axis Row, bool RowDescending) GetPlane(Vector3.Axis axis) =>
        axis switch
        {
            Vector3.Axis.X => (Vector3.Axis.Z, Vector3.Axis.Y, true),
            Vector3.Axis.Y => (Vector3.Axis.X, Vector3.Axis.Z, false),
            Vector3.Axis.Z => (Vector3.Axis.X, Vector3.Axis.Y, true),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };

    /// <summary>
    /// How far the grid runs along one axis: what the caller asked for, else what
    /// the model occupies — so a slice of a small model in a big scene is small.
    /// </summary>
    private static (int From, int To) GetRange(
        VoxelExtent extent,
        Vector3.Axis axis,
        Vector3I? windowFrom,
        Vector3I? windowTo)
    {
        if (windowFrom is not { } from)
            return (extent.Min[(int)axis], extent.Max[(int)axis]);

        // One corner is enough to window with; the other defaults to the model's
        // own edge rather than refusing the read.
        int low = from[(int)axis];
        int high = windowTo is { } to ? to[(int)axis] : extent.Max[(int)axis];

        return low <= high ? (low, high) : (high, low);
    }

    /// <summary>
    /// One character per material the model uses, in project order, so a character
    /// means the same thing in every slice of the same model. Materials the model
    /// does not use get none: a legend listing the whole registry is noise.
    /// </summary>
    private static IReadOnlyDictionary<Guid, char> AssignCharacters(IVoxelModel model, IProject project)
    {
        var usedHashSet = new HashSet<Guid>(model.Voxels.Values);
        var charactersByMaterialDictionary = new Dictionary<Guid, char>();
        var takenHashSet = new HashSet<char> { EmptyCharacter };

        foreach (IMaterial material in project.AllMaterials)
        {
            if (usedHashSet.Contains(material.Id))
                charactersByMaterialDictionary[material.Id] = ChooseCharacter(material.Name, takenHashSet);
        }

        return charactersByMaterialDictionary;
    }

    private static char ChooseCharacter(string name, HashSet<char> takenHashSet)
    {
        foreach (char letter in name)
        {
            char candidate = char.ToUpperInvariant(letter);

            if (char.IsLetterOrDigit(candidate) && takenHashSet.Add(candidate))
                return candidate;
        }

        foreach (char candidate in FallbackCharacters)
        {
            if (takenHashSet.Add(candidate))
                return candidate;
        }

        return OverflowCharacter;
    }

    private static List<object?> ComposeLegend(
        IProject project,
        IReadOnlyDictionary<Guid, char> charactersByMaterialDictionary,
        IReadOnlyDictionary<Guid, int> countsByMaterialDictionary)
    {
        var legendList = new List<object?>();

        foreach (IMaterial material in project.AllMaterials)
        {
            // Only what this layer actually shows. A character assigned to a
            // material the slice does not reach explains nothing about it.
            if (!countsByMaterialDictionary.TryGetValue(material.Id, out int count))
                continue;

            legendList.Add(new Dictionary<string, object?>
            {
                ["char"] = charactersByMaterialDictionary.TryGetValue(material.Id, out char label)
                    ? label.ToString()
                    : OverflowCharacter.ToString(),
                ["material"] = material.Name,
                ["id"] = material.Id.ToString(),
                ["cells"] = count
            });
        }

        return legendList;
    }
}
