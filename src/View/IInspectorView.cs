using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The inspector panel. It displays what it is given and reports what the user
/// typed; it never rounds, validates or decides which modes exist.
/// </summary>
public interface IInspectorView
{
    event Action<TransformChannel, Vector3> ValueEdited;

    event Action<TransformChannel, FieldMode> ModeEdited;

    /// <summary>
    /// The pivot row was edited: the point as an <b>offset from the middle of the
    /// content</b>, as the row holds it — 0 is the model's own centre. Reported
    /// raw — the half-cell rounding and the conversion to the stored point are
    /// decisions, and they are not made here.
    /// </summary>
    event Action<Vector3> PivotEdited;

    /// <summary>
    /// The pivot row's Center button: place the pivot at the middle of what the
    /// element holds. Reported, never computed — the middle is the presenter's to
    /// know, and it changes nothing when there is nothing to centre on.
    /// </summary>
    event Action PivotCenterRequested;

    /// <summary>
    /// The prefab section's "Make prefab" button: lift the selected element into
    /// the library and leave an instance of it behind.
    /// </summary>
    event Action MakePrefabRequested;

    /// <summary>
    /// The prefab section's "Open prefab" button: edit the prefab an instance
    /// stands for, or the one a read-only element is part of. Offered only on those.
    /// </summary>
    event Action OpenPrefabRequested;

    /// <summary>The prefab section's "Unpack" button. Offered only on an instance.</summary>
    event Action UnpackRequested;

    /// <summary>The colour of the material currently shown was changed.</summary>
    event Action<Color> ColorEdited;

    /// <summary>A ready-made material was chosen — its appearance and its colour, one record.</summary>
    event Action<MaterialPreset> PresetChosen;

    /// <summary>
    /// A tuning row moved. Carries the whole appearance rather than the one value
    /// that changed: a row knows its own number and nothing about the other five,
    /// and the panel is the only thing that has them all.
    /// </summary>
    event Action<MaterialAppearance> AppearanceEdited;

    /// <summary>The Basic/Advanced switch was pressed.</summary>
    event Action<bool> AdvancedChosen;

    /// <summary>The colour's notation was changed.</summary>
    event Action<ColorTextFormat> ColorFormatChosen;

    /// <summary>The preview fold was pressed.</summary>
    event Action<bool> PreviewShownChanged;

    /// <summary>
    /// The material's transparency switch. It turns the whole of the see-through
    /// machinery on and the colour's alpha then says how much; off, the material
    /// is opaque whatever alpha its colour happens to carry.
    /// </summary>
    event Action<bool> TransparencyEdited;

    /// <summary>
    /// A mirror-plane row was edited: the axis, whether the plane is on, and its
    /// <b>offset from the pivot</b> as the row holds it — 0 is a mirror through
    /// the pivot. Reported raw — rounding onto the half-cell lattice and the
    /// conversion to the stored plane are decisions, and they are not made here.
    /// </summary>
    event Action<Vector3.Axis, bool, float> SymmetryEdited;

    /// <summary>
    /// A mirror-plane row's Mirror button: write the image of every cell across
    /// that row's plane, wherever the image lands on an empty cell — the
    /// retroactive half of symmetry, one-shot. Which plane the row shows, and
    /// which cells that reaches, are the presenter's to decide.
    /// </summary>
    event Action<Vector3.Axis> MirrorRequested;

    /// <summary>
    /// A camera's lens was edited. The whole lens rather than the one number that
    /// moved: the field composes it from the lens it was last given, so a spin
    /// box's own quantising cannot round the four values nobody touched.
    /// </summary>
    event Action<CameraLens> CameraLensEdited;

    /// <summary>
    /// The whole settings, as the field composed them. Whole rather than named,
    /// because a spin box quantises to its own step and reading the others back to
    /// change one would round what nobody touched.
    /// </summary>
    event Action<ReferenceImageSettings> ReferenceImageEdited;

    /// <inheritdoc cref="ReferenceImageEdited"/>
    event Action<ReferenceModelSettings> ReferenceModelEdited;

    /// <summary>
    /// Somebody wants to point the selected reference at another file. The panel
    /// has no picker of its own — where a file comes from is the shell's.
    /// </summary>
    event Action ReferencePathChangeRequested;

    /// <summary>
    /// The selected camera should be the one this scene renders through. Only ever
    /// asked to turn on — a picture is taken through one camera, so the way to stop
    /// one rendering is to choose another.
    /// </summary>
    event Action ActiveCameraRequested;

    /// <summary>
    /// A tool button was pressed. Reported, never decided: exclusivity belongs
    /// to the session, and the strip only mirrors what
    /// <see cref="SetActiveTool"/> says came of the request.
    /// </summary>
    event Action<ToolKind> ToolSelected;

    /// <summary>
    /// A colour gesture started: the picker is open, and everything it reports
    /// until <see cref="ColorEditEnded"/> is one continuous edit. Dragging across
    /// a gradient raises <see cref="ColorEdited"/> on every frame, and those are
    /// one thing the user did, not two hundred.
    /// </summary>
    event Action ColorEditBegan;

    event Action ColorEditEnded;

    /// <summary>
    /// Which mode buttons a channel offers. Comes from the model's own rule, so
    /// the panel can never present a mode the model would refuse.
    /// </summary>
    void SetAllowedModes(TransformChannel channel, IReadOnlyList<FieldMode> modes);

    /// <summary>Nothing selected.</summary>
    void Clear();

    /// <param name="component">
    /// What the element holds, which is what the title's glyph is drawn from.
    /// A group and a voxel model differ in the panel only by which sections
    /// turn up for them, and reading a kind off which rows are missing is not
    /// reading.
    /// </param>
    void SetTarget(
        string name,
        ComponentKind component,
        TransformValues values,
        TransformModes modes);

    /// <summary>
    /// The selected element's pivot, already re-expressed as an offset from the
    /// middle of what it holds. Arrives after <see cref="SetTarget"/> for every
    /// element, since a group has a pivot as much as a model does.
    /// </summary>
    void SetPivot(Vector3 point);

    /// <summary>
    /// The selected element's mirror-plane slots, their positions already
    /// re-expressed as offsets from the pivot. Arrives after
    /// <see cref="SetTarget"/> for an element that has them — a voxel model —
    /// and the section stays hidden for any target it never arrives for.
    /// </summary>
    void SetSymmetry(SymmetryValues values);

    /// <summary>
    /// The selected camera's lens, and whether it is the one the scene renders
    /// through. Arrives after <see cref="SetTarget"/> for a camera element, and
    /// the section stays hidden for any target it never arrives for — the same
    /// bargain <see cref="SetSymmetry"/> makes.
    /// </summary>
    void SetCamera(CameraLens lens, bool active);

    /// <summary>
    /// The selected reference's file and how it is drawn, with whether that file is
    /// actually there. Arrives after <see cref="SetTarget"/> for a reference
    /// element, and the section stays hidden for any target it never arrives for —
    /// the same bargain <see cref="SetSymmetry"/> makes.
    /// </summary>
    void SetReferenceImage(ReferenceImageSettings settings, bool found);

    /// <inheritdoc cref="SetReferenceImage"/>
    void SetReferenceModel(ReferenceModelSettings settings, bool found);

    /// <summary>
    /// Where the selected element stands with respect to the library: the name of
    /// the prefab it is an instance of, or null for an ordinary element, which is
    /// offered the chance to become one. Arrives after <see cref="SetTarget"/> for
    /// an element that may be either, and the section stays hidden for any target
    /// it never arrives for — prefab contents among them, which are neither.
    /// </summary>
    void SetPrefab(string? sourceName);

    /// <summary>
    /// The selected element is part of a prefab instance, so it cannot be changed
    /// here: the panel says which prefab, offers to open it, and shows the
    /// transform and the pivot without taking input. Arrives after
    /// <see cref="SetTarget"/> in place of <see cref="SetPrefab"/>, and the
    /// next <see cref="SetTarget"/> makes the rows editable again.
    /// </summary>
    void SetPrefabContent(string prefabName);

    /// <summary>
    /// The tool strip for the current target, with the tool the editor is in.
    /// Arrives after <see cref="SetTarget"/> for an element the tools act on —
    /// a voxel model — and the section stays hidden for any target it never
    /// arrives for, the same bargain <see cref="SetSymmetry"/> makes.
    /// </summary>
    void SetTools(ToolKind activeTool);

    /// <summary>Keeps the strip in step when the tool changes while it is shown.</summary>
    void SetActiveTool(ToolKind tool);

    /// <summary>
    /// A scene is selected. It is a hierarchy container like any other and
    /// inspects like one, but it has no transform to show — it is the frame the
    /// others are placed in — so what it does have is its build region.
    /// </summary>
    void SetSceneTarget(string name, Vector3I size);

    /// <summary>
    /// A material is selected. Materials belong to the project rather than to any
    /// scene, and are inspected from the same panel as everything else.
    /// </summary>
    void SetMaterialTarget(
        string name,
        Color color,
        MaterialAppearance appearance,
        MaterialPreset? preset);

    /// <summary>
    /// Fills the two lists. Their words are the presenter's: the model owns what a
    /// preset is and has no business owning what it is called, the same division
    /// that keeps history labels out of <c>ActionKind</c>.
    /// </summary>
    void SetPresetOptions(
        IReadOnlyList<MaterialPresetOption> presets,
        IReadOnlyList<MaterialFinishOption> finishes);

    /// <summary>
    /// Whether the tuning rows are shown. An editor preference rather than
    /// document data: it is how somebody likes to work, not a fact about a model.
    /// </summary>
    void SetMaterialDepth(bool advanced);

    /// <summary>How the colour is written in its field. A preference on the same terms.</summary>
    void SetColorFormat(ColorTextFormat format);

    /// <summary>
    /// Whether the turnable block is shown. Folded away by default and remembered
    /// for the session: it is a live viewport, so it draws every frame it is up.
    /// </summary>
    void SetPreviewShown(bool shown);
}
