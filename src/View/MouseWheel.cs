using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Whether a mouse button is a wheel notch rather than a button being pressed.
/// </summary>
/// <remarks>
/// Godot delivers a scroll as an <c>InputEventMouseButton</c> with
/// <c>Pressed</c> true, so anything testing for a press catches every notch of
/// every scroll as well. That is not a distinction worth re-deriving per
/// surface: the render card learned it by closing itself and zooming the scene
/// behind it in one gesture, and the settings card had the identical defect
/// written out a second time, where a scroll the form could not answer put the
/// form away. One answer, asked by both.
/// </remarks>
internal static class MouseWheel
{
    public static bool IsNotch(MouseButton button) =>
        button is MouseButton.WheelUp
            or MouseButton.WheelDown
            or MouseButton.WheelLeft
            or MouseButton.WheelRight;
}
