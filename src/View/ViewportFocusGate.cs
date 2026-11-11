using Godot;

namespace VoxelCAD.View;

/// <summary>
/// Which input reaches the viewport's camera and tools, and which input merely
/// hands it the keyboard. Being pointed at is not being listened to: a panel that
/// is not the focused one has no business zooming, orbiting or answering a
/// trackpad gesture just because the cursor happens to be over it.
/// </summary>
/// <remarks>
/// The wheel is the case that made this a rule rather than a taste. It is a
/// <see cref="InputEventMouseButton"/> like any other, so without a gate a scroll
/// over an inactive viewport turned the camera while the user was working in a
/// dock — and a trackpad pinch did the same through
/// <see cref="InputEventMagnifyGesture"/>. Wheel notches therefore do not activate
/// either: a scroll is not a click, and a viewport that grabbed the keyboard
/// because the cursor drifted over it would take it from the panel being typed in.
/// </remarks>
internal static class ViewportFocusGate
{
    /// <summary>
    /// Whether a press on this button hands the viewport the keyboard. Any real
    /// button does — the click that activates the panel is also the click that
    /// acts in it, which is what the engine already does for the left button and
    /// what keeps a middle-button orbit from needing a click first.
    /// </summary>
    public static bool Activates(MouseButton button, bool pressed) =>
        pressed && !IsWheel(button);

    /// <summary>
    /// The four wheel directions, which Godot reports as buttons. Named here
    /// because "is this a click" is the question the gate is really asking.
    /// </summary>
    public static bool IsWheel(MouseButton button) =>
        button is MouseButton.WheelUp
            or MouseButton.WheelDown
            or MouseButton.WheelLeft
            or MouseButton.WheelRight;
}
