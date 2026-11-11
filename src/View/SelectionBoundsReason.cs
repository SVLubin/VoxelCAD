using System;

namespace VoxelCAD.View;

/// <summary>
/// Why the selected element's bounds are being drawn. Several unrelated parties
/// ask for the same box, so each holds its own reason and the box is on while any
/// of them is raised — rather than each of them writing a single boolean and the
/// last one to speak winning.
/// </summary>
[Flags]
internal enum SelectionBoundsReason
{
    None = 0,

    /// <summary>
    /// A tool that acts on the element as a whole is active. Raised and lowered by
    /// the tool change itself, which is what makes "every tool switch turns the box
    /// off, and some tools turn it straight back on" one rule instead of six.
    /// </summary>
    TransformTool = 1 << 0,

    /// <summary>
    /// The viewport is not the panel holding the keyboard, so the selection was
    /// made somewhere else — the project tree — and the box is the only thing
    /// saying which element that row stands for.
    /// </summary>
    ViewportInactive = 1 << 1
}
