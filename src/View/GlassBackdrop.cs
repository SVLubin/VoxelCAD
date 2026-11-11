namespace VoxelCAD.View;

/// <summary>
/// The pane every glass surface is made of: put one behind a panel's contents
/// and the panel is glass. It carries the shared material and keeps the
/// shader's rounded-corner mask in step with its own size.
/// </summary>
/// <remarks>
/// A class rather than four copies of the same two lines, because the rule it
/// enforces is the canon's: <b>one screen read per surface</b>. Anything drawn
/// inside a surface that already has one of these tints the pane with a flat
/// translucent fill and never adds a second — see <c>docs/panels.md</c>.
/// </remarks>
internal sealed partial class GlassBackdrop : ShapedSurface
{
    public GlassBackdrop()
        : base(GlassPanelMaterial.Shared()) =>
        Name = "Glass";
}
