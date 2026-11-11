using System;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The pivot row: a title and a "Center" button on the first line, X/Y/Z on the
/// second — the shape <see cref="Vector3Field"/> already uses, so the section
/// reads as one column of rows rather than two kinds of thing.
/// </summary>
/// <remarks>
/// The spin boxes step by half a cell because that is the whole domain of a pivot
/// — the same lattice the mirror planes live on — so for once Godot's Range
/// quantising a typed value to its step is the wanted behaviour rather than the
/// trap <see cref="NumericFieldRange"/> guards against. The button is the one-shot
/// "centre it now": it reports a request and computes nothing, because the middle
/// of a model is the presenter's to know.
/// </remarks>
internal sealed partial class PivotField : AxisBlock
{

    /// <summary>A whole number of steps on either side of zero, so the
    /// quantising lattice stays anchored on the half-cell grid.</summary>
    private const double PositionLimit = 1_000_000d;

    private readonly Button _centerButton;
    private readonly SpinBox[] _axisSpinBoxes;

    private bool _reporting = true;

    public PivotField(string title)
        // The caption's height, so it matches the mode tracks standing in the
        // same place on the three rows above it.
        : this(
            title,
            new Button { Text = "Center", ThemeTypeVariation = "CaptionButton" },
            CreateAxisSpinBoxes())
    {
    }

    private PivotField(string title, Button centerButton, SpinBox[] axes)
        : base(title, centerButton, axes)
    {
        _centerButton = centerButton;
        _axisSpinBoxes = axes;

        // A mouse target, not a keyboard stop: like the other buttons, it consumes
        // no arrows, so a control that could hold focus would let the arrows
        // escape the dock.
        _centerButton.FocusMode = Control.FocusModeEnum.None;
        _centerButton.Pressed += () => CenterRequested?.Invoke();

        foreach (SpinBox spin in axes)
            Wire(spin);
    }

    public event Action<Vector3>? PivotEdited;

    /// <summary>The Center button: place the pivot at the middle of what the element holds.</summary>
    public event Action? CenterRequested;

    /// <summary>
    /// Raised while an axis has keyboard focus, on the same terms as every other
    /// field: someone typing a pivot in counts as busy.
    /// </summary>
    public event Action? InteractionBegan;

    public event Action? InteractionEnded;

    /// <summary>Reflects model state without reporting it back as a user edit.</summary>
    public void SetValue(Vector3 point)
    {
        _reporting = false;

        for (var axis = 0; axis < AxisCount; axis++)
            _axisSpinBoxes[axis].Value = point[axis];

        _reporting = true;
    }

    /// <summary>Shows the pivot without taking input; see <see cref="Vector3Field.SetEditable"/>.</summary>
    public void SetEditable(bool editable)
    {
        foreach (SpinBox spin in _axisSpinBoxes)
            spin.Editable = editable;

        _centerButton.Disabled = !editable;
    }

    /// <summary>
    /// Made before the base constructor can lay them out, and wired after:
    /// nothing may touch <c>this</c> until <c>base</c> has run.
    /// </summary>
    private static SpinBox[] CreateAxisSpinBoxes()
    {
        var axes = new SpinBox[AxisCount];

        for (var axis = 0; axis < AxisCount; axis++)
        {
            axes[axis] = new SpinBox
            {
                Step = 0.5d,
                MinValue = -PositionLimit,
                MaxValue = PositionLimit
            };

            axes[axis].MinSize(40f, 0f).Expand();
        }

        return axes;
    }

    private void Wire(SpinBox spin)
    {
        spin.ValueChanged += _ => Report();

        // The SpinBox itself never takes focus; its inner LineEdit does.
        LineEdit line = spin.GetLineEdit();
        line.FocusEntered += () => InteractionBegan?.Invoke();
        line.FocusExited += () => InteractionEnded?.Invoke();
    }

    private void Report()
    {
        if (!_reporting)
            return;

        PivotEdited?.Invoke(new Vector3(
            (float)_axisSpinBoxes[0].Value,
            (float)_axisSpinBoxes[1].Value,
            (float)_axisSpinBoxes[2].Value));
    }
}
