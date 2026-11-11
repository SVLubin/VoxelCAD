using System;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// One tunable number: a name, a slider, and the value written beside it. What
/// the advanced half of the material editor is made of.
/// </summary>
/// <remarks>
/// A slider rather than a spin box, deliberately. These values have no units a
/// person reasons in — nobody knows what roughness 0.34 is — so what matters is
/// where they sit between the two ends, which is what a slider shows and a
/// number does not. The number is shown anyway, because a value that can be read
/// can be repeated.
/// <para>
/// Reports continuously while dragged, which is what makes it worth having: the
/// viewport follows the pointer and the surface is judged by watching it. The
/// gesture is what makes that affordable — a drag is one history record, opened
/// on the press and closed on the release.
/// </para>
/// </remarks>
internal sealed partial class SliderField : HBoxContainer
{
    private readonly HSlider _slider = new();
    private readonly Label _value = new();
    private readonly int _decimals;

    private bool _reporting = true;

    public SliderField(string name, string title, float minimum, float maximum, int decimals, string hint)
    {
        _decimals = decimals;

        _slider.MinValue = minimum;
        _slider.MaxValue = maximum;
        _slider.Step = Math.Pow(0.1d, decimals);
        _slider.Named(name).Expand(vertical: false).MouseOnly();
        _slider.CustomMinimumSize = new Vector2(70f, 18f);
        _slider.TooltipText = hint;

        _slider.ValueChanged += OnValueChanged;
        _slider.DragStarted += () => InteractionBegan?.Invoke();
        _slider.DragEnded += _ => InteractionEnded?.Invoke();

        _value.CustomMinimumSize = new Vector2(34f, 0f);
        _value.HorizontalAlignment = HorizontalAlignment.Right;

        Label caption = Ui.Label(title);
        caption.CustomMinimumSize = new Vector2(66f, 0f);

        AddChild(caption);
        AddChild(_slider);
        AddChild(_value);
    }

    public event Action<float>? Edited;

    /// <summary>Raised while the handle is held, so a drag is one history record.</summary>
    public event Action? InteractionBegan;

    public event Action? InteractionEnded;

    /// <summary>Reflects model state without reporting it back as a user edit.</summary>
    public void SetValue(float value)
    {
        _reporting = false;
        _slider.Value = value;
        _reporting = true;

        WriteValue(value);
    }

    private void OnValueChanged(double value)
    {
        WriteValue((float)value);

        if (_reporting)
            Edited?.Invoke((float)value);
    }

    private void WriteValue(float value) =>
        _value.Text = value.ToString($"F{_decimals}", System.Globalization.CultureInfo.InvariantCulture);
}
