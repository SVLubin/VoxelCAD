using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Godot;

namespace VoxelCAD.Presenter.Tests;

/// <summary>Remembers what the inspector was told, so a test can read it back.</summary>
internal sealed class FakeInspectorView : IInspectorView
{
    public event Action<TransformChannel, Vector3>? ValueEdited;

    public event Action<TransformChannel, FieldMode>? ModeEdited;

    public event Action<Color>? ColorEdited;


    public event Action<bool>? TransparencyEdited;

    public event Action<MaterialPreset>? PresetChosen;

    public event Action<MaterialAppearance>? AppearanceEdited;

    public event Action<bool>? AdvancedChosen;

    public event Action<ColorTextFormat>? ColorFormatChosen;

    public event Action<bool>? PreviewShownChanged;

    public event Action<Vector3.Axis, bool, float>? SymmetryEdited;

    public event Action<Vector3.Axis>? MirrorRequested;

    public event Action<CameraLens>? CameraLensEdited;

    public event Action<ReferenceImageSettings>? ReferenceImageEdited;

    public event Action<ReferenceModelSettings>? ReferenceModelEdited;

    public event Action? ReferencePathChangeRequested;

    public event Action? ActiveCameraRequested;

    public event Action<Vector3>? PivotEdited;

    public event Action? PivotCenterRequested;

    public event Action<ToolKind>? ToolSelected;

    public event Action? ColorEditBegan;

    public event Action? ColorEditEnded;

    public event Action? MakePrefabRequested;

    public event Action? OpenPrefabRequested;

    public event Action? UnpackRequested;

    public int ClearCount { get; private set; }

    /// <summary>
    /// Whether the prefab section is shown at all. Hidden by every new target, and
    /// brought back only by <see cref="SetPrefab"/> arriving for this one.
    /// </summary>
    public bool PrefabShown { get; private set; }

    /// <summary>
    /// The prefab an instance stands for; null both when the section is hidden and
    /// when it is shown for an ordinary element — <see cref="PrefabShown"/> is what
    /// tells those two apart.
    /// </summary>
    public string? PrefabSource { get; private set; }

    /// <summary>
    /// The prefab a read-only element is part of, when the panel said so; null for
    /// anything that may be edited where it stands.
    /// </summary>
    public string? PrefabContentOf { get; private set; }

    /// <summary>Whatever the panel was last shown, whichever kind it was.</summary>
    public string? TargetName { get; private set; }

    /// <summary>What the last element shown held — what its title's glyph is drawn from.</summary>
    public ComponentKind? TargetComponent { get; private set; }

    /// <summary>Null unless a scene is what the panel was last shown.</summary>
    public Vector3I? SceneSize { get; private set; }

    /// <summary>Null unless a material is what the panel was last shown.</summary>
    public Color? MaterialColor { get; private set; }

    public MaterialAppearance? MaterialAppearance { get; private set; }

    /// <summary>Which preset the panel was told the material is, or null for Custom.</summary>
    public MaterialPreset? MaterialPreset { get; private set; }

    public bool MaterialAdvanced { get; private set; }

    public ColorTextFormat ColorFormat { get; private set; }

    public bool PreviewShown { get; private set; }

    public IReadOnlyList<MaterialPresetOption> PresetOptions { get; private set; } =
        Array.Empty<MaterialPresetOption>();

    public IReadOnlyList<MaterialFinishOption> FinishOptions { get; private set; } =
        Array.Empty<MaterialFinishOption>();

    /// <summary>
    /// Null while the symmetry section is hidden — every new target hides it,
    /// and only a voxel element's slots arriving brings it back.
    /// </summary>
    public SymmetryValues? Symmetry { get; private set; }

    /// <summary>
    /// Null while the tool strip is hidden; else the tool the strip shows as
    /// active. Same bargain as <see cref="Symmetry"/>.
    /// </summary>
    public ToolKind? Tools { get; private set; }

    /// <summary>
    /// The pivot the row was last shown, in the element's own cells. Unlike
    /// symmetry it arrives for every element, group included, so it is cleared
    /// only by a target that is not an element at all.
    /// </summary>
    public Vector3? Pivot { get; private set; }

    public void SetAllowedModes(TransformChannel channel, IReadOnlyList<FieldMode> modes)
    {
    }

    public void Clear()
    {
        ClearCount++;
        TargetName = null;
        Pivot = null;

        ForgetSections();
    }

    public void SetTarget(
        string name,
        ComponentKind component,
        TransformValues values,
        TransformModes modes)
    {
        TargetName = name;
        TargetComponent = component;

        ForgetSections();
    }

    public void SetSymmetry(SymmetryValues values) => Symmetry = values;

    /// <summary>
    /// The lens the camera section was last shown, or null while the section is
    /// hidden — which is every target that is not a camera, since the real panel
    /// hides it on every new target and shows it only when this arrives.
    /// </summary>
    public CameraLens? Camera { get; private set; }

    /// <summary>Whether the shown camera was marked as the one that renders.</summary>
    public bool CameraRenders { get; private set; }

    public void SetCamera(CameraLens lens, bool active)
    {
        Camera = lens;
        CameraRenders = active;
    }

    /// <summary>
    /// The reference last shown, and whether the panel was told its file is there.
    /// Null where the section never arrived, which is what "this target is not a
    /// reference" looks like from the panel's side.
    /// </summary>
    public (ReferenceImageSettings Settings, bool Found)? ReferenceImage { get; private set; }

    public (ReferenceModelSettings Settings, bool Found)? ReferenceModel { get; private set; }

    public void SetReferenceImage(ReferenceImageSettings settings, bool found) =>
        ReferenceImage = (settings, found);

    public void SetReferenceModel(ReferenceModelSettings settings, bool found) =>
        ReferenceModel = (settings, found);

    /// <summary>
    /// Everything a section owns, forgotten. The real panel hides every section on
    /// each new target, so four copies of this list is four chances for the fake to
    /// go on remembering one the panel would have put away — which is exactly what
    /// makes "this target has no reference" untestable.
    /// </summary>
    private void ForgetSections()
    {
        SceneSize = null;
        MaterialColor = null;
        MaterialAppearance = null;
        MaterialPreset = null;
        Symmetry = null;
        Camera = null;
        CameraRenders = false;
        ReferenceImage = null;
        ReferenceModel = null;
        Tools = null;
        PrefabShown = false;
        PrefabSource = null;
        PrefabContentOf = null;
    }

    public void SetPivot(Vector3 point) => Pivot = point;

    public void SetPrefab(string? sourceName)
    {
        PrefabShown = true;
        PrefabSource = sourceName;
    }

    public void SetPrefabContent(string prefabName) => PrefabContentOf = prefabName;

    public void SetTools(ToolKind activeTool) => Tools = activeTool;

    /// <summary>Observable only while the strip is shown, like the real strip.</summary>
    public void SetActiveTool(ToolKind tool)
    {
        if (Tools != null)
            Tools = tool;
    }

    public void SetSceneTarget(string name, Vector3I size)
    {
        TargetName = name;
        Pivot = null;

        ForgetSections();

        SceneSize = size;
    }

    public void SetPresetOptions(
        IReadOnlyList<MaterialPresetOption> presets,
        IReadOnlyList<MaterialFinishOption> finishes)
    {
        PresetOptions = presets;
        FinishOptions = finishes;
    }

    public void SetMaterialDepth(bool advanced) => MaterialAdvanced = advanced;

    public void ChoosePreset(MaterialPreset preset) => PresetChosen?.Invoke(preset);

    public void EditAppearance(MaterialAppearance appearance) => AppearanceEdited?.Invoke(appearance);

    public void ChooseAdvanced(bool advanced) => AdvancedChosen?.Invoke(advanced);

    public void ChooseColorFormat(ColorTextFormat format) => ColorFormatChosen?.Invoke(format);

    public void SetColorFormat(ColorTextFormat format) => ColorFormat = format;

    public void SetPreviewShown(bool shown) => PreviewShown = shown;

    public void ShowPreview(bool shown) => PreviewShownChanged?.Invoke(shown);

    public void SetMaterialTarget(
        string name,
        Color color,
        MaterialAppearance appearance,
        MaterialPreset? preset)
    {
        TargetName = name;
        Pivot = null;

        ForgetSections();

        MaterialColor = color;
        MaterialAppearance = appearance;
        MaterialPreset = preset;
    }

    public void RaiseMirrorRequested(Vector3.Axis axis) => MirrorRequested?.Invoke(axis);


    public void RaiseValueEdited(TransformChannel channel, Vector3 value) =>
        ValueEdited?.Invoke(channel, value);

    public void RaiseModeEdited(TransformChannel channel, FieldMode mode) =>
        ModeEdited?.Invoke(channel, mode);

    public void RaiseColorEdited(Color color) => ColorEdited?.Invoke(color);

    public void RaiseTransparencyEdited(bool transparent) =>
        TransparencyEdited?.Invoke(transparent);

    public void RaiseToolSelected(ToolKind tool) => ToolSelected?.Invoke(tool);

    /// <summary>A mirror-plane row was edited, position in cells as the row holds it.</summary>
    public void RaiseSymmetryEdited(Vector3.Axis axis, bool enabled, float position) =>
        SymmetryEdited?.Invoke(axis, enabled, position);

    public void RaiseCameraLensEdited(CameraLens lens) => CameraLensEdited?.Invoke(lens);

    public void RaiseReferenceImageEdited(ReferenceImageSettings settings) =>
        ReferenceImageEdited?.Invoke(settings);

    public void RaiseReferenceModelEdited(ReferenceModelSettings settings) =>
        ReferenceModelEdited?.Invoke(settings);

    public void RaiseActiveCameraRequested() => ActiveCameraRequested?.Invoke();

    /// <summary>The pivot row was edited, in cells as the row holds it.</summary>
    public void RaisePivotEdited(Vector3 point) => PivotEdited?.Invoke(point);

    /// <summary>The Center button.</summary>
    public void RaisePivotCenterRequested() => PivotCenterRequested?.Invoke();

    /// <summary>One picker session: opened, dragged across, closed.</summary>
    public void RaiseColorGesture(params Color[] colors)
    {
        ColorEditBegan?.Invoke();

        foreach (Color color in colors)
            ColorEdited?.Invoke(color);

        ColorEditEnded?.Invoke();
    }

    public void RaiseColorEditBegan() => ColorEditBegan?.Invoke();

    public void RaiseColorEditEnded() => ColorEditEnded?.Invoke();

    /// <summary>The prefab section's three buttons.</summary>
    public void RaiseMakePrefabRequested() => MakePrefabRequested?.Invoke();

    public void RaiseOpenPrefabRequested() => OpenPrefabRequested?.Invoke();

    public void RaiseUnpackRequested() => UnpackRequested?.Invoke();
}
