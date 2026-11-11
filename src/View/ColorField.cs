using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// One colour on one line: a swatch that opens the picker, a chooser for how the
/// value is written, and a single field carrying it in that form.
/// </summary>
/// <remarks>
/// The numbers live in the dock rather than inside the popup, which is the whole
/// point of the row: the value can be read without opening anything, and the
/// picker is left to do the one thing only it can — pick by eye or off the
/// screen. One format at a time, because reading three representations to find
/// the one you wanted is the thing this replaces.
/// <para>
/// The field commits on Enter or on losing focus, never as the digits arrive:
/// typing 138 would otherwise be read as 1, then 13, then 138. And a refresh from
/// the model never round-trips through the text, so a colour picked on the wheel
/// is not quantised by the row that is merely showing it.
/// </para>
/// </remarks>
internal sealed partial class ColorField : HBoxContainer
{
    private readonly ColorPickerButton _swatch = new();
    private readonly OptionButton _format = new();
    private readonly LineEdit _value = new();

    private ColorTextFormat _written = ColorTextFormat.Hex;
    private Color _color = Colors.White;
    private bool _reporting = true;
    private bool _interacting;

    public ColorField(string title)
    {
        _swatch.EditAlpha = true;
        _swatch.MinSize(26f, 24f).Named("MaterialColour").MouseOnly();
        TrimPicker(_swatch.GetPicker());

        _swatch.ColorChanged += OnColorChanged;

        // The picker is a popup, and while it is up the user is mid-edit: a
        // handover waits for it exactly as it waits for a focused number field.
        _swatch.Pressed += OnPressed;
        _swatch.PopupClosed += OnPopupClosed;

        foreach (ColorTextFormat format in Enum.GetValues<ColorTextFormat>())
            _format.AddItem(LabelFor(format), (int)format);

        _format.MinSize(46f, 0f).Named("MaterialColourFormat").MouseOnly();
        _format.TooltipText = "How the colour is written in the field beside it.";
        _format.ItemSelected += OnFormatChosen;

        // Given a floor rather than left to take what is over: an OptionButton
        // claims its own text's width whatever it is asked for, so the field is
        // the one that gets squeezed, and eight hex digits are what it has to show.
        _value.Named("MaterialColourValue").Expand(vertical: false);
        _value.CustomMinimumSize = new Vector2(96f, 0f);
        _value.TooltipText = "The colour, in the chosen notation. Enter to apply.";
        _value.TextSubmitted += _ => Commit();
        _value.FocusEntered += () => InteractionBegan?.Invoke();
        _value.FocusExited += OnValueFocusExited;

        // Fixed rather than expanding, and the same width the other rows' captions
        // have: the slack belongs to the field, which has eight hex digits to show
        // and was clipping the last of them.
        Label caption = Ui.Label(title);
        // Narrower than the tuning rows' captions: the swatch beside it already
        // says what this row is.
        caption.CustomMinimumSize = new Vector2(50f, 0f);

        AddChild(caption);
        AddChild(_swatch);
        AddChild(_format);
        AddChild(_value);
    }

    public event Action<Color>? ColorEdited;

    public event Action<ColorTextFormat>? FormatChosen;

    public event Action? InteractionBegan;

    public event Action? InteractionEnded;

    /// <summary>Reflects model state without reporting it back as a user edit.</summary>
    public void SetColor(Color color)
    {
        _reporting = false;
        _color = color;
        _swatch.Color = color;
        _reporting = true;

        WriteValue();
    }

    /// <summary>Which notation the field writes in. Not reported back as a choice.</summary>
    public void SetFormat(ColorTextFormat format)
    {
        _written = format;
        _format.Selected = (int)format;

        WriteValue();
    }

    /// <summary>
    /// Cuts the engine's picker down to what a voxel material is. Godot's is a
    /// general colour editor and shows everything it can do at once; most of
    /// that is about something this document has no notion of, and the numbers
    /// it does show are on the row outside it now.
    /// </summary>
    private static void TrimPicker(ColorPicker picker)
    {
        picker.EditIntensity = false;
        picker.ColorModesVisible = false;
        picker.PresetsVisible = false;
        picker.CanAddSwatches = false;
        picker.SlidersVisible = false;
        picker.HexVisible = false;
    }

    private static string LabelFor(ColorTextFormat format) => format switch
    {
        ColorTextFormat.Hex => "HEX",
        ColorTextFormat.Rgb => "RGB",
        ColorTextFormat.Hsv => "HSV",
        _ => "?"
    };

    private void WriteValue()
    {
        // Left alone while it is being typed in, or the caret jumps to the end of
        // whatever the model last wrote.
        if (_value.HasFocus())
            return;

        _value.Text = ColorText.Write(_color, _written);
    }

    private void OnFormatChosen(long index)
    {
        var format = (ColorTextFormat)(int)index;

        _written = format;
        WriteValue();

        FormatChosen?.Invoke(format);
    }

    private void OnValueFocusExited()
    {
        Commit();
        InteractionEnded?.Invoke();
    }

    /// <summary>
    /// Rejected text is put back rather than argued with: the row shows what the
    /// colour is, and something that is not a colour cannot be what it is.
    /// </summary>
    private void Commit()
    {
        if (ColorText.TryRead(_value.Text, _written, _color, out Color parsed))
        {
            if (parsed != _color)
                ColorEdited?.Invoke(parsed);
        }

        _value.Text = ColorText.Write(_color, _written);
    }

    /// <summary>
    /// Kept balanced by a flag: clicking the button again while the popup is open
    /// closes it and presses it, and an unmatched "began" would hold the editor
    /// busy against a handover that has nobody to wait for.
    /// </summary>
    private void OnPressed()
    {
        if (_interacting)
            return;

        _interacting = true;
        InteractionBegan?.Invoke();
    }

    private void OnPopupClosed()
    {
        if (!_interacting)
            return;

        _interacting = false;
        InteractionEnded?.Invoke();
    }

    private void OnColorChanged(Color color)
    {
        if (!_reporting)
            return;

        ColorEdited?.Invoke(color);
    }
}
