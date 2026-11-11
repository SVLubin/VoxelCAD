using System;

namespace VoxelCAD.CommonTypes;

/// <summary>
/// The modifier keys a synthesized gesture holds down while it runs.
/// </summary>
/// <remarks>
/// They are genuinely held, not merely flagged on the event: the editor reads
/// some modifiers off the event (a Shift-click erases) and others off the live
/// keyboard (<c>Input.IsKeyPressed</c>, which is how the camera decides it is
/// navigating), and a gesture that set only the flags would satisfy half the
/// code and puzzle the other half.
/// </remarks>
[Flags]
public enum InputModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Ctrl = 4,
    Meta = 8
}
