using System;
using VoxelCAD.CommonTypes;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// A question, as a glass card in the shell rather than as an
/// <see cref="AcceptDialog"/>.
/// </summary>
/// <remarks>
/// The same move the context menu and the settings form already made, and for
/// the same reason: an <see cref="AcceptDialog"/> is a <see cref="Window"/>, so
/// it paints its own panel outside any child's reach — no glass, no hairline,
/// its own corner radius, its own title bar with the engine's close button, and
/// its own bolder body text. It was the last surface in the editor still
/// wearing the engine's clothes, and it turns up at exactly the moments a user
/// is being asked to decide something.
/// <para>
/// Two smaller things come with being a <see cref="Control"/>. Its buttons are
/// ordinary named ones, so a question can be answered by driving — an
/// <see cref="AcceptDialog"/>'s live in a window <c>ui</c> cannot see. And
/// Escape is hung off the viewport's own report rather than off a second key
/// handler, because the viewport consumes Escape before any of them: the same
/// rule the settings form and the temporary panels already pay.
/// </para>
/// <para>
/// No scrim behind it, matching the settings form. A modal that greys the window
/// is a decision about every modal in this editor at once, not about this one.
/// </para>
/// </remarks>
internal sealed partial class DialogCard : Control
{
    /// <summary>
    /// How wide the card may grow before its message wraps. Wide enough for a
    /// sentence about a file path, narrow enough that the sentence is one
    /// paragraph rather than one line across a large monitor.
    /// </summary>
    private const float CardWidth = 380f;

    /// <summary>
    /// What stands between the title, the sentence and the answers. Wider than
    /// the theme's own row gap: three things that are not a list, and a dialog
    /// whose parts touch reads as a banner rather than as a question.
    /// </summary>
    private const int Breathing = 10;

    private readonly PanelContainer _card;
    private readonly Label _title = new() { Name = "DialogTitle" };
    private readonly Label _message = new() { Name = "DialogMessage" };
    private readonly TextureRect _glyph = new() { Name = "DialogGlyph" };
    private readonly HBoxContainer _titleRow;
    private readonly HBoxContainer _buttons = new() { Name = "DialogButtons" };

    /// <summary>
    /// How small the card starts, and the same two per cent the settings card
    /// arrives on: the two are one family — a card centred over the window,
    /// asked for and then dismissed — and they can be on screen at once, a
    /// question being asked over the settings form.
    /// </summary>
    private const float ArrivalScale = 0.98f;

    private readonly Smoothed _presence;

    private ConfirmResult? _answered;

    public DialogCard(MotionTiming timing)
    {
        // Snapped rather than left unaimed: the first target a Smoothed is ever
        // given snaps, so the first question would arrive outright.
        _presence = new Smoothed(MotionSpeed.Opacity, timing);
        _presence.SnapTo(0f);

        Name = "Dialog";
        Visible = false;

        // Modal: everything under it is out of reach until the question is
        // answered, which is the whole of what a dialog being modal means.
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _title.ThemeTypeVariation = "DialogTitle";
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _message.CustomMinimumSize = new Vector2(CardWidth, 0f);

        _glyph.CustomMinimumSize = new Vector2(ChromePanel.GlyphSide, ChromePanel.GlyphSide);
        _glyph.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _glyph.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _glyph.Visible = false;

        _titleRow = Ui.HBox(_glyph, _title).Named("DialogTitleRow");
        _buttons.Alignment = BoxContainer.AlignmentMode.End;

        VBoxContainer body = Ui.VBox(_titleRow, _message, _buttons).Named("DialogBody");
        body.AddThemeConstantOverride("separation", Breathing);

        _card = new PanelContainer
        {
            Name = "DialogCard",
            ThemeTypeVariation = "GlassPanel",
            MouseFilter = MouseFilterEnum.Stop
        };
        _card.AddChild(new GlassBackdrop());
        _card.AddChild(Ui.Margin(body, 12).Named("DialogPadding"));

        // Centred by a container, not by anchors: a control anchored to the
        // middle grows down and right from it, which is how the settings form
        // once put its own corner in the centre of the window.
        var centre = new CenterContainer { Name = "DialogCentre" };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        centre.MouseFilter = MouseFilterEnum.Ignore;
        centre.AddChild(_card);

        AddChild(centre);
    }

    /// <summary>
    /// How the question was answered. Raised once per question: a card already
    /// answered is on its way down and must not answer again, which is the same
    /// guard the engine's dialog needed for its own second signal.
    /// </summary>
    public event Action<ConfirmResult>? Answered;

    /// <summary>
    /// Asked of where the card is heading, not of whether it is drawn: one still
    /// fading out is visible and is no longer a question anybody is being asked.
    /// </summary>
    public bool IsOpen => _presence.Target > 0f;

    public void Ask(ConfirmRequest request)
    {
        Fill(request.Title, request.Message, warning: false);

        // Right to left, as every desktop puts them: the answer that goes
        // through last, the one that does not next to it, and the third — throw
        // the work away — set apart at the far end where it cannot be pressed by
        // aiming at Cancel and missing.
        if (request.DenyLabel.Length > 0)
        {
            AddButton(request.DenyLabel, "DialogDeny", ConfirmResult.Deny, primary: false);
            _buttons.AddChild(Spacer());
        }

        AddButton(request.CancelLabel, "DialogCancel", ConfirmResult.Cancel, primary: false);

        Open(AddButton(request.ConfirmLabel, "DialogConfirm", ConfirmResult.Confirm, primary: true));
    }

    public void ShowError(string title, string message)
    {
        Fill(title, message, warning: true);

        Open(AddButton("OK", "DialogConfirm", ConfirmResult.Confirm, primary: true));
    }

    /// <summary>
    /// Escape, arriving from the viewport's own report. Answers Cancel, which is
    /// what dismissing a question has always meant — and what
    /// <see cref="ConfirmResult.Cancel"/> is ordered first for.
    /// </summary>
    public bool TryDismiss()
    {
        if (!IsOpen)
            return false;

        Answer(ConfirmResult.Cancel);

        return true;
    }

    private static Control Spacer() => new()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        MouseFilter = MouseFilterEnum.Ignore
    };

    private void Fill(string title, string message, bool warning)
    {
        _answered = null;

        _title.Text = title;
        _message.Text = message;
        _message.Visible = message.Length > 0;

        // Only the error wears one. A confirmation's title already names what is
        // being asked about, and a question mark in a circle would be a picture
        // of the fact that a dialog is open.
        _glyph.Visible = warning;

        foreach (Node child in _buttons.GetChildren())
        {
            _buttons.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    /// The warning glyph, and the idling. Both belong here rather than in the
    /// constructor: a node outside the tree cannot see the theme, and Godot
    /// turns processing on for a script that overrides <c>_Process</c> as it
    /// sets the node up — after any constructor. The glyph used to be fetched
    /// by a one-shot inside <c>_Process</c>, which only worked because nothing
    /// ever turned processing off.
    /// </summary>
    public override void _Ready()
    {
        _glyph.Texture = GetThemeIcon(EditorIconCatalog.Warning, EditorThemeFactory.SemanticType);

        SetProcess(false);
    }

    /// <summary>
    /// Moves the card on. One number does the fade and the size, because they
    /// are one sentence about the card: how much of it has arrived.
    /// </summary>
    public override void _Process(double delta)
    {
        _presence.Tick((float)delta);

        float presence = _presence.Value;

        Modulate = new Color(1f, 1f, 1f, presence);

        _card.PivotOffset = _card.Size * 0.5f;
        _card.Scale = Vector2.One * Mathf.Lerp(ArrivalScale, 1f, presence);

        if (!_presence.IsSettled)
            return;

        if (presence <= 0f)
            Visible = false;

        SetProcess(false);
    }

    private void Open(Button primary)
    {
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;

        _presence.Retarget(1f);
        SetProcess(true);

        // The card sizes itself around whichever question it was handed, and the
        // previous one's buttons are freed rather than resized.
        _card.ResetSize();

        // A frame later: a button that is not laid out yet cannot take focus,
        // and the freed buttons of the previous question are still in the tree
        // until the queue drains.
        primary.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>
    /// One answer. The one that goes through wears the accent and takes the
    /// keyboard, which does two jobs with one act: it says which answer the
    /// question expects, and it is what makes Enter answer — a focused
    /// <see cref="Button"/> is pressed by <c>ui_accept</c>, so no second key
    /// handler is needed and none can disagree with the button that is lit.
    /// </summary>
    private Button AddButton(string label, string name, ConfirmResult result, bool primary)
    {
        var button = new Button
        {
            Text = label,
            ThemeTypeVariation = primary ? "PrimaryButton" : string.Empty
        }.Named(name);

        if (!primary)
            button.MouseOnly();

        button.Pressed += () => Answer(result);
        _buttons.AddChild(button);

        return button;
    }

    private void Answer(ConfirmResult result)
    {
        // Once. A button pressed on a card already answered would report a
        // second time over a question whose caller has moved on.
        if (_answered.HasValue)
            return;

        _answered = result;

        // Paint eases, refusal does not: the card stops being modal on the
        // answer rather than when the fade finishes, or a question already
        // answered would go on holding the editor for a fifth of a second.
        MouseFilter = MouseFilterEnum.Ignore;

        _presence.Retarget(0f);
        SetProcess(true);

        Answered?.Invoke(result);
    }
}
