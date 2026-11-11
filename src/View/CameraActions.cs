using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The named input actions the viewport camera answers to, defined in the
/// project's input map rather than as keycodes in the camera.
/// </summary>
/// <remarks>
/// The camera does not care which key turns it, and saying so is what leaves
/// room for a remapping layer later: the binding lives in one editable place,
/// and the code reads an intention. It also means a key can be rebound without
/// a rebuild, and that the bindings are visible in Godot's own Input Map panel
/// instead of buried in a switch.
/// </remarks>
internal static class CameraActions
{
    public static readonly StringName TurnLeft = "camera_turn_left";
    public static readonly StringName TurnRight = "camera_turn_right";
    public static readonly StringName TurnUp = "camera_turn_up";
    public static readonly StringName TurnDown = "camera_turn_down";

    /// <summary>
    /// Put what matters on screen. The camera reports the request rather than
    /// answering it: what is worth framing is the selection, and the viewport is
    /// the last place that should know what a selection is.
    /// </summary>
    public static readonly StringName Focus = "camera_focus";

    /// <summary>All four, for matching an arriving event against the set.</summary>
    public static readonly IReadOnlyList<StringName> Turn = new[]
    {
        TurnLeft,
        TurnRight,
        TurnUp,
        TurnDown
    };
}
