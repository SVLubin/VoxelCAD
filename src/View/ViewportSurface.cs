using System;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The 3D viewport's share of the window, expressed as a <see cref="Control"/>:
/// everything the docks do not cover, plus the keyboard whenever it is the
/// focused panel. Navigation and picking read their input from here, so who owns
/// a given event is decided once, by the engine, for wheels, trackpad gestures,
/// clicks, motion and keys alike.
/// </summary>
/// <remarks>
/// One rule, two arbiters, because the devices differ. The pointer has a
/// position, so its owner is the GUI hit test, and the surface is laid out by the
/// shell as the panel between the docks — the region is a fact about the layout,
/// decided where the layout is. The keyboard has no position, so its owner is
/// focus, and the viewport is an ordinary focusable widget: clicking the scene
/// hands it the keyboard exactly as clicking the hierarchy hands the keyboard to
/// the tree.
///
/// What both replace — taking whatever reaches <c>_UnhandledInput</c> — is not
/// an ownership test at all. It means "nobody else claimed it", which is a
/// different question with a different answer: Godot's <see cref="ItemList"/>
/// scrolls on a pan gesture without accepting it, and an arrow key on a focused
/// <see cref="Button"/> is claimed by the engine's own focus navigation before
/// any unhandled handler runs.
/// </remarks>
internal sealed partial class ViewportSurface : Control
{
    private readonly IInputState _input;

    public ViewportSurface(IInputState input)
    {
        _input = input;

        Name = "ViewportSurface";

        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
    }

    /// <summary>
    /// The keyboard went elsewhere — to another panel, or to another
    /// application. Whoever tracks held keys drops them here, so a key that goes
    /// down in the viewport cannot keep acting after the viewport stops
    /// listening; the matching release is delivered to whoever has focus by then,
    /// which is no longer this.
    /// </summary>
    public event Action? KeyboardLost;

    /// <summary>
    /// A pointer button pressed on the scene itself. The shell reads it as "the
    /// hand moved on" and retracts its temporary panel — a click on a panel never
    /// gets here, the GUI hit test routes it to the panel.
    /// </summary>
    public event Action? PointerPressed;

    /// <summary>
    /// Where viewport input goes. Positions are in viewport coordinates, the
    /// space camera projection speaks. Returning true marks the event used, which
    /// both keeps it from travelling on as unhandled input and — for keys — stops
    /// the engine reading it as a request to move focus to a neighbouring panel.
    /// </summary>
    public Func<InputEvent, bool>? InputHandler { get; set; }

    /// <summary>
    /// Whether the pointer is over the scene rather than over a panel. What the
    /// cursor hovers only means something here, so the brush preview asks before
    /// drawing itself.
    /// </summary>
    /// <remarks>
    /// Asked of the driving party's pointer and answered geometrically, rather
    /// than tracked from the engine's own enter and exit signals. Those are
    /// recomputed each frame from the machine's cursor whatever else is going on,
    /// so while an agent held the editor they reported the user walking on and off
    /// the viewport — and the brush believed them.
    /// </remarks>
    public bool IsPointerInside =>
        _input.TryGetPointer(out Vector2 position) && GetGlobalRect().HasPoint(position);

    /// <summary>
    /// Whether the viewport is the panel holding the keyboard. The brush preview
    /// asks as well as the camera: a cell proposed under the cursor while the
    /// hierarchy is the active panel promises an edit the next keystroke will not
    /// make, and the tool cursor going quiet is how the viewport says it is not
    /// the one listening.
    /// </summary>
    public bool HasKeyboard => HasFocus();

    /// <summary>
    /// The viewport is the editor's default keyboard owner: with nothing focused
    /// the arrows would answer to no one, and a 3D editor that ignores them until
    /// the scene is clicked reads as broken rather than as principled.
    /// </summary>
    public override void _Ready() => GrabFocus();

    /// <summary>
    /// The keyboard leaving. Escape is not answered here — the shell takes it in
    /// <c>_Input</c>, ahead of GUI routing, and hands the keyboard back to this
    /// surface itself — so the only route this override owns is the one the
    /// engine reports: focus going elsewhere, or the window going behind.
    /// </summary>
    public override void _Notification(int what)
    {
        // Losing the window is not losing focus — Godot keeps the focused
        // control across an application switch but stops delivering the key
        // releases, so both have to count as handing the keyboard back.
        if ((long)what is NotificationFocusExit or NotificationApplicationFocusOut)
            KeyboardLost?.Invoke();
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Real buttons only: the wheel arrives as a button press too, and a
        // scroll is not a click — the same sentence ViewportFocusGate exists
        // to enforce for activation.
        if (@event is InputEventMouseButton { Pressed: true } pressedButton
            && !ViewportFocusGate.IsWheel(pressedButton.ButtonIndex))
        {
            PointerPressed?.Invoke();
        }

        if (InputHandler == null)
            return;

        // Being pointed at is not being listened to. Until the viewport is the
        // focused panel the only thing it answers is the click that focuses it;
        // a wheel notch, a trackpad pinch or a motion over it belongs to whatever
        // panel the user is actually working in. See ViewportFocusGate.
        if (!HasKeyboard && !TryActivate(@event))
            return;

        // GUI input arrives in control-local coordinates. The transform is the
        // identity while the surface is full-rect and untransformed; going
        // through it anyway is what keeps that a layout detail rather than an
        // assumption baked into every consumer. Events without a position — keys
        // among them — pass through unchanged.
        if (InputHandler(@event.XformedBy(GetGlobalTransform())))
            AcceptEvent();
    }

    /// <summary>
    /// Takes the keyboard for a click that landed on an inactive viewport, and
    /// says whether it did — the same event then goes on to the camera and the
    /// tools, so activating and acting are one gesture rather than two.
    /// </summary>
    /// <remarks>
    /// The engine already does this for the left button before the event is
    /// dispatched, which is why a left click behaves identically either way; what
    /// this adds is the middle and right buttons, which the engine does not focus
    /// on and which are the camera's own.
    /// </remarks>
    private bool TryActivate(InputEvent @event)
    {
        if (@event is not InputEventMouseButton button
            || !ViewportFocusGate.Activates(button.ButtonIndex, button.Pressed))
        {
            return false;
        }

        GrabFocus();

        return true;
    }
}
