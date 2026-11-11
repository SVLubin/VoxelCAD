namespace VoxelCAD.CommonTypes;

/// <summary>What happened to a scene, as reported by the scene's change stream.</summary>
public enum SceneChangeKind
{
    ElementAdded,

    /// <summary>
    /// The element and its whole subtree are gone. Only the subtree root is
    /// reported — consumers are expected to drop its descendants themselves.
    /// </summary>
    ElementRemoved,

    ElementMoved,
    ElementRenamed,
    TransformChanged,
    ComponentChanged,

    /// <summary>
    /// Cells of an element's voxel model changed. The affected region is not
    /// reported yet — the view remeshes the whole model. See the tech-debt ledger.
    /// </summary>
    VoxelsChanged,

    /// <summary>
    /// A voxel model's mirror-plane slots changed. The cells did not: symmetry
    /// is a brush modifier, so only what previews and what future edits write
    /// follows it.
    /// </summary>
    SymmetryChanged,

    /// <summary>
    /// An element's pivot rule or explicit point changed. Nothing moved: the
    /// pivot is where the handles act, not a term in the transform, so what
    /// follows it is the gizmo and the inspector's own row.
    /// </summary>
    PivotChanged,

    /// <summary>
    /// An element was hidden or shown. Only the element itself is reported: the
    /// flag is its own, and what is nested under it goes with it because a
    /// consumer composes visibility down the tree the way it composes transforms.
    /// </summary>
    VisibilityChanged,

    /// <summary>
    /// A camera's lens changed. Nothing moved — where a camera stands is its
    /// element's transform — so what follows it is the frustum drawn in the
    /// viewport and any picture being taken through it.
    /// </summary>
    CameraChanged,

    /// <summary>
    /// Which camera this scene renders through changed, or there is no longer one.
    /// <see cref="SceneChange.ElementId"/> is the camera now active, or
    /// <see cref="System.Guid.Empty"/> where the scene has none — the one kind
    /// whose element id may name nothing.
    /// </summary>
    ActiveCameraChanged,

    /// <summary>
    /// A reference's file or the way it is drawn changed. Nothing moved — where a
    /// reference stands is its element's transform — so what follows it is the
    /// plane or the model drawn in the viewport and the inspector's own rows.
    /// </summary>
    ReferenceChanged
}
