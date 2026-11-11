namespace VoxelCAD.View;

/// <summary>
/// Whether a chrome surface cuts a notch, and how far in. One answer, asked by
/// the panel that will wear it and by the outline drawn where it will land.
/// </summary>
/// <remarks>
/// A fact about where a slot <em>stands</em> — unpinned, with something below it
/// to reveal — and deliberately not about what it is doing this frame. A cut
/// that also asked whether a body was showing would ease 0 to 36 as a panel
/// opened, growing a rectangle and then cutting a Γ into it, which is exactly
/// what <c>docs/panels.md</c> forbids: the cut is what the shape is, not
/// something the opening does to it.
/// </remarks>
internal static class PanelNotch
{
    public static bool Cuts(bool pinned, bool revealsTabs) => !pinned && revealsTabs;

    public static float Reach(bool pinned, bool revealsTabs) =>
        Cuts(pinned, revealsTabs) ? EdgeColumn.NotchReach : 0f;
}
