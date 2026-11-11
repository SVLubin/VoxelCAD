using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The editor's own interface, as something to read and — later — to drive. A
/// debug and self-test instrument: it exists so the editor can exercise its own
/// buttons, handles and viewport, which is the one thing no semantic command can
/// stand in for.
/// </summary>
/// <remarks>
/// It is emphatically <em>not</em> a way to work. Placing voxels is
/// <c>set-voxels</c>, moving an element is <c>set-transform</c>; driving the
/// interface to reach either would be an agent pretending to be a mouse, and
/// would quietly rot the surface everything else is built on.
/// </remarks>
public interface IUiDriver
{
    /// <summary>
    /// The controls of the editor chrome, in draw order, with their rects in input
    /// coordinates. This is the aiming channel: a reference from here survives a
    /// relayout, and a pixel read off a screenshot does not.
    /// </summary>
    UiSnapshot ReadControls(UiSnapshotOptions options);

    /// <summary>
    /// The window in input coordinates — what a control's rect is measured
    /// against, and what a point to aim at is clamped to.
    /// </summary>
    Vector2I GetWindowSize();

    /// <summary>
    /// Where a reference lands, or why it is nowhere to aim. The offset runs 0..1
    /// across the control's rect; its centre is the default.
    /// </summary>
    InputTargetResolution Resolve(string reference, Vector2 offset);

    /// <summary>Moves the pointer, and nothing else.</summary>
    void Move(Vector2 point);

    /// <summary>
    /// A click, or half of one: <see cref="HoldAction.Press"/> leaves the button down
    /// and the gesture in flight, so whatever the editor shows while a handle is held
    /// can be read before <see cref="HoldAction.Release"/> ends it.
    /// </summary>
    void Click(
        Vector2 point,
        MouseButton button,
        InputModifiers modifiers,
        int clicks,
        HoldAction action);

    /// <summary>
    /// Press, <paramref name="steps"/> motions one frame apart, release. The
    /// frames are what make it a drag rather than a teleport.
    /// </summary>
    void Drag(Vector2 from, Vector2 to, int steps, MouseButton button, InputModifiers modifiers);

    void Key(Key key, HoldAction action, InputModifiers modifiers);

    void TypeText(string text);

    void Wheel(Vector2 point, int notches, InputModifiers modifiers);

    /// <summary>A trackpad's two-finger scroll, carrying a precise delta.</summary>
    void Pan(Vector2 point, Vector2 delta, int steps, InputModifiers modifiers);

    /// <summary>A trackpad's pinch, as the factor it multiplies the size by.</summary>
    void Magnify(Vector2 point, float factor, int steps, InputModifiers modifiers);

    /// <summary>
    /// Advances the gesture in flight by one frame. False once idle — which is
    /// what tells the queue that the command which started it may now answer, and
    /// that whatever is behind it may run.
    /// </summary>
    bool Step();
}
