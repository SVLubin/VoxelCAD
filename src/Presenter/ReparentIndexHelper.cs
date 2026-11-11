namespace VoxelCAD.Presenter;

/// <summary>
/// Turns "where the row was dropped, in the tree as it looks now" into "where it
/// lands among its new siblings once it has left the place it was in".
/// </summary>
/// <remarks>
/// The two differ by exactly one case, and only that case: an element moving
/// <b>further down inside its own parent</b> leaves a gap behind it as it goes, so
/// every row below shifts up one and the place it was dropped at has moved with
/// them. Dropping it higher, or into another parent, takes nothing out from under
/// the target.
/// <para>
/// Without this, dragging the first of three rows to the bottom asks for index 3
/// of a list that is two long by the time it is asked — which the model now clamps
/// to the end rather than throwing, so the bug it would have left is the quieter
/// kind: the row lands one place from where it was dropped.
/// </para>
/// </remarks>
internal static class ReparentIndexHelper
{
    /// <param name="dropIndex">Where it was dropped, counted before anything moved.</param>
    /// <param name="sameParent">Whether it is already a child of the row it was dropped into.</param>
    /// <param name="currentIndex">
    /// Its present place among those siblings. Ignored unless
    /// <paramref name="sameParent"/>.
    /// </param>
    public static int Calculate(int dropIndex, bool sameParent, int currentIndex) =>
        sameParent && currentIndex < dropIndex ? dropIndex - 1 : dropIndex;
}
