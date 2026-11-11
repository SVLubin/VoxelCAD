using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Which tools a plain click may change the selection with: the cursor, whose
/// whole job that is, and the three transform tools, where clicking a part to
/// take hold of it is what every editor with handles does.
/// </summary>
/// <remarks>
/// The tools that aim at cells are deliberately not here. A click already means
/// something under them — place, erase, sample, pin, pull — and having it also
/// change which model is being edited is the complaint auto-select has earned
/// everywhere it exists: the surface under the pointer would quietly become the
/// thing the next stroke lands on.
/// <para>
/// Public for one reason and no other, the same one <see cref="ViewCubeGeometry"/>
/// is: both sides of the seam have to give the same answer. The viewport asks it
/// before reporting a pick at all, the presenter asks it before acting on one,
/// and a disagreement between the two would be a click that is reported and then
/// dropped — which looks exactly like selection not working.
/// </para>
/// </remarks>
public static class SelectingTools
{
    public static bool IsSelectingTool(ToolKind tool) =>
        tool == ToolKind.Select || TransformTools.IsTransformTool(tool);
}
