using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The three mirror-plane rows: a toggle and a position per local axis. The
/// spin boxes step by half a cell because that is the whole domain of a mirror
/// plane — a cell boundary or a cell centre — so for once Godot's Range
/// quantising a typed value to its step is the wanted behaviour rather than the
/// trap <see cref="NumericFieldRange"/> guards against.
/// </summary>
internal sealed partial class SymmetryField : VBoxContainer
{
    private const int AxisCount = 3;

    /// <summary>A whole number of steps on either side of zero, so the
    /// quantising lattice stays anchored on the half-cell grid.</summary>
    private const double PositionLimit = 1_000_000d;

    private static readonly string[] AxisNames = { "X", "Y", "Z" };

    /// <summary>Every colour slot a <see cref="CheckBox"/>'s label can be drawn in.</summary>
    private static readonly string[] CheckColorSlots =
    {
        "font_color",
        "font_hover_color",
        "font_pressed_color",
        "font_hover_pressed_color",
        "font_focus_color"
    };

    private readonly CheckBox[] _enabledCheckBoxes = new CheckBox[AxisCount];
    private readonly SpinBox[] _planeSpinBoxes = new SpinBox[AxisCount];
    private readonly Button[] _mirrorButtons = new Button[AxisCount];

    private bool _reporting = true;

    public SymmetryField()
    {
        for (var axis = 0; axis < AxisCount; axis++)
        {
            _enabledCheckBoxes[axis] = CreateEnabledCheckBox(axis);
            _planeSpinBoxes[axis] = CreatePlaneSpinBox(axis);
            _mirrorButtons[axis] = CreateMirrorButton(axis);

            AddChild(Ui.HBox(_enabledCheckBoxes[axis], _planeSpinBoxes[axis], _mirrorButtons[axis]));
        }
    }

    public event Action<Vector3.Axis, bool, float>? PlaneEdited;

    /// <summary>
    /// The row's Mirror button: write the image of every cell across this row's
    /// plane, where empty. Reported, never computed — which cells that is, is the
    /// presenter's to know, and it goes through the very function the command does.
    /// </summary>
    public event Action<Vector3.Axis>? MirrorRequested;

    /// <summary>
    /// Raised while a position box has keyboard focus, on the same terms as the
    /// transform fields: someone typing a plane in counts as busy.
    /// </summary>
    public event Action? InteractionBegan;

    public event Action? InteractionEnded;

    /// <summary>Reflects model state without reporting it back as a user edit.</summary>
    public void SetValues(SymmetryValues values)
    {
        _reporting = false;

        Apply(0, values.X);
        Apply(1, values.Y);
        Apply(2, values.Z);

        _reporting = true;
    }

    private void Apply(int axis, SymmetryPlane plane)
    {
        _enabledCheckBoxes[axis].ButtonPressed = plane.Enabled;
        _planeSpinBoxes[axis].Value = plane.PlaneDoubled / 2d;
    }

    /// <summary>
    /// A mouse target, not a keyboard stop: like the material colour button, it
    /// consumes no arrows, so it must not be able to hold focus — a control that
    /// did would let the arrows escape the dock.
    /// </summary>
    private CheckBox CreateEnabledCheckBox(int axis)
    {
        var check = new CheckBox
        {
            Text = AxisNames[axis],
            FocusMode = FocusModeEnum.None
        };

        check.Name = $"SymmetryAxis{axis}";
        check.Toggled += _ => Report(axis);

        return check;
    }

    public override void _Notification(int what)
    {
        if ((long)what == NotificationThemeChanged && IsInsideTree())
            TintAxisChecks();
    }

    public override void _Ready() => TintAxisChecks();

    /// <summary>
    /// Writes each box its axis colour — the same three the handles and the
    /// transform rows name their axes in, darkened by the theme where the card
    /// is light. Three greys would be a second vocabulary for one set of facts.
    /// </summary>
    /// <remarks>
    /// Every state, not just the idle one: a checked box takes
    /// <c>font_pressed_color</c>, which is the accent, so overriding only
    /// <c>font_color</c> left a ticked X reading blue — the colour Z wears.
    /// </remarks>
    private void TintAxisChecks()
    {
        for (var axis = 0; axis < 3; axis++)
        {
            if (FindChild($"SymmetryAxis{axis}", recursive: true, owned: false) is not CheckBox check)
                continue;

            Color color = GetThemeColor(
                EditorAxisColors.NameOf(axis), EditorThemeFactory.SemanticType);

            foreach (string slot in CheckColorSlots)
                check.AddThemeColorOverride(slot, color);
        }
    }

    private SpinBox CreatePlaneSpinBox(int axis)
    {
        var spin = new SpinBox
        {
            Step = 0.5d,
            MinValue = -PositionLimit,
            MaxValue = PositionLimit
        };

        spin.MinSize(40f, 0f).Expand();
        spin.ValueChanged += _ => Report(axis);

        // The SpinBox itself never takes focus; its inner LineEdit does.
        LineEdit line = spin.GetLineEdit();
        line.FocusEntered += () => InteractionBegan?.Invoke();
        line.FocusExited += () => InteractionEnded?.Invoke();

        return spin;
    }

    /// <summary>
    /// A one-shot beside the row it acts for, on the pivot row's Center button's
    /// terms: a mouse target rather than a keyboard stop, and never through
    /// <see cref="Report"/> — pushing model state back must not fire it.
    /// </summary>
    private Button CreateMirrorButton(int axis)
    {
        var button = new Button
        {
            Text = "Mirror",
            ThemeTypeVariation = "CaptionButton",
            FocusMode = FocusModeEnum.None,
            TooltipText = "Write every cell's mirror image across this plane, where empty",
            Name = $"SymmetryMirror{axis}"
        };

        button.Pressed += () =>
        {
            // The button takes no focus, so a plane typed into the box beside it is
            // still uncommitted — a SpinBox takes its text on Enter or on losing
            // focus, and this steals neither. Committing first is what makes the
            // button mirror across the plane the row is *showing*: without it the
            // typed number was ignored and then overwritten by the refresh the
            // mirror's own edit triggers.
            _planeSpinBoxes[axis].Apply();
            MirrorRequested?.Invoke((Vector3.Axis)axis);
        };

        return button;
    }

    private void Report(int axis)
    {
        if (!_reporting)
            return;

        PlaneEdited?.Invoke(
            (Vector3.Axis)axis,
            _enabledCheckBoxes[axis].ButtonPressed,
            (float)_planeSpinBoxes[axis].Value);
    }
}
