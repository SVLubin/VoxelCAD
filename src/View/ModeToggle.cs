using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Compact mode switch for a whole vector, never per axis — one
/// <see cref="SegmentedRow"/>, so the choices are visibly one question with one
/// answer. Which modes it offers is told to it, never decided here: the model
/// owns that rule.
/// </summary>
/// <remarks>
/// All that is left here is the vocabulary — what each mode is called and what
/// its hint says — because that is the part a channel can change: Integer is a
/// step of one cell on position and scale and one degree on rotation.
/// </remarks>
internal sealed partial class ModeToggle : SegmentedRow
{
    private readonly List<FieldMode> _modesList = new();
    private readonly bool _angular;

    /// <param name="angular">
    /// Whether the channel is measured in degrees. It decides one word:
    /// <see cref="FieldMode.Integer"/> is a step of one <em>cell</em> on position
    /// and scale and one <em>degree</em> on rotation, and calling both of them
    /// "1" was the toggle claiming they were the same thing.
    /// </param>
    public ModeToggle(bool angular)
    {
        _angular = angular;

        Chosen += index => ModeSelected?.Invoke(_modesList[index]);
    }

    public event Action<FieldMode>? ModeSelected;

    public void SetAllowedModes(IReadOnlyList<FieldMode> modes)
    {
        _modesList.Clear();
        _modesList.AddRange(modes);

        var segments = new SegmentSpec[modes.Count];

        for (var index = 0; index < modes.Count; index++)
        {
            FieldMode mode = modes[index];

            segments[index] = new SegmentSpec(
                $"Mode{mode}", LabelFor(mode, _angular), HintFor(mode, _angular));
        }

        SetSegments(segments);
    }

    /// <summary>Reflects model state without reporting it back as a user edit.</summary>
    public void SetMode(FieldMode mode) => Select(_modesList.IndexOf(mode));

    /// <summary>
    /// A word for the free mode and the step for the others, because that is
    /// what the choice is: snap to this much, or to nothing. <c>.0</c> stood for
    /// the first and was read by nobody.
    /// </summary>
    internal static string LabelFor(FieldMode mode, bool angular) => mode switch
    {
        FieldMode.Integer => angular ? "1°" : "1",
        FieldMode.Float => "Free",
        FieldMode.Snap90 => "90°",
        _ => "?"
    };

    internal static string HintFor(FieldMode mode, bool angular) => mode switch
    {
        FieldMode.Integer => angular
            ? "Whole degrees."
            : "Whole cells. The value lands on the grid the voxels sit on.",
        FieldMode.Float => "Any value, snapped to nothing.",
        FieldMode.Snap90 => "Quarter turns, so the cells stay square to the room.",
        _ => string.Empty
    };
}
