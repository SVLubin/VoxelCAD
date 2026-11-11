namespace VoxelCAD.View;

/// <summary>
/// The outline drawn in the hole a carried panel leaves — the shape it will wear
/// when it lands, in the accent. If the landing is a Γ, this is a Γ.
/// </summary>
/// <remarks>
/// Not a <see cref="Godot.Panel"/> with a stylebox, for the same reason a panel's
/// own surface is not one: a stylebox draws rectangles, and no per-corner radius
/// rounds a concave corner at all. A rectangular outline hung on a tab's corner
/// also lay over the very tabs the notch exists to reveal, washing them and
/// running its border across them — the claim in <see cref="EdgeColumn"/>'s
/// constructor that the hole is empty of panels was false until the notch made
/// it true.
/// </remarks>
internal sealed partial class GhostOutline : ShapedSurface
{
    public GhostOutline()
        : base(GhostOutlineMaterial.Shared())
    {
        Name = "DropGhost";
        Visible = false;
    }
}
