using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <inheritdoc cref="IInspectorView"/>
internal sealed partial class InspectorView : VBoxContainer, IInspectorView
{
    /// <summary>
    /// The kind's glyph beside the name, from the very function the project
    /// tree's rows use — so the picture that named a thing in the tree is the
    /// picture at the top of the panel that edits it, and the two cannot drift.
    /// </summary>
    /// <remarks>
    /// It was a bare name, which for a scene or a material is answered by the
    /// section under it and for an element is not answered at all: a group and a
    /// voxel model differ only by which sections happen to appear, and reading
    /// what a thing <em>is</em> off which rows are missing is not reading.
    /// </remarks>
    private readonly TextureRect _kind = new()
    {
        StretchMode = TextureRect.StretchModeEnum.KeepCentered,
        MouseFilter = MouseFilterEnum.Ignore
    };

    private readonly Label _name = new();
    private readonly Dictionary<TransformChannel, Vector3Field> _fieldsByChannelDictionary = new();
    private readonly InspectorSection _transform;
    private readonly InspectorSection _tools;
    private readonly InspectorSection _symmetry;
    private readonly InspectorSection _camera;
    private readonly InspectorSection _reference;
    private readonly InspectorSection _prefab;
    private readonly InspectorSection _scene;
    private readonly InspectorSection _material;
    private readonly ToolStrip _toolStrip = new();
    private readonly Label _buildRegion = new();
    private readonly ColorField _color = new("Colour");
    private readonly SegmentedRow _depth = new();
    private readonly SliderField _roughness = new(
        "MaterialRoughness", "Roughness", 0f, 1f, 2,
        "Zero is a mirror, one is chalk.");
    private readonly SliderField _metallic = new(
        "MaterialMetallic", "Metallic", 0f, 1f, 2,
        "One tints the reflection instead of the diffuse. Full metal has no diffuse "
        + "term at all, so a face turned from every light goes black — the metal "
        + "presets stop short of it for that reason.");
    private readonly SliderField _emission = new(
        "MaterialEmission", "Emission", 0f, 1f, 2,
        "Self-lit strength. Still shaded: the scene's light goes on reaching it.");
    private readonly SliderField _scale = new(
        "MaterialFinishScale", "Scale",
        MaterialAppearanceLimits.MinimumScale, MaterialAppearanceLimits.MaximumScale, 2,
        "How many voxels one unit of the facture spans — a brick course, a board, a\n"
        + "panel, a mosaic tile. The same number means the same size on any finish.");
    private readonly SliderField _relief = new(
        "MaterialFinishRelief", "Relief", 0f, MaterialAppearanceLimits.MaximumRelief, 2,
        "How much of the facture's own relief to apply. Zero is flat.");
    private readonly PresetField _preset = new("Preset");
    private readonly MaterialPreviewField _preview;
    private readonly FinishField _finish = new("Finish");
    private readonly CheckBox _transparent = new();
    private readonly SymmetryField _symmetryField = new();
    private readonly CameraField _cameraField = new();
    private readonly ReferenceField _referenceField = new();
    private readonly PivotField _pivotField = new("Pivot");
    private readonly Label _prefabSource = new();
    private readonly Button _makePrefab;
    private readonly Button _openPrefab;
    private readonly Button _unpack;

    public InspectorView(MotionTiming timing)
    {
        _preview = new MaterialPreviewField();

        // Not field initializers: a group folds at the editor's own pace like
        // every other transition, and lands at once when they are off.
        _transform = new InspectorSection("Transform", timing);
        _tools = new InspectorSection("Tools", timing);
        _symmetry = new InspectorSection("Symmetry", timing);
        _camera = new InspectorSection("Camera", timing);
        _reference = new InspectorSection("Reference", timing);
        _prefab = new InspectorSection("Prefab", timing);
        _scene = new InspectorSection("Scene", timing);
        _material = new InspectorSection("Material", timing);

        _makePrefab = Ui.Button("Make prefab", () => MakePrefabRequested?.Invoke())
            .Named("MakePrefab");
        _openPrefab = Ui.Button("Open prefab", () => OpenPrefabRequested?.Invoke())
            .Named("OpenPrefab");
        _unpack = Ui.Button("Unpack", () => UnpackRequested?.Invoke()).Named("UnpackPrefab");

        _name.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        AddChild(Ui.HBox(_kind, _name).Named("InspectorTitle"));
        AddChild(_transform);
        AddChild(_tools);
        AddChild(_symmetry);
        AddChild(_camera);
        AddChild(_reference);
        AddChild(_prefab);
        AddChild(_scene);
        AddChild(_material);

        _makePrefab.TooltipText =
            "Move this element into the project's prefab library and leave an instance "
            + "of it here. Nothing appears to move.";
        _unpack.TooltipText =
            "Cut this instance's link to its source. The contents become ordinary "
            + "elements; the library entry stays.";

        // Wraps, because a read-only element's line is a sentence rather than a
        // name, and a panel never grows to fit a label.
        _prefabSource.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _prefab.AddRow(_prefabSource);
        _prefab.AddRow(Ui.HBox(_makePrefab, _openPrefab, _unpack));

        AddField(TransformChannel.Position, "Position");
        AddField(TransformChannel.Rotation, "Rotation");
        AddField(TransformChannel.Scale, "Scale");

        // In the transform section rather than beside the tools: it is the point
        // the Position row names, which is the same kind of fact as where the
        // element is — and unlike the tool strip it is there for a group too.
        _pivotField.PivotEdited += point => PivotEdited?.Invoke(point);
        _pivotField.CenterRequested += () => PivotCenterRequested?.Invoke();
        _pivotField.InteractionBegan += () => InteractionBegan?.Invoke();
        _pivotField.InteractionEnded += () => InteractionEnded?.Invoke();
        _transform.AddRow(_pivotField);

        _toolStrip.ToolSelected += tool => ToolSelected?.Invoke(tool);
        _tools.AddRow(_toolStrip);

        _symmetryField.PlaneEdited +=
            (axis, enabled, position) => SymmetryEdited?.Invoke(axis, enabled, position);
        _symmetryField.MirrorRequested += axis => MirrorRequested?.Invoke(axis);
        _symmetryField.InteractionBegan += () => InteractionBegan?.Invoke();
        _symmetryField.InteractionEnded += () => InteractionEnded?.Invoke();
        _symmetry.AddRow(_symmetryField);

        _cameraField.LensEdited += lens => CameraLensEdited?.Invoke(lens);
        _cameraField.ActiveRequested += () => ActiveCameraRequested?.Invoke();
        _cameraField.InteractionBegan += () => InteractionBegan?.Invoke();
        _cameraField.InteractionEnded += () => InteractionEnded?.Invoke();
        _camera.AddRow(_cameraField);

        _referenceField.ImageEdited += settings => ReferenceImageEdited?.Invoke(settings);
        _referenceField.ModelEdited += settings => ReferenceModelEdited?.Invoke(settings);
        _referenceField.PathChangeRequested += () => ReferencePathChangeRequested?.Invoke();
        _referenceField.InteractionBegan += () => InteractionBegan?.Invoke();
        _referenceField.InteractionEnded += () => InteractionEnded?.Invoke();
        _reference.AddRow(_referenceField);

        _scene.AddRow(Ui.HBox(Ui.Label("Build region").Expand(vertical: false), _buildRegion));

        // Basic first and Advanced behind a switch, because the six numbers are
        // for tuning a material that already nearly is what it should be, and the
        // list above is how it gets there.
        _depth.SetSegments(new[]
        {
            new SegmentSpec("MaterialBasic", "Basic", "The preset, the colour, and nothing else."),
            new SegmentSpec("MaterialAdvanced", "Advanced", "Every value the preset writes, one at a time.")
        });
        _depth.Chosen += index => AdvancedChosen?.Invoke(index == 1);
        _material.AddRow(_depth);

        _color.ColorEdited += color => ColorEdited?.Invoke(color);
        _color.FormatChosen += format => ColorFormatChosen?.Invoke(format);
        _color.InteractionBegan += OnColorInteractionBegan;
        _color.InteractionEnded += OnColorInteractionEnded;

        _preset.Chosen += preset => PresetChosen?.Invoke(preset);
        _preview.ShownChanged += shown => PreviewShownChanged?.Invoke(shown);

        _material.AddRow(_preview);
        _material.AddRow(_preset);
        _material.AddRow(_color);

        // A mouse target like the colour button: a control that consumes no
        // arrows must not be able to hold focus, or it strands the keyboard.
        _transparent.Text = "Transparent";
        _transparent.TooltipText = "Let this material be seen through. The colour's alpha "
            + "then says how much; off, the material is opaque whatever alpha it carries.";
        _transparent.Named("MaterialTransparent").MouseOnly();
        _transparent.Toggled += pressed => TransparencyEdited?.Invoke(pressed);
        _material.AddRow(_transparent);

        _finish.Chosen += finish => AppearanceEdited?.Invoke(
            _appearance with { Finish = finish });

        AddAdvancedRow(_roughness, value => _appearance with { Roughness = value });
        AddAdvancedRow(_metallic, value => _appearance with { Metallic = value });
        AddAdvancedRow(_emission, value => _appearance with { Emission = value });
        _advancedRowsList.Add(_finish);
        _material.AddRow(_finish);
        AddAdvancedRow(_scale, value => _appearance with { FinishScale = value });
        AddAdvancedRow(_relief, value => _appearance with { FinishRelief = value });

        Clear();
    }

    /// <summary>
    /// The rows only the advanced depth shows. Hidden rather than rebuilt, so a
    /// slider keeps its place and the section's height settles once.
    /// </summary>
    private readonly List<Control> _advancedRowsList = new();

    /// <summary>
    /// The appearance the panel is showing, kept so a row that edits one value
    /// can report the whole struct — a slider knows its own number and nothing
    /// about the other five.
    /// </summary>
    private MaterialAppearance _appearance = MaterialAppearance.Default;

    public event Action<TransformChannel, Vector3>? ValueEdited;

    /// <summary>A ready-made material was chosen: its appearance and its colour.</summary>
    public event Action<MaterialPreset>? PresetChosen;

    /// <summary>One of the tuning rows moved. Carries the whole appearance, not the delta.</summary>
    public event Action<MaterialAppearance>? AppearanceEdited;

    /// <summary>The Basic/Advanced switch. An editor preference, not document data.</summary>
    public event Action<bool>? AdvancedChosen;

    /// <summary>How the colour is written in its field. An editor preference too.</summary>
    public event Action<ColorTextFormat>? ColorFormatChosen;

    /// <summary>The preview fold was pressed.</summary>
    public event Action<bool>? PreviewShownChanged;

    public event Action<TransformChannel, FieldMode>? ModeEdited;

    public event Action? MakePrefabRequested;

    public event Action? OpenPrefabRequested;

    public event Action? UnpackRequested;

    public event Action<Color>? ColorEdited;

    public event Action<bool>? TransparencyEdited;

    public event Action<ToolKind>? ToolSelected;

    public event Action<Vector3.Axis, bool, float>? SymmetryEdited;


    public event Action<Vector3.Axis>? MirrorRequested;

    public event Action<CameraLens>? CameraLensEdited;

    public event Action<ReferenceImageSettings>? ReferenceImageEdited;

    public event Action<ReferenceModelSettings>? ReferenceModelEdited;

    public event Action? ReferencePathChangeRequested;

    public event Action? ActiveCameraRequested;

    public event Action<Vector3>? PivotEdited;

    public event Action? PivotCenterRequested;

    public event Action? ColorEditBegan;

    public event Action? ColorEditEnded;

    /// <summary>Relayed from the fields; the arbiter counts them, so no tallying here.</summary>
    public event Action? InteractionBegan;

    public event Action? InteractionEnded;

    public void SetAllowedModes(TransformChannel channel, IReadOnlyList<FieldMode> modes) =>
        _fieldsByChannelDictionary[channel].SetAllowedModes(modes);

    public void Clear()
    {
        _name.Text = "Nothing selected";

        SetKindIcon(null);
        ShowSection(null);
    }

    public void SetTarget(
        string name,
        ComponentKind component,
        TransformValues values,
        TransformModes modes)
    {
        _name.Text = name;

        SetKindIcon(EditorIconCatalog.ForRow(SelectionKind.Element, component));
        ShowSection(_transform);

        _fieldsByChannelDictionary[TransformChannel.Position].SetValue(values.Position, modes.Position);
        _fieldsByChannelDictionary[TransformChannel.Rotation]
            .SetValue(values.RotationDegrees, modes.Rotation);
        _fieldsByChannelDictionary[TransformChannel.Scale].SetValue(values.Scale, modes.Scale);

        // Editable until this target says otherwise — SetPrefabContent is the one
        // that does, and it arrives after this for every read-only element.
        SetTransformEditable(true);
    }

    public void SetPivot(Vector3 point) => _pivotField.SetValue(point);

    public void SetSceneTarget(string name, Vector3I size)
    {
        _name.Text = name;

        SetKindIcon(EditorIconCatalog.ForRow(SelectionKind.Scene, ComponentKind.None));
        ShowSection(_scene);

        // Read-only, and honestly so: resizing the region is a model operation
        // with a question behind it — what becomes of the voxels left outside.
        _buildRegion.Text = $"{size.X} × {size.Y} × {size.Z}";
    }

    public void SetPresetOptions(
        IReadOnlyList<MaterialPresetOption> presets,
        IReadOnlyList<MaterialFinishOption> finishes)
    {
        _preset.SetOptions(presets);
        _finish.SetOptions(finishes);
    }

    public void SetMaterialDepth(bool advanced)
    {
        _depth.Select(advanced ? 1 : 0);

        foreach (Control row in _advancedRowsList)
            row.Visible = advanced;
    }

    public void SetColorFormat(ColorTextFormat format) => _color.SetFormat(format);

    public void SetPreviewShown(bool shown) => _preview.SetShown(shown);

    public void SetMaterialTarget(
        string name,
        Color color,
        MaterialAppearance appearance,
        MaterialPreset? preset)
    {
        _name.Text = name;

        // The catalogue answers "no glyph" for a material, because a material
        // row's mark in the tree is its own colour swatch. Here the colour is a
        // row of its own two lines down, so the title takes the history's
        // material glyph rather than nothing.
        SetKindIcon(EditorIconCatalog.Material);
        ShowSection(_material);

        _appearance = appearance;

        _preview.SetMaterial(name, color, appearance);
        _color.SetColor(color);

        // No signal, and the guard as well: a checkbox written to reflect state
        // announces the write, and a panel that answers its own announcement
        // edits the document for having been looked at.
        _transparent.SetPressedNoSignal(appearance.Transparent);
        _preset.SetPreset(preset);

        _roughness.SetValue(appearance.Roughness);
        _metallic.SetValue(appearance.Metallic);
        _emission.SetValue(appearance.Emission);
        _finish.SetFinish(appearance.Finish);
        _scale.SetValue(appearance.FinishScale);
        _relief.SetValue(appearance.FinishRelief);
    }

    /// <summary>
    /// The picker being open means two things at once, to two different
    /// listeners: the user is busy, so a handover waits; and a colour gesture is
    /// under way, so the edits it produces belong together.
    /// </summary>
    private void OnColorInteractionBegan()
    {
        InteractionBegan?.Invoke();
        ColorEditBegan?.Invoke();
    }

    private void OnColorInteractionEnded()
    {
        ColorEditEnded?.Invoke();
        InteractionEnded?.Invoke();
    }

    public void SetCamera(CameraLens lens, bool active)
    {
        _camera.Visible = true;

        _cameraField.SetValues(lens, active);
    }

    public void SetReferenceImage(ReferenceImageSettings settings, bool found)
    {
        _reference.Visible = true;

        _referenceField.SetValues(settings, found);
    }

    public void SetReferenceModel(ReferenceModelSettings settings, bool found)
    {
        _reference.Visible = true;

        _referenceField.SetValues(settings, found);
    }

    public void SetSymmetry(SymmetryValues values)
    {
        _symmetry.Visible = true;

        _symmetryField.SetValues(values);
    }

    public void SetTools(ToolKind activeTool)
    {
        _tools.Visible = true;

        _toolStrip.SetActiveTool(activeTool);
    }

    /// <summary>
    /// One row for both readings, because an element is one or the other: an
    /// ordinary element is offered the chance to become a prefab, an instance is
    /// told what it stands for and offered the two things that can be done about it.
    /// </summary>
    public void SetPrefab(string? sourceName)
    {
        _prefab.Visible = true;

        var isInstance = sourceName != null;

        _prefabSource.Text = isInstance
            ? $"Instance of '{sourceName}'"
            : "Not a prefab";
        _prefabSource.Visible = isInstance;

        _makePrefab.Visible = !isInstance;
        _openPrefab.Visible = isInstance;
        _unpack.Visible = isInstance;

        _openPrefab.TooltipText =
            "Edit what this instance stands for. The library entry opens in a room of "
            + "its own; every instance of it follows what you do there.";
    }

    /// <summary>
    /// The prefab section, worn by an element that is part of an instance: what it
    /// belongs to and the way to where it can be changed, with the transform shown
    /// and not taking input. The tools and the mirrors are simply not offered —
    /// the presenter sends neither for such an element.
    /// </summary>
    public void SetPrefabContent(string prefabName)
    {
        _prefab.Visible = true;

        _prefabSource.Text = $"Part of the prefab '{prefabName}'. Open the prefab to change it.";
        _prefabSource.Visible = true;

        _makePrefab.Visible = false;
        _openPrefab.Visible = true;
        _unpack.Visible = false;

        _openPrefab.TooltipText =
            "Edit the prefab this element is part of. It opens in a room of its own; "
            + "every instance of it follows what you do there.";

        SetTransformEditable(false);
    }

    private void SetTransformEditable(bool editable)
    {
        foreach (Vector3Field field in _fieldsByChannelDictionary.Values)
            field.SetEditable(editable);

        _pivotField.SetEditable(editable);
    }

    public void SetActiveTool(ToolKind tool) => _toolStrip.SetActiveTool(tool);

    /// <summary>
    /// One section at a time — what is selected is one kind of thing, and the
    /// panel showing two sets of rows at once would be lying about that. The
    /// tools and symmetry sections are the companions: they hide with every new
    /// target and come back only when <see cref="SetTools"/> or
    /// <see cref="SetSymmetry"/> or <see cref="SetCamera"/> arrives for this one.
    /// </summary>
    private void ShowSection(InspectorSection? section)
    {
        _transform.Visible = ReferenceEquals(section, _transform);
        _tools.Visible = false;
        _symmetry.Visible = false;
        _camera.Visible = false;
        _reference.Visible = false;
        _prefab.Visible = false;
        _scene.Visible = ReferenceEquals(section, _scene);
        _material.Visible = ReferenceEquals(section, _material);
    }

    /// <summary>
    /// Hidden rather than left blank when there is no glyph, so the name does
    /// not sit at an indent nothing explains.
    /// </summary>
    private void SetKindIcon(string? icon)
    {
        _kind.Visible = icon is not null && IsInsideTree();
        _kind.Texture = _kind.Visible
            ? GetThemeIcon(icon!, EditorThemeFactory.SemanticType)
            : null;
    }

    /// <summary>
    /// One tuning row: shown only at the advanced depth, reporting the whole
    /// appearance with its own value replaced, and holding a gesture open while
    /// its handle is dragged so a drag is one history record.
    /// </summary>
    private void AddAdvancedRow(SliderField field, Func<float, MaterialAppearance> change)
    {
        field.Edited += value => AppearanceEdited?.Invoke(change(value));
        field.InteractionBegan += OnColorInteractionBegan;
        field.InteractionEnded += OnColorInteractionEnded;

        _advancedRowsList.Add(field);
        _material.AddRow(field);
    }

    private void AddField(TransformChannel channel, string title)
    {
        var field = new Vector3Field(title, channel);

        field.ValueEdited += value => ValueEdited?.Invoke(channel, value);
        field.ModeSelected += mode => ModeEdited?.Invoke(channel, mode);
        field.InteractionBegan += () => InteractionBegan?.Invoke();
        field.InteractionEnded += () => InteractionEnded?.Invoke();

        _fieldsByChannelDictionary[channel] = field;
        _transform.AddRow(field);
    }
}
