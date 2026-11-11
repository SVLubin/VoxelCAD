using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Every decision the reference nodes take about how a reference is drawn, worked
/// out without an engine. The nodes then do nothing but write these onto a
/// material and an instance.
/// </summary>
/// <remarks>
/// Split out for the reason <see cref="VoxelMaterialLook"/> is: a
/// <see cref="Godot.StandardMaterial3D"/> cannot be constructed in a test host, so
/// as long as these lived inside the node they could only be verified by taking a
/// photograph.
/// </remarks>
/// <param name="Drawn">
/// Whether there is anything to draw at all. Opacity nought is "keep it and stop
/// drawing it", which is a different sentence from hiding the element — the row
/// stays lit, the handles stay on it, and it comes back by turning one dial.
/// </param>
/// <param name="Unshaded">
/// Whether the scene's lights are ignored. On unless the reference was told to be
/// lit: a photograph already carries its own light and a drawing reads worse under
/// a key light.
/// </param>
/// <param name="Transparency">
/// What Godot's per-instance fade takes, which runs the other way from opacity —
/// nought is solid. Per instance rather than per material precisely so a model's
/// own materials are never touched to fade it.
/// </param>
/// <param name="Nearest">
/// Whether the picture is sampled without smoothing between its pixels. What pixel
/// art needs, and meaningless for a model, which carries whatever its own
/// materials say.
/// </param>
internal readonly record struct ReferenceLook(
    bool Drawn,
    bool Unshaded,
    float Transparency,
    bool Nearest)
{
    /// <summary>
    /// Never. A reference is furniture, on <c>SceneBox</c>'s own terms: it stands
    /// in the scene to be traced over, and a drawing that threw a shadow across the
    /// model being built from it would be scenery pretending to be geometry.
    /// </summary>
    /// <remarks>
    /// A property rather than a field so that nothing can construct a reference
    /// that casts one. Stated here, where it is testable, rather than as a line in
    /// a node nothing can build.
    /// </remarks>
    public bool CastsShadow => false;

    /// <summary>
    /// Always. A plane has no inside, so which way it faces is a fact about where
    /// somebody stood rather than about the picture — and a reference that vanished
    /// when the camera crossed it would be one you had to orbit back to find.
    /// </summary>
    public bool DoubleSided => true;

    public static ReferenceLook From(ReferenceImageSettings settings) => new(
        Drawn: settings.Opacity > 0f,
        Unshaded: !settings.LitByScene,
        Transparency: 1f - settings.Opacity,
        Nearest: !settings.Filtered);

    public static ReferenceLook From(ReferenceModelSettings settings) => new(
        Drawn: settings.Opacity > 0f,
        Unshaded: !settings.LitByScene,
        Transparency: 1f - settings.Opacity,
        // A model wears its own materials, and what they sample with is theirs.
        Nearest: false);
}
