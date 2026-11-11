namespace VoxelCAD.CommonTypes;

/// <summary>
/// Where one chrome panel stands: which window edge, where in that edge's
/// column, and the two switches a header carries — pinned (present in the
/// column) and folded (showing only its header). See <c>docs/panels.md</c>.
/// </summary>
/// <remarks>
/// A display preference, not document data: it crosses the view seam the way
/// <see cref="ViewCubeCorner"/> does, and is stored on the same terms.
/// <para>
/// <see cref="Height"/> is the body height the user dragged the panel to, in
/// canvas units, and <c>0</c> means "as tall as the content asks" — the answer
/// every panel starts with. It is the panel's own and travels with it through
/// drags and pins; the width deliberately is not here, being the side's (see
/// <see cref="PanelWidths"/>).
/// </para>
/// </remarks>
public readonly record struct PanelPlacement(
    PanelKind Kind,
    PanelSide Side,
    int Order,
    bool Pinned,
    bool Folded,
    double Height = 0.0);
