using System;
using System.Collections.Generic;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// Turns a gesture into the events the engine would have received had a hand made
/// it, spread over the frames a hand would have taken.
/// </summary>
/// <remarks>
/// The frames are the point. Godot accumulates input, so several motion events
/// parsed in one frame arrive as one — a drag squeezed into a single frame is a
/// teleport, and it exercises none of the per-frame logic a drag is meant to
/// exercise. Measured: four motions in a frame arrive as one; four across four
/// frames arrive as four, each a frame after it was parsed.
/// <para>
/// Everything goes through <c>Input.ParseInputEvent</c> rather than
/// <c>Viewport.PushInput</c>: it is the path a real event takes, so the window
/// dispatch, the InputMap and the accumulation all behave as they do for a hand.
/// A pushed event skips <c>Input</c> entirely, which leaves every part of the
/// engine that reads device state disagreeing with the events being delivered.
/// </para>
/// <para>
/// The editor's own tools no longer read that state directly — they read
/// <see cref="IInputState"/>, which is what this fills in as it sends. That is
/// not a duplicate of what <c>Input</c> already knows: <c>Input</c> answers for
/// the machine, and while an agent holds the editor the tools must answer for
/// the agent. Before the split, a hand resting on Shift in front of the machine
/// flipped the agent's brush to erase.
/// </para>
/// <para>
/// Modifiers are genuinely held for the length of a gesture, as key events, and
/// also flagged on each event: the editor reads them both ways, and half a
/// modifier is worse than none.
/// </para>
/// </remarks>
internal sealed class InputInjector
{
    /// <summary>
    /// Empty frames appended to every gesture. One so the last real frame's
    /// events are delivered — a parsed event arrives the frame after — and one so
    /// the <c>_Process</c> that reacts to it has run before the command that
    /// started the gesture reports back.
    /// </summary>
    private const int SettleFrames = 2;

    /// <summary>The buttons a gesture can hold down; a wheel notch holds nothing.</summary>
    private static readonly MouseButton[] HoldableButtons =
    {
        MouseButton.Left, MouseButton.Right, MouseButton.Middle
    };

    private readonly Queue<List<InputEvent>> _framesQueue = new();
    private readonly int _keyboardDevice;
    private readonly InjectedInputState _state;
    private readonly Func<Vector2, Vector2> _toCanvas;

    /// <summary>
    /// Where the last motion was aimed, so the next one can say how far it moved.
    /// Composed as the events are built rather than as they are sent: a gesture is
    /// one path, and the deltas along it have to add up to it whatever order the
    /// frames drain in.
    /// </summary>
    private Vector2 _lastPoint;

    /// <param name="state">
    /// Told what the agent's pointer and keys are doing, as they are sent. The
    /// tools poll that rather than the machine's devices while an agent holds the
    /// editor, so it has to be derived here — this is the only place that knows.
    /// </param>
    /// <param name="toCanvas">
    /// Input coordinates into the canvas ones the tools read. The two differ by
    /// the window's stretch, which has been measured at 2.58.
    /// </param>
    public InputInjector(int keyboardDevice, InjectedInputState state, Func<Vector2, Vector2> toCanvas)
    {
        _keyboardDevice = keyboardDevice;
        _state = state;
        _toCanvas = toCanvas;
    }

    /// <summary>Whether a gesture is still in flight.</summary>
    public bool IsBusy => _framesQueue.Count > 0;

    /// <summary>
    /// Parses one frame's worth of events. False once there is nothing left, which
    /// is what releases the command that started the gesture.
    /// </summary>
    public bool Step()
    {
        if (_framesQueue.Count == 0)
            return false;

        foreach (InputEvent @event in _framesQueue.Dequeue())
        {
            Record(@event);

            Input.ParseInputEvent(@event);
        }

        // The settle frames are what make it safe to drop the flag here: a
        // parsed event arrives the frame after, so the last real event has
        // already been dispatched by the time the queue empties.
        if (_framesQueue.Count == 0)
            _state.NoteGesture(false);

        return _framesQueue.Count > 0;
    }

    /// <summary>
    /// What this event says the agent's pointer and keys are now doing. Recorded
    /// as the event is sent rather than when the gesture was built, so the state
    /// tracks the drag frame by frame instead of jumping to where it will end.
    /// </summary>
    private void Record(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton button:
                _state.NotePointer(_toCanvas(button.Position));

                // A wheel notch is a press and a release of nothing anybody holds.
                if (button.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
                    _state.NoteButton(button.ButtonIndex, button.Pressed);

                break;

            case InputEventMouseMotion motion:
                _state.NotePointer(_toCanvas(motion.Position));

                break;

            // Qualified because this class has a Key method of its own.
            case InputEventKey { Keycode: not Godot.Key.None } key:
                _state.NoteKey(key.Keycode, key.Pressed);

                break;
        }
    }

    /// <summary>
    /// Moves the pointer, carrying whichever buttons the agent is actually holding.
    /// That matters once a press can outlive the command that made it: a motion
    /// between a held press and its release is a frame of a drag, and an event that
    /// claimed no button was down would be describing a different gesture.
    /// </summary>
    public void Move(Vector2 point)
    {
        Frame(Motion(point, GetHeldMask(), InputModifiers.None));
        Settle();
    }

    /// <summary>
    /// Pointer there, a frame for the editor to notice, then the press and the
    /// release. The idle frame is not padding: the brush only places what its
    /// preview already offered, and the preview is computed in <c>_Process</c> —
    /// so a click with no frame between arriving and pressing is a click that
    /// paints nothing.
    /// </summary>
    /// <remarks>
    /// <paramref name="action"/> splits the pair when a gesture has to be left in
    /// flight — the mouse's half of what <see cref="Key"/> already does for held
    /// keys, and the only way anything shown <em>during</em> a drag can be looked at
    /// from here. A held press keeps its modifiers down with it, for the same reason
    /// a held key does; the release drops them.
    /// </remarks>
    public void Click(
        Vector2 point,
        MouseButton button,
        InputModifiers modifiers,
        int clicks,
        HoldAction action = HoldAction.Tap)
    {
        HoldModifiers(modifiers);

        // A release aims where the button comes up, and the button is already down:
        // the mask says so, and the motion is the last frame of the drag.
        Frame(Motion(point, action == HoldAction.Release ? GetHeldMask() : (MouseButtonMask)0, modifiers));
        Frame();

        if (action == HoldAction.Release)
        {
            Frame(Button(point, button, pressed: false, modifiers));
            ReleaseModifiers(modifiers);
            Settle();

            return;
        }

        if (action == HoldAction.Press)
        {
            Frame(Button(point, button, pressed: true, modifiers));
            Settle();

            return;
        }

        for (var click = 0; click < InputLimits.ClampClicks(clicks); click++)
        {
            // The engine never derives a double click from timing — the flag
            // arrives on the OS event or not at all. Without it a synthesized
            // pair is two single clicks, and a Tree row can never be activated.
            Frame(Button(point, button, pressed: true, modifiers, doubleClick: click > 0));
            Frame(Button(point, button, pressed: false, modifiers));
        }

        ReleaseModifiers(modifiers);
        Settle();
    }

    /// <summary>
    /// Press where the drag starts, one motion per frame along the way, release at
    /// the end. The path ends exactly on the target rather than near it, because a
    /// handle dragged to "almost there" is a transform nobody asked for.
    /// </summary>
    public void Drag(
        Vector2 from,
        Vector2 to,
        int steps,
        MouseButton button,
        InputModifiers modifiers)
    {
        MouseButtonMask mask = ToMask(button);

        HoldModifiers(modifiers);
        Frame(Motion(from, (MouseButtonMask)0, modifiers));
        Frame();
        Frame(Button(from, button, pressed: true, modifiers));

        foreach (Vector2 point in InputLimits.ComposePath(from, to, steps))
            Frame(Motion(point, mask, modifiers));

        Frame(Button(to, button, pressed: false, modifiers));
        ReleaseModifiers(modifiers);
        Settle();
    }

    public void Key(Key key, HoldAction action, InputModifiers modifiers)
    {
        HoldModifiers(modifiers);

        if (action != HoldAction.Release)
            Frame(KeyEvent(key, pressed: true, modifiers));

        if (action != HoldAction.Press)
            Frame(KeyEvent(key, pressed: false, modifiers));

        // A key meant to stay down keeps its modifiers down with it; anything
        // else would release Shift out from under a held Shift+key.
        if (action != HoldAction.Press)
            ReleaseModifiers(modifiers);

        Settle();
    }

    /// <summary>
    /// A whole string in one frame. Keys are not accumulated the way motion is, so
    /// they all arrive, in order — and a field filled a character per frame would
    /// take four seconds over a path nobody is watching.
    /// </summary>
    public void TypeText(string text)
    {
        var eventsList = new List<InputEvent>();

        foreach (char character in text)
        {
            eventsList.Add(Character(character, pressed: true));
            eventsList.Add(Character(character, pressed: false));
        }

        Frame(eventsList);
        Settle();
    }

    /// <summary>
    /// One notch per frame. The camera's zoom is exponential and per-notch, so
    /// notches delivered together would be indistinguishable from one large one —
    /// and would not exercise the path a wheel actually takes.
    /// </summary>
    public void Wheel(Vector2 point, int notches, InputModifiers modifiers)
    {
        int count = InputLimits.ClampNotches(notches);
        MouseButton button = count >= 0 ? MouseButton.WheelUp : MouseButton.WheelDown;

        HoldModifiers(modifiers);
        Frame(Motion(point, (MouseButtonMask)0, modifiers));

        for (var notch = 0; notch < Mathf.Abs(count); notch++)
        {
            // A wheel notch is a press and a release in the same breath; that is
            // how the engine reports a real one.
            Frame(
                Button(point, button, pressed: true, modifiers),
                Button(point, button, pressed: false, modifiers));
        }

        ReleaseModifiers(modifiers);
        Settle();
    }

    /// <summary>
    /// A trackpad's two-finger scroll, as the engine reports one: a pan gesture
    /// carrying a precise delta rather than a wheel notch.
    /// </summary>
    /// <remarks>
    /// It exists because without it the trackpad path could not be exercised by
    /// anything in this repository — the injector spoke only in notches, so gesture
    /// handling was code nothing could drive, which is how the panels came by their
    /// own unverifiable corners. A hand is still the only instrument that can say
    /// whether a <em>real</em> trackpad feels right; this one says whether the code
    /// behind it runs at all.
    /// <para>
    /// Delivered a step at a time like every other gesture, because Godot
    /// accumulates input and a whole scroll in one frame is a teleport that
    /// exercises none of the per-frame logic a scroll is meant to exercise.
    /// </para>
    /// </remarks>
    public void Pan(Vector2 point, Vector2 delta, int steps, InputModifiers modifiers)
    {
        int count = InputLimits.ClampSteps(steps);

        HoldModifiers(modifiers);
        Frame(Motion(point, (MouseButtonMask)0, modifiers));

        for (var step = 0; step < count; step++)
        {
            Frame(SyntheticInput.Tag(ApplyModifiers(
                new InputEventPanGesture
                {
                    Device = _keyboardDevice,
                    Position = point,
                    Delta = delta / count
                },
                modifiers)));
        }

        ReleaseModifiers(modifiers);
        Settle();
    }

    /// <summary>
    /// A trackpad's pinch. The factor is what the gesture multiplies the size by
    /// over the whole of it, spread across the steps the way the engine spreads a
    /// real one.
    /// </summary>
    public void Magnify(Vector2 point, float factor, int steps, InputModifiers modifiers)
    {
        int count = InputLimits.ClampSteps(steps);
        float each = Mathf.Pow(Mathf.Max(factor, 0.01f), 1f / count);

        HoldModifiers(modifiers);
        Frame(Motion(point, (MouseButtonMask)0, modifiers));

        for (var step = 0; step < count; step++)
        {
            Frame(SyntheticInput.Tag(ApplyModifiers(
                new InputEventMagnifyGesture
                {
                    Device = _keyboardDevice,
                    Position = point,
                    Factor = each
                },
                modifiers)));
        }

        ReleaseModifiers(modifiers);
        Settle();
    }

    // --- frames ---

    /// <summary>
    /// Every frame of every gesture goes through here, which is why the
    /// in-flight flag is raised here rather than in each gesture builder: a new
    /// kind of gesture cannot forget to raise it.
    /// </summary>
    private void Frame(params InputEvent[] events) => Frame(new List<InputEvent>(events));

    private void Frame(List<InputEvent> events)
    {
        _framesQueue.Enqueue(events);
        _state.NoteGesture(true);
    }

    private void Settle()
    {
        for (var frame = 0; frame < SettleFrames; frame++)
            Frame();
    }

    private void HoldModifiers(InputModifiers modifiers) => ModifierFrame(modifiers, pressed: true);

    private void ReleaseModifiers(InputModifiers modifiers) => ModifierFrame(modifiers, pressed: false);

    /// <summary>
    /// The modifier keys of a gesture, down or up together. Nothing at all when
    /// there are none, so a plain click does not spend two frames on an empty
    /// frame each end.
    /// </summary>
    private void ModifierFrame(InputModifiers modifiers, bool pressed)
    {
        var eventsList = new List<InputEvent>();

        foreach (Key key in ModifierKeys(modifiers))
            eventsList.Add(KeyEvent(key, pressed, InputModifiers.None));

        if (eventsList.Count > 0)
            Frame(eventsList);
    }

    private static IEnumerable<Key> ModifierKeys(InputModifiers modifiers)
    {
        if (modifiers.HasFlag(InputModifiers.Shift))
            yield return Godot.Key.Shift;

        if (modifiers.HasFlag(InputModifiers.Alt))
            yield return Godot.Key.Alt;

        if (modifiers.HasFlag(InputModifiers.Ctrl))
            yield return Godot.Key.Ctrl;

        if (modifiers.HasFlag(InputModifiers.Meta))
            yield return Godot.Key.Meta;
    }

    // --- events ---

    /// <summary>
    /// A motion, carrying how far it moved as well as where it landed.
    /// </summary>
    /// <remarks>
    /// <c>Relative</c> is not decoration. Godot starts a GUI drag by accumulating
    /// the <em>relative</em> motion while a button is held and comparing it to a
    /// threshold — so with a zero delta on every frame the accumulator never grows,
    /// <c>_GetDragData</c> is never asked, and drag and drop is simply unreachable
    /// from here however far the pointer is dragged. Measured exactly that way on
    /// the project tree: the rows highlighted, the press and the release landed, and
    /// nothing was ever dropped.
    /// <para>
    /// It is composed here, from the pointer's last injected position, because this
    /// is the only place that knows where the agent's pointer was — the machine's
    /// is somebody else's.
    /// </para>
    /// </remarks>
    private InputEventMouseMotion Motion(Vector2 point, MouseButtonMask mask, InputModifiers modifiers)
    {
        Vector2 relative = point - _lastPoint;
        _lastPoint = point;

        var motion = new InputEventMouseMotion
        {
            Device = _keyboardDevice,
            Position = point,
            GlobalPosition = point,
            Relative = relative,
            ScreenRelative = relative,
            ButtonMask = mask
        };

        return SyntheticInput.Tag(ApplyModifiers(motion, modifiers));
    }

    private InputEventMouseButton Button(
        Vector2 point,
        MouseButton button,
        bool pressed,
        InputModifiers modifiers,
        bool doubleClick = false)
    {
        var click = new InputEventMouseButton
        {
            Device = _keyboardDevice,
            ButtonIndex = button,
            Pressed = pressed,
            Position = point,
            GlobalPosition = point,
            ButtonMask = pressed ? ToMask(button) : (MouseButtonMask)0,
            DoubleClick = doubleClick
        };

        return SyntheticInput.Tag(ApplyModifiers(click, modifiers));
    }

    /// <summary>
    /// Both keycodes are set, and both matter: <c>Input.IsKeyPressed</c> reads the
    /// keycode, while the InputMap bindings in this project were recorded against
    /// physical keys. Setting one would work for half the editor.
    /// </summary>
    private InputEventKey KeyEvent(Key key, bool pressed, InputModifiers modifiers)
    {
        var @event = new InputEventKey
        {
            Device = _keyboardDevice,
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = pressed
        };

        return SyntheticInput.Tag(ApplyModifiers(@event, modifiers));
    }

    /// <summary>
    /// A character rather than a key: a text field reads the unicode off the event
    /// and never asks which key produced it, so there is no keycode to invent.
    /// </summary>
    private InputEventKey Character(char character, bool pressed)
    {
        return SyntheticInput.Tag(new InputEventKey
        {
            Device = _keyboardDevice,
            Keycode = Godot.Key.None,
            PhysicalKeycode = Godot.Key.None,
            Unicode = character,
            Pressed = pressed
        });
    }

    private static T ApplyModifiers<T>(T @event, InputModifiers modifiers)
        where T : InputEventWithModifiers
    {
        @event.ShiftPressed = modifiers.HasFlag(InputModifiers.Shift);
        @event.AltPressed = modifiers.HasFlag(InputModifiers.Alt);
        @event.CtrlPressed = modifiers.HasFlag(InputModifiers.Ctrl);
        @event.MetaPressed = modifiers.HasFlag(InputModifiers.Meta);

        return @event;
    }

    /// <summary>
    /// The mask of whatever the agent is holding right now, read back off the state
    /// the injector itself fills in. Asked rather than remembered, because a press
    /// and the motions that follow it are now separate commands and there is nothing
    /// spanning them to remember it in.
    /// </summary>
    private MouseButtonMask GetHeldMask()
    {
        var mask = (MouseButtonMask)0;

        foreach (MouseButton button in HoldableButtons)
        {
            if (_state.IsButtonDown(button))
                mask |= ToMask(button);
        }

        return mask;
    }

    private static MouseButtonMask ToMask(MouseButton button) => button switch
    {
        MouseButton.Left => MouseButtonMask.Left,
        MouseButton.Right => MouseButtonMask.Right,
        MouseButton.Middle => MouseButtonMask.Middle,
        _ => (MouseButtonMask)0
    };
}
