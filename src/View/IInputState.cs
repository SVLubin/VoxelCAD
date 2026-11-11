using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The pointer and keys of <em>whoever is driving the editor</em> — not of the
/// machine's devices.
/// </summary>
/// <remarks>
/// The distinction is the whole point, and its absence was a real defect. Input
/// reaches the editor by two channels: events, which are routed through the tree
/// and can be swallowed, and <b>state</b>, which the tools poll because they need
/// an answer on a frame where no event arrived — is Shift down, where is the
/// pointer, is the button still held. Polling <c>Input</c> directly answers those
/// about the physical keyboard and mouse, and that was correct for exactly as long
/// as one party drove the editor.
/// <para>
/// With a handover there are two, and control is exclusive. Commands honour that,
/// and the banner makes events honour it; the polled state did not, so while an
/// agent held the editor the user's Shift still flipped the brush to erase, their
/// pointer still moved the preview, and releasing their mouse button could end the
/// agent's drag. Measured, not theorised.
/// </para>
/// <para>
/// So the tools read this instead, and it answers for the party that holds
/// control. Nothing else about them changes.
/// </para>
/// </remarks>
internal interface IInputState
{
    /// <summary>
    /// Where the pointer is, in the canvas coordinates the camera projects rays
    /// from and a control's rect is measured in.
    /// </summary>
    /// <returns>
    /// False when there is no pointer at all — an agent that has not moved one
    /// has none, and a tool with nowhere to point should offer nothing rather
    /// than guess at the origin.
    /// </returns>
    bool TryGetPointer(out Vector2 position);

    bool IsKeyDown(Key key);

    bool IsButtonDown(MouseButton button);
}
