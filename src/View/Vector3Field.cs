using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// One transform vector: a title and mode toggle on the first line, X/Y/Z on the
/// second. Two lines rather than one long row so it survives a fixed-width dock
/// whatever the title length.
/// </summary>
/// <remarks>
/// Deliberately dumb about modes. The step size is only a spin-box affordance —
/// the field never rounds a value itself, it displays whatever the model wrote
/// back. Snapping lives in one place, and it is not here. Keeping that promise
/// takes care, because Godot's <c>Range</c> quantises to its own lattice: see
/// <see cref="NumericFieldRange"/>.
/// </remarks>
internal sealed partial class Vector3Field : AxisBlock
{
    private readonly ModeToggle _modeToggle;
    private readonly SpinBox[] _axisSpinBoxes;
    private readonly bool _halfCellInteger;

    private bool _reporting = true;

    /// <summary>
    /// The last value the model wrote, kept so that editing one axis reports the
    /// other two as the model has them rather than as the fields rounded them.
    /// </summary>
    private Vector3 _value;

    /// <param name="channel">
    /// Which channel this is, and the two things that follow from it. Position
    /// names the pivot and the pivot lives on the half-cell lattice, so the
    /// values the model writes back land on halves — a box stepping by one would
    /// quantise them to a value the model never held and display a lie. And
    /// rotation is measured in degrees, which is the difference between a mode
    /// button reading "1" and one reading "1°".
    /// <para>
    /// One fact rather than the two flags it used to be derived into: the caller
    /// knows the channel, and every question this class asks of it has one
    /// answer per channel.
    /// </para>
    /// </param>
    public Vector3Field(string title, TransformChannel channel)
        : this(title, channel, new ModeToggle(angular: channel == TransformChannel.Rotation),
            CreateAxisSpinBoxes())
    {
    }

    private Vector3Field(
        string title, TransformChannel channel, ModeToggle modeToggle, SpinBox[] axes)
        : base(title, modeToggle, axes)
    {
        _halfCellInteger = channel == TransformChannel.Position;
        _modeToggle = modeToggle;
        _axisSpinBoxes = axes;

        _modeToggle.ModeSelected += mode => ModeSelected?.Invoke(mode);

        for (var axis = 0; axis < AxisCount; axis++)
            Wire(axis, axes[axis]);
    }

    public event Action<Vector3>? ValueEdited;

    public event Action<FieldMode>? ModeSelected;

    /// <summary>
    /// Raised while an axis has keyboard focus. Someone typing a value counts as
    /// busy, so a handover waits rather than seizing the editor mid-number.
    /// </summary>
    public event Action? InteractionBegan;

    public event Action? InteractionEnded;

    public void SetAllowedModes(IReadOnlyList<FieldMode> modes) => _modeToggle.SetAllowedModes(modes);

    /// <summary>Reflects model state without reporting it back as a user edit.</summary>
    public void SetValue(Vector3 value, FieldMode mode)
    {
        _reporting = false;
        _value = value;

        // Range and limits first: assigning a value against the previous mode's
        // lattice would quantise it on the way in.
        ApplyMode(mode);
        _modeToggle.SetMode(mode);

        for (var axis = 0; axis < AxisCount; axis++)
            _axisSpinBoxes[axis].Value = value[axis];

        _reporting = true;
    }

    /// <summary>
    /// Shows the value without taking input — for an element that cannot be changed
    /// where it stands. The boxes read as uneditable and the mode track greys out,
    /// which is the refusal said before anybody types rather than after.
    /// </summary>
    public void SetEditable(bool editable)
    {
        foreach (SpinBox spin in _axisSpinBoxes)
            spin.Editable = editable;

        _modeToggle.SetEnabled(editable);
    }

    /// <summary>
    /// The boxes have to exist before the base constructor can lay them out, so
    /// they are made here and wired afterwards — nothing may touch <c>this</c>
    /// before <c>base</c> has run.
    /// </summary>
    private static SpinBox[] CreateAxisSpinBoxes()
    {
        var axes = new SpinBox[AxisCount];

        for (var axis = 0; axis < AxisCount; axis++)
        {
            axes[axis] = new SpinBox
            {
                AllowGreater = true,
                AllowLesser = true
            };

            axes[axis].MinSize(40f, 0f).Expand();
        }

        return axes;
    }

    private void Wire(int axis, SpinBox spin)
    {
        spin.ValueChanged += value => Report(axis, value);

        // The SpinBox itself never takes focus; its inner LineEdit does.
        LineEdit line = spin.GetLineEdit();
        line.FocusEntered += () => InteractionBegan?.Invoke();
        line.FocusExited += () => InteractionEnded?.Invoke();
    }

    private void ApplyMode(FieldMode mode)
    {
        double step = _halfCellInteger && mode == FieldMode.Integer
            ? 0.5d
            : NumericFieldRange.GetStep(mode);
        double limit = NumericFieldRange.GetLimit(mode);

        foreach (SpinBox spin in _axisSpinBoxes)
        {
            spin.Step = step;
            spin.MinValue = -limit;
            spin.MaxValue = limit;
        }
    }

    /// <summary>
    /// Only the edited axis comes from the field; the other two come from what the
    /// model last wrote. A field rounds to its step, so reading all three back
    /// would quietly re-write the untouched ones at the display's precision.
    /// </summary>
    private void Report(int axis, double value)
    {
        if (!_reporting)
            return;

        Vector3 edited = _value;
        edited[axis] = (float)value;

        ValueEdited?.Invoke(edited);
    }
}
