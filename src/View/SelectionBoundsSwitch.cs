using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// One switch, pulled by several parties. Whether the selected element's bounds
/// are drawn is not any one caller's decision — a transform tool wants the box, a
/// selection made in the project tree wants it, and the brush wants it out of the
/// way — so each raises its own <see cref="SelectionBoundsReason"/> and the box is
/// on while any of them stands.
/// </summary>
/// <remarks>
/// Kept out of the viewport node on purpose, like every other decision the panels
/// make: what is left in the node is attaching a mesh. The rule the tool half
/// encodes is worth stating plainly — <b>every tool change lowers the tool's
/// reason, and some tools raise it again in the same breath</b>, which is why
/// nothing has to remember what the previous tool wanted.
/// </remarks>
internal sealed class SelectionBoundsSwitch
{
    private SelectionBoundsReason _reasons;

    /// <summary>Whether anything is asking for the box.</summary>
    public bool IsOn => _reasons != SelectionBoundsReason.None;

    /// <summary>Who is asking, for a caller that wants to know why rather than whether.</summary>
    public SelectionBoundsReason Reasons => _reasons;

    /// <summary>
    /// Raises or lowers one reason, and answers whether the box itself changed
    /// state — not whether the reason did. A second reason going up under a box
    /// that is already drawn costs no rebuild, and that is the whole point of the
    /// answer.
    /// </summary>
    public bool Set(SelectionBoundsReason reason, bool raised)
    {
        SelectionBoundsReason updated = raised ? _reasons | reason : _reasons & ~reason;

        if (updated == _reasons)
            return false;

        bool wasOn = IsOn;
        _reasons = updated;

        return IsOn != wasOn;
    }

    /// <summary>
    /// Which tools ask for the box: the ones aimed at whole elements. The three
    /// transform tools, because a tool that grows handles should grow the box with
    /// them — and the cursor, whose entire job is saying which element is which,
    /// and which would otherwise be the one tool that changes the selection while
    /// showing nothing for it. The cell tools do not: under the brush, the
    /// eyedropper, the fill or the extruder the pointer is aimed at a face, and a
    /// box around the whole model says nothing about where the next click lands.
    /// </summary>
    /// <remarks>
    /// No longer the same answer the gizmo places handles by, and the difference is
    /// the point: <see cref="TransformTools"/> answers "does this tool get handles",
    /// this answers "does this tool get the box". They agreed until the cursor
    /// arrived — a tool that wants the box and must never grow a handle.
    /// </remarks>
    public static bool RaisedByTool(ToolKind tool) =>
        TransformTools.IsTransformTool(tool) || tool == ToolKind.Select;
}
