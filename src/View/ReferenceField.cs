using System;
using System.IO;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// A reference as the inspector shows it: which file it stands for, whether that
/// file is there, how big the picture is in cells, and how it is drawn. One field
/// for both kinds — the rows a picture has and a model has not are hidden rather
/// than built into a second control, because everything they share is everything
/// except two rows.
/// </summary>
/// <remarks>
/// It reports the <b>whole</b> settings on every edit, composed from the ones it
/// was last given rather than read back off the controls — <see cref="CameraField"/>'s
/// own rule, and for the same reason: a spin box quantises to its step, so reading
/// five values back to change one would round the four nobody touched.
/// </remarks>
internal sealed partial class ReferenceField : VBoxContainer
{
    private const double Step = 0.01d;

    private readonly Label _file = new() { Name = "ReferenceFile" };
    private readonly Button _change;
    private readonly Button _oneToOne;
    private readonly SpinBox _width = new() { Name = "ReferenceWidth" };
    private readonly SpinBox _height = new() { Name = "ReferenceHeight" };
    private readonly Label _pixels = new() { Name = "ReferencePixels" };
    private readonly SpinBox _opacity = new() { Name = "ReferenceOpacity" };

    private readonly CheckBox _lit = new()
    {
        Name = "ReferenceLit",
        Text = "Lit by the scene"
    };

    private readonly CheckBox _filtered = new()
    {
        Name = "ReferenceFiltered",
        Text = "Smooth between pixels"
    };

    private readonly CheckBox _wireframe = new()
    {
        Name = "ReferenceWireframe",
        Text = "Draw as edges"
    };

    private readonly HBoxContainer _sizeRow;
    private readonly HBoxContainer _pixelsRow;

    private (string Path, bool Found) _described = (string.Empty, true);
    private ReferenceImageSettings _image = ReferenceImageSettings.Default;
    private ReferenceModelSettings _model = ReferenceModelSettings.Default;
    private bool _isPicture = true;
    private bool _reporting = true;

    public ReferenceField()
    {
        _change = Ui.Button("Change…", () => PathChangeRequested?.Invoke())
            .Named("ReferenceChange");
        _oneToOne = Ui.Button("1:1", () => Report(_image with
            {
                SizeInCells = _image.OneToOneSize
            }))
            .Named("ReferenceOneToOne");

        _file.AutowrapMode = TextServer.AutowrapMode.Off;
        _file.ClipText = true;
        _file.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _file.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        _change.TooltipText =
            "Point this reference at another file. It has to be the same kind of thing — "
            + "a picture cannot become a model.";
        _oneToOne.TooltipText =
            "One pixel of the picture on one voxel. What tracing pixel art wants, and the "
            + "only size that is not a guess.";

        Dial(_width, "How wide the picture stands, in cells.");
        Dial(_height, "How tall the picture stands, in cells.");
        Dial(_opacity, "How solid it is. Nought keeps it in the scene and stops drawing it, "
            + "which is a different thing from hiding the element.");

        _opacity.MinValue = ReferenceLimits.MinimumOpacity;
        _opacity.MaxValue = ReferenceLimits.MaximumOpacity;

        _width.ValueChanged += value => Report(_image with
        {
            SizeInCells = new Vector2((float)value, _image.SizeInCells.Y)
        });

        _height.ValueChanged += value => Report(_image with
        {
            SizeInCells = new Vector2(_image.SizeInCells.X, (float)value)
        });

        _opacity.ValueChanged += value =>
        {
            if (_isPicture)
                Report(_image with { Opacity = (float)value });
            else
                Report(_model with { Opacity = (float)value });
        };

        Switch(_lit, "Whether the scene's lights fall on it. Off by default: a photograph "
            + "already carries its own light, and a drawing reads worse under a key light.");
        Switch(_filtered, "Smoothed between the picture's own pixels. Off is what pixel art "
            + "needs and is why it is the default.");
        Switch(_wireframe, "Drawn as the model's own edges instead of its surfaces, so it "
            + "does not cover what is being built inside it.");

        _lit.Toggled += pressed =>
        {
            if (_isPicture)
                Report(_image with { LitByScene = pressed });
            else
                Report(_model with { LitByScene = pressed });
        };

        _filtered.Toggled += pressed => Report(_image with { Filtered = pressed });
        _wireframe.Toggled += pressed => Report(_model with { Wireframe = pressed });

        _sizeRow = Ui.HBox(
            Caption("Size"),
            _width.Expand(vertical: false),
            _height.Expand(vertical: false),
            _oneToOne).Named("ReferenceSizeRow");

        _pixelsRow = Ui.HBox(Caption("Picture"), _pixels.Expand(vertical: false))
            .Named("ReferencePixelsRow");

        _pixels.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        AddChild(Ui.HBox(Caption("File"), _file, _change).Named("ReferenceFileRow"));
        AddChild(_sizeRow);
        AddChild(_pixelsRow);
        AddChild(Ui.HBox(Caption("Opacity"), _opacity.Expand(vertical: false))
            .Named("ReferenceOpacityRow"));
        AddChild(_lit);
        AddChild(_filtered);
        AddChild(_wireframe);
    }

    /// <summary>The whole settings, every time — see the remarks on why they are whole.</summary>
    public event Action<ReferenceImageSettings>? ImageEdited;

    public event Action<ReferenceModelSettings>? ModelEdited;

    /// <summary>Somebody wants to point this reference at another file.</summary>
    public event Action? PathChangeRequested;

    /// <summary>Raised while a number is being typed, on every other field's terms.</summary>
    public event Action? InteractionBegan;

    public event Action? InteractionEnded;

    /// <summary>
    /// Reflects model state without reporting it back as a user edit. Which rows
    /// show follows the kind: a picture has a size and a smoothing switch, a model
    /// has neither and has edges instead.
    /// </summary>
    public void SetValues(ReferenceImageSettings settings, bool found)
    {
        _reporting = false;

        try
        {
            _image = settings;
            _isPicture = true;

            Describe(settings.Path, found);

            _width.Value = settings.SizeInCells.X;
            _height.Value = settings.SizeInCells.Y;
            _opacity.Value = settings.Opacity;

            _pixels.Text = settings.PixelSize == Vector2I.Zero
                ? "not measured"
                : $"{settings.PixelSize.X} × {settings.PixelSize.Y} px, "
                    + $"{Say(settings.PixelsPerCell.X)} per cell";

            // NoSignal and the guard both, on the camera checkbox's own terms:
            // assigning ButtonPressed emits Toggled, so filling the panel would
            // write to the document for having been looked at.
            _lit.SetPressedNoSignal(settings.LitByScene);
            _filtered.SetPressedNoSignal(settings.Filtered);

            _sizeRow.Visible = true;
            _pixelsRow.Visible = true;
            _filtered.Visible = true;
            _wireframe.Visible = false;

            // Nothing to divide by, so the button would set the size to itself.
            _oneToOne.Disabled = settings.PixelSize == Vector2I.Zero;
        }
        finally
        {
            _reporting = true;
        }
    }

    /// <inheritdoc cref="SetValues(ReferenceImageSettings, bool)"/>
    public void SetValues(ReferenceModelSettings settings, bool found)
    {
        _reporting = false;

        try
        {
            _model = settings;
            _isPicture = false;

            Describe(settings.Path, found);

            _opacity.Value = settings.Opacity;

            _lit.SetPressedNoSignal(settings.LitByScene);
            _wireframe.SetPressedNoSignal(settings.Wireframe);

            _sizeRow.Visible = false;
            _pixelsRow.Visible = false;
            _filtered.Visible = false;
            _wireframe.Visible = true;
        }
        finally
        {
            _reporting = true;
        }
    }

    /// <summary>
    /// The file's own name, and whether it is there. The whole path would be a line
    /// of text nobody can read in a panel this wide; what a reader needs is which
    /// file it is and whether it is loading, and <c>references</c> answers the rest.
    /// </summary>
    private void Describe(string path, bool found)
    {
        _described = (path, found);

        string name = string.IsNullOrEmpty(path) ? "no file" : Path.GetFileName(path);

        _file.Text = found ? name : $"{name} — not found";
        _file.TooltipText = path;

        // Overridden only where it has something to say. A colour read once and
        // kept is the one theme lookup a repaint cannot reach, so the state is
        // remembered and re-dressed on the notification instead.
        if (found)
            _file.RemoveThemeColorOverride("font_color");
        else
            _file.AddThemeColorOverride(
                "font_color",
                GetThemeColor("warning", EditorThemeFactory.SemanticType));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationThemeChanged)
            Describe(_described.Path, _described.Found);
    }

    private void Report(ReferenceImageSettings settings)
    {
        if (!_reporting)
            return;

        _image = settings;

        ImageEdited?.Invoke(settings);
    }

    private void Report(ReferenceModelSettings settings)
    {
        if (!_reporting)
            return;

        _model = settings;

        ModelEdited?.Invoke(settings);
    }

    private void Switch(CheckBox box, string hint)
    {
        box.FocusMode = FocusModeEnum.None;
        box.TooltipText = hint;
    }

    private void Dial(SpinBox box, string hint)
    {
        box.MinValue = ReferenceLimits.MinimumSizeInCells;
        box.MaxValue = ReferenceLimits.MaximumSizeInCells;
        box.Step = Step;
        box.TooltipText = hint;
        box.UpdateOnTextChanged = false;
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        box.GetLineEdit().FocusEntered += () => InteractionBegan?.Invoke();
        box.GetLineEdit().FocusExited += () => InteractionEnded?.Invoke();
    }

    private static Label Caption(string title)
    {
        Label caption = Ui.Label(title);

        caption.CustomMinimumSize = new Vector2(84f, 0f);
        caption.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        return caption;
    }

    private static string Say(float value) => value.ToString("0.###");
}
