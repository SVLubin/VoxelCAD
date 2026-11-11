using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The ready-made materials, in one list, grouped. Shows which one the material
/// currently is — or <b>Custom</b>, where its values are nobody's preset.
/// </summary>
/// <remarks>
/// Custom is an entry rather than a blank, because a blank row says nothing at
/// all. Choosing it does nothing — there is no set of values called Custom to
/// apply — and it is left enabled rather than greyed for a reason found by
/// looking: Godot draws no text for a selected entry that is disabled, so a
/// tuned material's row came out empty either way, which is the one thing this
/// entry exists to prevent.
/// </remarks>
internal sealed partial class PresetField : HBoxContainer
{
    /// <summary>
    /// Outside the enum's range, and deliberately not minus one: a separator's own
    /// id is minus one, so the search for Custom found the separator above it and
    /// selected that — an entry with no text, which is exactly the blank row this
    /// class exists to avoid. Found by looking at the panel, not by reading it.
    /// </summary>
    private const int CustomId = 9999;

    private readonly OptionButton _presets = new();

    public PresetField(string title)
    {
        _presets.Named("MaterialPreset").Expand(vertical: false).MouseOnly();
        _presets.TooltipText =
            "A ready-made material: how it takes light, what facture it wears, and its "
            + "colour. Tuning any of those afterwards is what the advanced rows are for.";

        _presets.ItemSelected += OnChosen;

        Label caption = Ui.Label(title);
        caption.CustomMinimumSize = new Vector2(66f, 0f);

        AddChild(caption);
        AddChild(_presets);
    }

    public event Action<MaterialPreset>? Chosen;

    /// <summary>
    /// Fills the list. Separated from construction because the words are the
    /// presenter's — the model owns what a preset is and not what it is called —
    /// and they arrive after the panel has been built.
    /// </summary>
    public void SetOptions(IReadOnlyList<MaterialPresetOption> options)
    {
        _presets.Clear();

        MaterialPresetGroup? group = null;

        foreach (MaterialPresetOption option in options)
        {
            if (group is not null && option.Group != group)
                _presets.AddSeparator();

            group = option.Group;
            _presets.AddItem(option.Label, (int)option.Preset);
        }

        _presets.AddSeparator();
        _presets.AddItem("Custom", CustomId);
    }

    /// <summary>
    /// Which preset the material's values are, or none for Custom. Reflects model
    /// state without reporting it back as a choice.
    /// </summary>
    public void SetPreset(MaterialPreset? preset)
    {
        int id = preset is null ? CustomId : (int)preset.Value;

        for (var index = 0; index < _presets.ItemCount; index++)
        {
            if (_presets.IsItemSeparator(index) || _presets.GetItemId(index) != id)
                continue;

            _presets.Selected = index;

            return;
        }
    }

    private void OnChosen(long index)
    {
        int id = _presets.GetItemId((int)index);

        if (id == CustomId)
            return;

        Chosen?.Invoke((MaterialPreset)id);
    }
}
