namespace VoxelCAD.CommonTypes;

/// <summary>
/// Which tool the viewport is in. Exclusive: exactly one is active, and what a
/// plain left click does in the scene follows from it.
/// </summary>
/// <remarks>
/// <see cref="Select"/> is the plain cursor — the tool that edits nothing — and it
/// is where the editor opens: a fresh window points at things rather than holding a
/// brush over them. It aims at whole elements rather than at cells or faces, and
/// what a click does is say which element the pointer is over. Being a tool rather
/// than the absence of one is what makes leaving a mode the same gesture as
/// entering one: exclusivity, the tool strip and the keyboard all work on it
/// unchanged. The three
/// transform tools map one-to-one onto
/// <see cref="TransformChannel"/>, which is what lets one gizmo serve all three.
/// <see cref="Eyedropper"/> is the brush's counterpart on the same cells: it
/// reads the clicked voxel's material instead of writing one.
/// <see cref="Extrude"/> aims at a face rather than a cell: it takes the whole
/// connected run of surface the click landed on and pushes it out by a layer, or
/// pulls it in. <see cref="Fill"/> aims at a cell and recolours the connected
/// volume of its material — the brush's counterpart for a region rather than a
/// cell.
/// </remarks>
public enum ToolKind
{
    /// <summary>
    /// First, so that zero — what an unset field reads as — is the tool that edits
    /// nothing. Which tool the editor <em>starts</em> in is still stated outright in
    /// <c>EditorSession</c> rather than left to this order; they agree, and neither
    /// depends on the other.
    /// </summary>
    Select,

    Paint,
    Move,
    Rotate,
    Scale,
    Eyedropper,
    Extrude,
    Fill
}
