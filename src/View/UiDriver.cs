using System.Collections.Generic;
using System.Globalization;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <inheritdoc cref="IUiDriver"/>
/// <remarks>
/// A plain class rather than a node, deliberately: it needs the tree and the
/// window, neither of which is a lifecycle callback, and everything it decides —
/// what to report, how a reference is spelled, where inside a rect to aim — lives
/// in the pure classes beside it (<see cref="UiSnapshotPolicy"/>,
/// <see cref="UiReference"/>, <see cref="UiCoordinates"/>) where a test can reach
/// it. What is left here is the walk itself.
/// </remarks>
internal sealed class UiDriver : IUiDriver
{
    private readonly Node _shell;
    private readonly InjectedInputState _injected;

    private InputInjector? _injector;

    public UiDriver(Node shell, InjectedInputState injected)
    {
        _shell = shell;
        _injected = injected;
    }

    public Vector2I GetWindowSize() => _shell.GetWindow().Size;

    /// <summary>
    /// Built on first use rather than in the constructor: it reads the keymap for
    /// the device a synthesized key has to wear, and the shell is assembled before
    /// the tree it belongs to is running.
    /// </summary>
    private InputInjector Injector =>
        _injector ??= new InputInjector(SyntheticInput.ResolveKeyboardDevice(), _injected, ToCanvas);

    /// <summary>
    /// A point aimed in window pixels, in the canvas units a control's rect and
    /// the camera's rays are measured in. Read live, like every other use of this
    /// transform — it changes with the window's size.
    /// </summary>
    private Vector2 ToCanvas(Vector2 inputPosition) =>
        _shell.GetWindow().GetFinalTransform().AffineInverse() * inputPosition;

    public InputTargetResolution Resolve(string reference, Vector2 offset)
    {
        // Invisible controls are read in on purpose: a target that is there but
        // hidden deserves to be refused by name, not reported as unknown. The
        // decision itself is InputTargeting's, over what this found.
        UiSnapshot snapshot = ReadControls(
            new UiSnapshotOptions(string.Empty, int.MaxValue, IncludeInvisible: true));

        return InputTargeting.Resolve(snapshot.Controls, reference, offset);
    }

    public void Move(Vector2 point) => Injector.Move(Clamp(point));

    public void Click(
        Vector2 point,
        MouseButton button,
        InputModifiers modifiers,
        int clicks,
        HoldAction action) =>
        Injector.Click(Clamp(point), button, modifiers, clicks, action);

    public void Drag(
        Vector2 from,
        Vector2 to,
        int steps,
        MouseButton button,
        InputModifiers modifiers) =>
        Injector.Drag(Clamp(from), Clamp(to), steps, button, modifiers);

    public void Key(Key key, HoldAction action, InputModifiers modifiers) =>
        Injector.Key(key, action, modifiers);

    public void TypeText(string text) => Injector.TypeText(text);

    public void Wheel(Vector2 point, int notches, InputModifiers modifiers) =>
        Injector.Wheel(Clamp(point), notches, modifiers);

    public void Pan(Vector2 point, Vector2 delta, int steps, InputModifiers modifiers) =>
        Injector.Pan(Clamp(point), delta, steps, modifiers);

    public void Magnify(Vector2 point, float factor, int steps, InputModifiers modifiers) =>
        Injector.Magnify(Clamp(point), factor, steps, modifiers);

    public bool Step() => _injector?.Step() ?? false;

    /// <summary>
    /// Into the window, silently. A point outside it is a caller's arithmetic
    /// being off, and the edge is the nearest place the gesture could have meant —
    /// the same bargain the model makes with a transform.
    /// </summary>
    private Vector2 Clamp(Vector2 point) => InputLimits.ClampPoint(point, GetWindowSize());

    public UiSnapshot ReadControls(UiSnapshotOptions options)
    {
        var controlsList = new List<UiControl>();
        var total = 0;

        // Read every time, never cached: the window's stretch changes with its
        // size, and a stale transform is a click in the wrong place.
        Transform2D canvasToInput = _shell.GetWindow().GetFinalTransform();

        Walk(_shell, string.Empty, Vector2.Zero, 0, canvasToInput, clip: null, options, controlsList, ref total);

        return new UiSnapshot(controlsList, total, UiSnapshotPolicy.MaximumControls);
    }

    /// <summary>
    /// Depth first in child order, which within one window is draw order — so a
    /// control added later is drawn over the ones before it. Across windows it is
    /// not, which is what <c>layer</c> carries: a popup is a child of whichever
    /// button owns it and is drawn over everything regardless.
    /// </summary>
    /// <remarks>
    /// <paramref name="clip"/> is the running intersection of every clipping
    /// ancestor's rect, and each control is reported clipped to it: the rect an
    /// agent aims into is the part a click can actually reach, and a control
    /// scrolled wholly out of view collapses to a zero rect the targeting
    /// refuses by name. Scrollable panel bodies made this routine — the old
    /// full-height docks never clipped anything.
    /// </remarks>
    private static void Walk(
        Node node,
        string reference,
        Vector2 windowOffset,
        int layer,
        Transform2D canvasToInput,
        Rect2? clip,
        UiSnapshotOptions options,
        List<UiControl> controlsList,
        ref int total)
    {
        var index = 0;

        // Internal children included: a menu button's popup, a spin box's own
        // field and steppers, a list's scrollbars are all things a user clicks,
        // and a snapshot that left them out would say the interface has parts it
        // does not. They are also where several widgets keep everything
        // clickable about themselves.
        foreach (Node child in node.GetChildren(includeInternal: true))
        {
            string childReference = UiReference.Append(
                reference,
                UiReference.ComposeSegment(child.Name.ToString(), child.GetType().Name, index));

            index++;

            if (!UiSnapshotPolicy.ShouldDescend(childReference, options))
                continue;

            Rect2? childClip = clip;

            if (TryDescribe(child, childReference, windowOffset, layer, canvasToInput, out UiControl described))
            {
                if (clip is Rect2 clipRect)
                    described = described with { Rect = described.Rect.Intersection(clipRect) };

                if (child is Control { ClipContents: true })
                    childClip = described.Rect;

                if (UiSnapshotPolicy.ShouldReport(childReference, described.Visible, options)
                    && UiSnapshotPolicy.MatchesSearch(
                        childReference, described.Text, described.Type, options))
                {
                    total++;

                    if (controlsList.Count < UiSnapshotPolicy.MaximumControls)
                        controlsList.Add(described);
                }
            }

            if (child is Window window)
            {
                // A window the engine draws inside the main one puts its contents
                // in the same pixels as everything else, once its own corner is
                // accounted for. One the platform gave a window of its own does
                // not, and nothing here can aim at it — see the native file
                // dialog, which is the same trap. Popups draw over every clip.
                if (window.IsEmbedded())
                {
                    Walk(
                        window,
                        childReference,
                        windowOffset + window.Position,
                        layer + 1,
                        canvasToInput,
                        clip: null,
                        options,
                        controlsList,
                        ref total);
                }

                continue;
            }

            Walk(
                child,
                childReference,
                windowOffset,
                layer,
                canvasToInput,
                childClip,
                options,
                controlsList,
                ref total);
        }
    }

    /// <summary>
    /// What one node looks like to an agent, or nothing at all — most of the tree
    /// is neither a control nor a window and has no place on screen to report.
    /// </summary>
    private static bool TryDescribe(
        Node node,
        string reference,
        Vector2 windowOffset,
        int layer,
        Transform2D canvasToInput,
        out UiControl control)
    {
        switch (node)
        {
            case Control widget:
            {
                Rect2 canvasRect = widget.GetGlobalRect();

                control = new UiControl(
                    reference,
                    widget.GetType().Name,
                    ReadText(widget),
                    UiCoordinates.ToInput(
                        new Rect2(canvasRect.Position + windowOffset, canvasRect.Size),
                        canvasToInput),
                    widget.IsVisibleInTree(),
                    IsDisabled(widget),
                    widget.HasFocus(),
                    // NOTE: The handover dimmer stops the machine's mouse but
                    // never the agent's aim — its hit test opens under the
                    // injected pointer, wherever the aim lands. The snapshot is
                    // the aiming channel, so reporting it as a mouse-taker
                    // would refuse every dimmed click that would in fact land.
                    widget.MouseFilter == Control.MouseFilterEnum.Stop
                        && widget is not AgentDimmer,
                    layer);

                return true;
            }

            case Window window:
            {
                // An unembedded window is reported so an agent can see that it is
                // up, with no rect — which is also what refuses it as a target,
                // since a zero-sized control cannot be aimed at.
                Rect2 rect = window.IsEmbedded()
                    ? UiCoordinates.ToInput(
                        new Rect2(windowOffset + window.Position, window.Size),
                        canvasToInput)
                    : new Rect2();

                control = new UiControl(
                    reference,
                    window.GetType().Name,
                    window.Title,
                    rect,
                    window.Visible,
                    Disabled: false,
                    Focused: false,
                    TakesMouse: window.IsEmbedded(),
                    Layer: layer + 1);

                return true;
            }

            default:
                control = default;

                return false;
        }
    }

    /// <summary>
    /// Whatever the control shows in words. A spin box shows a number, which is
    /// the one thing worth reading back off it — its own text field is an internal
    /// child the walk never reaches.
    /// </summary>
    private static string ReadText(Control widget) => widget switch
    {
        Button button => button.Text,
        Label label => label.Text,
        LineEdit line => line.Text,
        SpinBox spin => spin.Value.ToString(CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    /// <summary>
    /// Whether it would refuse to be interacted with. Broader than a button's own
    /// flag, because an uneditable field refuses in exactly the same way and for
    /// the same reason.
    /// </summary>
    private static bool IsDisabled(Control widget) => widget switch
    {
        BaseButton button => button.Disabled,
        LineEdit line => !line.Editable,
        SpinBox spin => !spin.Editable,
        _ => false
    };
}
