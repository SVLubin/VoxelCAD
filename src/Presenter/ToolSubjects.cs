using System;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;

namespace VoxelCAD.Presenter;

/// <summary>
/// What each tool needs to have anything to act on, and what the active tool
/// resolves to when the selection does not offer it.
/// </summary>
/// <remarks>
/// One answer, asked by everything that can put the editor into a tool: the
/// fallback that watches the selection, and the panel keys that decide whether a
/// tool may be asked for at all. Two copies could come to disagree about what a
/// tool needs, and the disagreement would read as the trap this exists to close —
/// a tool that is active over nothing, where the viewport proposes no preview,
/// draws no handles, and answers no click, with nothing anywhere saying why.
/// <para>
/// Named as what each tool <b>qualifies</b> for rather than as what is refused,
/// and the switch is exhaustive on purpose: a tool added tomorrow has no arm here
/// and fails <c>ToolSubjectsTests</c> until somebody decides what it takes hold
/// of. The same lesson <c>TransformTools</c> was written for.
/// </para>
/// </remarks>
internal static class ToolSubjects
{
    /// <summary>Whether the tool has anything to do with that subject.</summary>
    /// <remarks>
    /// The tools that select are a yes to everything, and that is the rule rather
    /// than a coincidence of two of them: a tool whose click picks what is under it
    /// is never over nothing, because the click is what finds the something. Which
    /// is why the fallback and <c>SelectingTools</c> have to be read together —
    /// widening one narrows the other.
    /// </remarks>
    public static bool CanAct(ToolKind tool, ToolSubject subject) =>
        tool switch
        {
            // The cursor takes hold of nothing, which is exactly what makes it the
            // way out of every other tool: it is the one that still answers a click
            // when there is nothing selected at all.
            ToolKind.Select => true,

            // The handles need an element — but a click that misses them picks one,
            // so reaching for Move before choosing a part is a way of working
            // rather than a dead end.
            ToolKind.Move or ToolKind.Rotate or ToolKind.Scale => true,

            // The cell tools aim at voxels, and their click is spent on the cells
            // rather than on finding an element, so a group — which has a transform
            // but no cells — really is nothing for them to act on.
            ToolKind.Paint or ToolKind.Eyedropper or ToolKind.Extrude or ToolKind.Fill =>
                subject == ToolSubject.VoxelModel,

            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null)
        };

    /// <summary>
    /// The tool the editor should be in: the one asked for while it has a subject,
    /// and the cursor the moment it does not.
    /// </summary>
    public static ToolKind Resolve(ToolKind tool, ToolSubject subject) =>
        CanAct(tool, subject) ? tool : ToolKind.Select;

    /// <summary>
    /// What the selection currently offers. An element the scene no longer has
    /// counts as nothing, which is the case a deletion leaves behind: the selection
    /// goes on naming an id, and nothing about the id says it is gone.
    /// </summary>
    public static ToolSubject FindSubject(IScene scene, SelectionTarget selection)
    {
        // Prefab-instance contents are read-only wherever they appear: they are
        // selectable and inspectable rows, and every tool that would write to one
        // is refused by the model. So there is nothing here for a tool to act on,
        // and the fallback puts the editor in the cursor rather than leaving a
        // brush over cells whose every click would throw.
        if (selection.Kind != SelectionKind.Element
            || !scene.TryGetElement(selection.Id, out IHierarchyElement element)
            || element.PrefabInstanceId != null)
        {
            return ToolSubject.Nothing;
        }

        return element.Component is IVoxelModel ? ToolSubject.VoxelModel : ToolSubject.Element;
    }
}
