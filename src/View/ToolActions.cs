using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The named input actions that switch the viewport's tool, defined in the
/// project's input map rather than as keycodes in the viewport.
/// </summary>
/// <remarks>
/// Same bargain as <see cref="CameraActions"/>: the binding lives in one editable
/// place the engine's own Input Map panel shows, and the code reads an intention.
/// The pairing with <see cref="ToolKind"/> lives here too, so the viewport matches
/// an arriving key against a list instead of carrying a switch over keycodes.
/// </remarks>
internal static class ToolActions
{
    public static readonly StringName Select = "tool_select";
    public static readonly StringName Paint = "tool_paint";
    public static readonly StringName Move = "tool_move";
    public static readonly StringName Rotate = "tool_rotate";
    public static readonly StringName Scale = "tool_scale";
    public static readonly StringName Eyedropper = "tool_eyedropper";
    public static readonly StringName Extrude = "tool_extrude";
    public static readonly StringName Fill = "tool_fill";

    /// <summary>All of them, for matching an arriving event against the set.</summary>
    public static readonly IReadOnlyList<(StringName Action, ToolKind Tool)> Tools = new[]
    {
        (Select, ToolKind.Select),
        (Paint, ToolKind.Paint),
        (Move, ToolKind.Move),
        (Rotate, ToolKind.Rotate),
        (Scale, ToolKind.Scale),
        (Eyedropper, ToolKind.Eyedropper),
        (Extrude, ToolKind.Extrude),
        (Fill, ToolKind.Fill)
    };

    /// <summary>
    /// Which tool a key asks for, if any. Both panels that read tool keys go
    /// through here, so the modifier rule below is one answer rather than two.
    /// </summary>
    /// <remarks>
    /// <b>Exact on the modifiers</b>, which Godot's own action matching is not:
    /// with the default <c>exactMatch: false</c> a binding's modifiers only have to
    /// be a subset of the event's, so a bare X answers Cmd+X — and the extruder
    /// would have taken the shell's cut, claiming the event on its way past. A
    /// bare-key binding must not answer a chord.
    /// </remarks>
    public static bool TryResolve(InputEventKey key, out ToolKind tool)
    {
        foreach ((StringName action, ToolKind candidate) in Tools)
        {
            if (!key.IsAction(action, exactMatch: true))
                continue;

            tool = candidate;

            return true;
        }

        tool = default;

        return false;
    }
}
