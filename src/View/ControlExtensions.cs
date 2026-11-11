using Godot;

namespace VoxelCAD.View;

/// <summary>Sizing and naming helpers, so layout stays readable inline with <see cref="Ui"/>.</summary>
internal static class ControlExtensions
{
    public static T Expand<T>(this T control, bool horizontal = true, bool vertical = true)
        where T : Control
    {
        if (horizontal)
            control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        if (vertical)
            control.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        return control;
    }

    public static T MinSize<T>(this T control, float width, float height) where T : Control
    {
        control.CustomMinimumSize = new Vector2(width, height);

        return control;
    }

    public static T Named<T>(this T node, string name) where T : Node
    {
        node.Name = name;

        return node;
    }

    /// <summary>
    /// Marks a control a mouse target rather than a keyboard stop: clicking it
    /// runs its action without taking the keyboard away from whoever was using
    /// it. For a button that is what the user already expects — pressing Undo is
    /// not a request to start typing at the toolbar — and without it the arrow
    /// keys turn into a walk across the buttons, because an arrow no control
    /// claims is read by the engine as "move focus".
    /// </summary>
    public static T MouseOnly<T>(this T control) where T : Control
    {
        control.FocusMode = Control.FocusModeEnum.None;

        return control;
    }

    /// <summary>
    /// Makes the control a wall for arrow-key focus movement: walking up a list
    /// reaches its top row and stops there instead of continuing into whatever
    /// panel happens to sit above it on screen.
    /// </summary>
    /// <remarks>
    /// A focus neighbour pointing at the control itself is the engine's own way
    /// to say this, and it is the only way that works from outside the widget.
    /// Key input reaches the focused control and nothing else — it does not walk
    /// up the ancestors the way mouse input does — so a container cannot watch
    /// for an arrow its contents declined and veto the move. By the time anything
    /// above could look, the focus has already gone. Measured, after building the
    /// container that cannot work.
    ///
    /// This does not stop the arrows being useful inside the control: whatever it
    /// consumes for its own navigation never gets this far.
    /// </remarks>
    public static T ContainsArrowFocus<T>(this T control) where T : Control
    {
        foreach (Side side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
            control.SetFocusNeighbor(side, ".");

        return control;
    }
}
