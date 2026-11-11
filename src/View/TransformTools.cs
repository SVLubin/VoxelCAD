using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Which tools get transform handles — the three with a transform channel. One
/// answer, and the gizmo places handles for exactly these.
/// </summary>
/// <remarks>
/// Named as the tools that qualify rather than the tools that do not, and that
/// is the lesson of a real bug, not taste: the gizmo used to refuse only the
/// brush and the eyedropper by name, so the extruder — added later — fell
/// through the gap. It then placed itself invisibly under them, drew no
/// parts, and its <b>mathematical</b> hit test went on answering: a click near
/// the pivot's axes was claimed by a handle nobody could see, and the cell tool
/// under the pointer never heard the click. A new tool must be a no to this
/// question by default.
/// </remarks>
internal static class TransformTools
{
    public static bool IsTransformTool(ToolKind tool) =>
        tool is ToolKind.Move or ToolKind.Rotate or ToolKind.Scale;
}
