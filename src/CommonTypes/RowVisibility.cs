namespace VoxelCAD.CommonTypes;

/// <summary>
/// What a project-tree row says about whether the thing it names reaches the
/// viewport and the export. Three states rather than two, because a row has to
/// tell them apart: what is drawn is the <em>effective</em> answer, while the eye
/// offered on the row is about the element's <b>own</b> flag — the only one
/// clicking it can change.
/// </summary>
public enum RowVisibility
{
    Shown,

    /// <summary>Hidden by its own flag: a crossed eye, and the row recedes.</summary>
    Hidden,

    /// <summary>
    /// Shown in its own right, but standing under something hidden. The row
    /// recedes like a hidden one — it reaches nothing either — while its eye
    /// still reads as shown, because that is what its own flag says and what
    /// clicking would change.
    /// </summary>
    HiddenByAncestor
}
