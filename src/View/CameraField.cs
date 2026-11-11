using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// A camera's lens, as the inspector shows it: how the drawing converges, how
/// wide it is, and what the lens does with what is not in focus — plus the one
/// switch that says which camera the scene renders through.
/// </summary>
/// <remarks>
/// <para>
/// It reports the <b>whole</b> lens on every edit, composed from the one it was
/// last given rather than read back off the controls. That is not a shortcut, it
/// is what stops the field destroying what it cannot say: a spin box quantises to
/// its own step, so reading five values back to change one would round the other
/// four every time — the defect this repository has already paid for twice, once
/// on the transform rows and once on the render card's exposure.
/// </para>
/// <para>
/// The steps are chosen against <see cref="RenderDialRange"/>'s rule all the same:
/// every minimum is a whole number of its step, so the lattice runs from zero and
/// each value the model accepts can be typed in.
/// </para>
/// </remarks>
internal sealed partial class CameraField : VBoxContainer
{
    /// <summary>
    /// Fine enough that a lens reads as continuous, and coarse enough to show two
    /// decimals. A hundredth of a degree is past what any eye tells apart, and a
    /// hundredth of a cell is a hundredth of a voxel.
    /// </summary>
    private const double Step = 0.01d;

    private readonly SegmentedRow _projectionRow = new() { Name = "CameraProjectionRow" };
    private readonly SpinBox _fov = new() { Name = "CameraFov" };
    private readonly SpinBox _orthoHeight = new() { Name = "CameraOrthoHeight" };
    private readonly SpinBox _aperture = new() { Name = "CameraBlur" };
    private readonly SpinBox _focus = new() { Name = "CameraFocus" };

    private readonly CheckBox _active = new()
    {
        Name = "CameraRenders",
        Text = "Renders this scene"
    };

    private readonly RenderProjection[] _projections = Enum.GetValues<RenderProjection>();

    private CameraLens _lens = CameraLens.Default;
    private bool _reporting = true;

    public CameraField()
    {
        _projectionRow.SetSegments(_projections
            .Select(projection => new SegmentSpec(
                $"CameraProjection{projection}",
                projection == RenderProjection.Perspective ? "Perspective" : "Parallel",
                projection == RenderProjection.Perspective
                    ? "Edges converge with distance, as the viewport shows them."
                    : "No convergence — parallel edges stay parallel, as a drawing of a "
                        + "part does."))
            .ToList());

        _projectionRow.Chosen += index => Report(_lens with { Projection = _projections[index] });

        Dial(_fov, CameraLimits.MinimumFov, CameraLimits.MaximumFov,
            "How wide a converging lens sees, measured up the picture. A parallel "
            + "lens ignores it and keeps it.");
        Dial(_orthoHeight, 0d, CameraLimits.MaximumOrthographicHeight,
            "How tall the parallel window is, in cells. Nought keeps the framing the "
            + "converging lens had, so changing the projection changes the drawing "
            + "rather than what is in it.");
        Dial(_aperture, 0d, CameraLimits.MaximumAperture,
            "The lens's radius in cells. Nought is a pinhole and everything is sharp; "
            + "above it what is not at the focus distance spreads.");
        Dial(_focus, 0d, CameraLimits.MaximumFocusDistance,
            "How far the lens is focused, in cells. Nought is whatever the camera is "
            + "pointed at — and the render card's Focus button sets this by pointing "
            + "at the picture, which is usually what is wanted.");

        _fov.ValueChanged += value => Report(_lens with { FovDegrees = (float)value });
        _orthoHeight.ValueChanged +=
            value => Report(_lens with { OrthographicHeight = (float)value });
        _aperture.ValueChanged += value => Report(_lens with { Aperture = (float)value });
        _focus.ValueChanged += value => Report(_lens with { FocusDistance = (float)value });

        // Only ever pressed to turn on: un-choosing is not a thing a scene can be
        // asked for — a picture is taken through one camera, so the way to stop
        // this one rendering is to choose another. Disabled while it is the one,
        // which is what draws it and what refuses the press at once.
        _active.FocusMode = FocusModeEnum.None;
        _active.Toggled += OnActiveToggled;
        _active.TooltipText =
            "Take this scene's picture through this camera. Only one renders at a time.";

        AddChild(Row("Drawing", _projectionRow));
        AddChild(Row("Field of view", _fov));
        AddChild(Row("Window", _orthoHeight));
        AddChild(Row("Blur", _aperture));
        AddChild(Row("Focus", _focus));
        AddChild(_active);
    }

    /// <summary>The whole lens, every time — see the remarks on why it is whole.</summary>
    public event Action<CameraLens>? LensEdited;

    /// <summary>The scene should render through this camera.</summary>
    public event Action? ActiveRequested;

    /// <summary>Raised while a number is being typed, on every other field's terms.</summary>
    public event Action? InteractionBegan;

    public event Action? InteractionEnded;

    /// <summary>Reflects model state without reporting it back as a user edit.</summary>
    public void SetValues(CameraLens lens, bool active)
    {
        _reporting = false;

        try
        {
            _lens = lens;

            _projectionRow.Select(Array.IndexOf(_projections, lens.Projection));
            _fov.Value = lens.FovDegrees;
            _orthoHeight.Value = lens.OrthographicHeight;
            _aperture.Value = lens.Aperture;
            _focus.Value = lens.FocusDistance;

            // NoSignal, and the guard above as well. Assigning ButtonPressed emits
            // Toggled, so filling the panel asked the scene to render through the
            // camera being shown — which turned a fallback into a recorded choice
            // every time a camera was merely selected. It shipped, and only driving
            // found it: a fake view raises the event directly, so no test ever went
            // through the checkbox.
            _active.SetPressedNoSignal(active);
            _active.Disabled = active;
        }
        finally
        {
            _reporting = true;
        }
    }

    /// <summary>
    /// A press, never a fill. The guard is the same one every number here goes
    /// through, kept beside <c>SetPressedNoSignal</c> rather than instead of it:
    /// one of them stops the signal and the other stops what it would have done,
    /// and the defect they close is the panel writing to the document just for
    /// having been shown something.
    /// </summary>
    private void OnActiveToggled(bool pressed)
    {
        if (_reporting && pressed)
            ActiveRequested?.Invoke();
    }

    private void Report(CameraLens lens)
    {
        if (!_reporting)
            return;

        _lens = lens;

        LensEdited?.Invoke(lens);
    }

    private void Dial(SpinBox box, double minimum, double maximum, string hint)
    {
        box.MinValue = minimum;
        box.MaxValue = maximum;
        box.Step = Step;
        box.TooltipText = hint;
        box.UpdateOnTextChanged = false;
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        box.GetLineEdit().FocusEntered += () => InteractionBegan?.Invoke();
        box.GetLineEdit().FocusExited += () => InteractionEnded?.Invoke();
    }

    private static HBoxContainer Row(string title, Control control)
    {
        Label caption = Ui.Label(title);
        caption.CustomMinimumSize = new Vector2(84f, 0f);
        caption.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        return Ui.HBox(caption, control.Expand(vertical: false));
    }
}
