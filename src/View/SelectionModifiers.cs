using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Which modifier on a click asks for what. One function, asked by the viewport
/// and by the project tree, for the reason <see cref="SelectingTools"/> is one:
/// a modifier meaning one thing in the scene and another in the list is a thing
/// nobody could learn.
/// </summary>
/// <remarks>
/// The command modifier toggles and Shift widens, which is the platform's own
/// convention for a list and the CAD one for a viewport at the same time.
/// <para>
/// <b>The command modifier is Cmd on macOS and Ctrl elsewhere, and on macOS that
/// is not a preference.</b> The engine's macOS window turns a Ctrl+left-click into
/// a right button press before any of this is reached — it reads the control flag
/// in its own <c>mouseDown:</c> and sends <c>MouseButton.Right</c> — so a Ctrl+click
/// simply never arrives here as a left click, whatever anybody binds. Godot's
/// <see cref="InputEventWithModifiers.IsCommandOrControlPressed"/> is the platform's
/// answer and is the whole of it; this deliberately does <em>not</em> also accept
/// plain Ctrl the way <see cref="ShellShortcuts.IsCommand"/> does for chords, where
/// a key is a key and a Windows habit costs nothing.
/// </para>
/// </remarks>
internal static class SelectionModifiers
{
    public static SelectionIntent Resolve(InputEventWithModifiers modifiers) =>
        Resolve(modifiers.IsCommandOrControlPressed(), modifiers.ShiftPressed);

    /// <summary>
    /// With both held the toggle wins. An arbitrary tie-break rather than a
    /// meaning, and stated so that the two surfaces break it the same way.
    /// </summary>
    public static SelectionIntent Resolve(bool command, bool shift)
    {
        if (command)
            return SelectionIntent.Toggle;

        return shift ? SelectionIntent.Extend : SelectionIntent.Replace;
    }
}
