using System.Collections.Generic;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// What the agent's own pointer and keys are doing, kept from the events the
/// injector parses. The agent's counterpart to the machine's devices.
/// </summary>
/// <remarks>
/// It is derived rather than declared: the injector is the only thing that knows
/// what it is about to send, so it says so as it sends it, and nothing here has to
/// be kept in step with the gesture builders by hand.
/// <para>
/// The pointer is absent until the agent moves one. That is not a corner case to
/// paper over — it is the correct answer for an agent that has taken control and
/// only issued semantic commands, and it is what stops the brush offering a cell
/// under a pointer that does not exist.
/// </para>
/// </remarks>
internal sealed class InjectedInputState : IInputState
{
    private readonly HashSet<Key> _heldKeysHashSet = new();
    private readonly HashSet<MouseButton> _heldButtonsHashSet = new();

    private Vector2? _pointer;

    /// <summary>
    /// Whether injected events are on their way to the engine right now — from
    /// the moment a gesture is built until its frames have drained, settle
    /// frames included, which is exactly the window its events are dispatched
    /// in. The handover dimmer opens only while this is true, so between
    /// gestures it is solid glass with no hole at all.
    /// </summary>
    /// <remarks>
    /// Separate from the pointer on purpose: the pointer must persist between
    /// gestures, because the brush's preview is polled every frame and an agent
    /// that moved its pointer and then stopped should still see what a click
    /// would do. The hole must not.
    /// </remarks>
    public bool IsGestureInFlight { get; private set; }

    public bool TryGetPointer(out Vector2 position)
    {
        position = _pointer ?? Vector2.Zero;

        return _pointer.HasValue;
    }

    public bool IsKeyDown(Key key) => _heldKeysHashSet.Contains(key);

    public bool IsButtonDown(MouseButton button) => _heldButtonsHashSet.Contains(button);

    /// <summary>In the canvas coordinates the tools read, not the window pixels it was aimed in.</summary>
    public void NotePointer(Vector2 position) => _pointer = position;

    /// <summary>Whether a gesture's frames are still queued or draining.</summary>
    public void NoteGesture(bool inFlight) => IsGestureInFlight = inFlight;

    public void NoteKey(Key key, bool pressed) => Note(_heldKeysHashSet, key, pressed);

    public void NoteButton(MouseButton button, bool pressed) =>
        Note(_heldButtonsHashSet, button, pressed);

    /// <summary>
    /// Forgets everything, which is what a fresh handover deserves: a pointer left
    /// over from the last agent's session would put a preview on screen that
    /// nobody had asked for.
    /// </summary>
    public void Clear()
    {
        _heldKeysHashSet.Clear();
        _heldButtonsHashSet.Clear();
        _pointer = null;
        IsGestureInFlight = false;
    }

    private static void Note<T>(HashSet<T> heldHashSet, T value, bool pressed)
    {
        if (pressed)
            heldHashSet.Add(value);
        else
            heldHashSet.Remove(value);
    }
}
