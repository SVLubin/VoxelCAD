using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The devices in front of the machine: Godot's own input singleton and the
/// viewport's pointer. What the tools used to read directly, now behind the seam
/// so it can be swapped for the agent's.
/// </summary>
/// <remarks>
/// A five-line adapter with nothing to decide, which is deliberate — every
/// decision belongs to <see cref="EditorInputState"/> and to the sources that can
/// be built without an engine.
/// </remarks>
internal sealed class PhysicalInputState : IInputState
{
    private readonly Node _context;

    public PhysicalInputState(Node context)
    {
        _context = context;
    }

    /// <summary>
    /// Always somewhere: a physical pointer exists even when it is off the window,
    /// and "off the window" is a question about a rect, not about existence.
    /// </summary>
    public bool TryGetPointer(out Vector2 position)
    {
        position = _context.GetViewport().GetMousePosition();

        return true;
    }

    public bool IsKeyDown(Key key) => Input.IsKeyPressed(key);

    public bool IsButtonDown(MouseButton button) => Input.IsMouseButtonPressed(button);
}
