using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>The facture chooser: one list, one at a time, no grouping.</summary>
internal sealed partial class FinishField : HBoxContainer
{
    private readonly OptionButton _finishes = new();

    public FinishField(string title)
    {
        _finishes.Named("MaterialFinish").Expand(vertical: false).MouseOnly();
        _finishes.TooltipText =
            "The facture laid over the colour: a tiling relief that supplies a normal "
            + "map and a faint shading, never a picture pasted on. 'Smooth' is none.";

        _finishes.ItemSelected += OnChosen;

        Label caption = Ui.Label(title);
        caption.CustomMinimumSize = new Vector2(66f, 0f);

        AddChild(caption);
        AddChild(_finishes);
    }

    public event Action<MaterialFinish>? Chosen;

    /// <summary>
    /// Every finish there is, the ones nobody may pick included: those are listed
    /// and refused rather than left out, so the chooser can still say a material
    /// wears one. Left out, <see cref="SetFinish"/> found no row for it and the
    /// chooser went on showing the previous material's finish.
    /// </summary>
    public void SetOptions(IReadOnlyList<MaterialFinishOption> options)
    {
        _finishes.Clear();

        foreach (MaterialFinishOption option in options)
        {
            _finishes.AddItem(option.Label, (int)option.Finish);
            _finishes.SetItemDisabled(_finishes.ItemCount - 1, !option.Choosable);
        }
    }

    /// <summary>Reflects model state without reporting it back as a choice.</summary>
    public void SetFinish(MaterialFinish finish)
    {
        for (var index = 0; index < _finishes.ItemCount; index++)
        {
            if (_finishes.GetItemId(index) != (int)finish)
                continue;

            _finishes.Selected = index;

            return;
        }
    }

    private void OnChosen(long index) => Chosen?.Invoke((MaterialFinish)_finishes.GetItemId((int)index));
}
