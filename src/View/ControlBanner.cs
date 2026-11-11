using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The overlay that says who holds the editor. It also does the suspending: while
/// an agent works, a dimmer covers the panels and every event the user makes is
/// swallowed, so they watch rather than edit. Its own button stays live on top of
/// that, and so does anything the agent injects — see <see cref="_Input"/>.
/// </summary>
/// <remarks>
/// The plaque reads like a badge: the robot on the left, the agent's name as the
/// title, and under it — in the agent's own words — what it is doing, from the
/// <c>topic</c> it announced with <c>take-control</c> and updates with
/// <c>report-topic</c>. The clock and the one button sit to the right. All
/// wording for the phases lives here, next to the labels that show it; the
/// presenter sends state, not sentences.
/// </remarks>
internal sealed partial class ControlBanner : Control
{
    private const float PlaqueWidth = 400f;

    /// <summary>The robot's side, in canvas units — a badge, not a row icon.</summary>
    private const float IconSide = 30f;

    /// <summary>How far the plaque rises as it goes, and drops in as it comes.</summary>
    private const float PlaqueRise = 10f;

    /// <summary>Where it settles, measured from the top of the window.</summary>
    private const float PlaqueTop = 12f;

    /// <summary>
    /// How lit the gold is while a request is only pending. The held ring
    /// fading in, which is what the countdown has always meant — one colour at
    /// two strengths rather than two colours to tell apart.
    /// </summary>
    private const float PendingRing = 0.5f;

    private readonly MotionTiming _timing;
    private readonly AgentDimmer _dimmer;

    /// <summary>
    /// What the plaque slides in. A wrapper rather than the plaque itself,
    /// because the plaque is anchored to the top centre and writing a
    /// <c>Position</c> on an anchored control re-derives its offsets from the
    /// parent's origin rather than from its anchor — which threw it two hundred
    /// units off the left of the window, once per frame. The wrapper spans the
    /// banner, so its own anchors make a position mean what it says.
    /// </summary>
    private readonly Control _plaqueSlot = new() { Name = "PlaqueSlot" };
    private readonly PanelContainer _plaque = new();
    private readonly AgentFace _face;

    /// <summary>
    /// The plaque coming and going, the gold's own strength, and the dimmer
    /// falling over the interface. Three numbers rather than three visibilities:
    /// what the banner says is that the editor has changed hands, and a thing
    /// that important appearing in one frame reads as a glitch.
    /// </summary>
    private readonly Smoothed _plaqueFade;
    private readonly Smoothed _ringLit;
    private readonly Smoothed _dimFade;
    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Label _queued = new();
    private readonly Label _clock = new();
    private readonly Button _action;

    private Color _subtitleColor;
    private Color _warningColor;

    /// <summary>
    /// The plaque's own frame and the gold it is drawn in. Owned rather than
    /// taken from the theme each time, because its border colour is animated and
    /// a theme resource is shared — writing to one would repaint every other
    /// thing that asked for it.
    /// </summary>
    private StyleBoxFlat? _ringStyle;
    private Color _ringColor;
    private Color _dimColor;

    /// <summary>
    /// What is on the plaque now, so a repaint can put it back. The banner is
    /// told its state rather than deriving it, so nothing else could.
    /// </summary>
    private ControlBannerState _state;

    private bool _holdsControl;

    public ControlBanner(InjectedInputState injected, MotionTiming timing)
    {
        _timing = timing;
        _plaqueFade = new Smoothed(MotionSpeed.Opacity, timing);
        _ringLit = new Smoothed(MotionSpeed.Opacity, timing);
        _dimFade = new Smoothed(MotionSpeed.Opacity, timing);
        _face = new AgentFace(timing, IconSide);

        Name = "ControlBanner";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Paint, and the machine's mouse-over with it. Events are still
        // suspended in _Input, in one place for both devices; what the dimmer
        // stops is the half no event handler can reach — the engine's per-frame
        // hover and the cursor shape that follows it — while staying open to
        // the agent at the injected pointer alone. See AgentDimmer.
        _dimmer = new AgentDimmer(injected);
        _dimmer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_dimmer);

        // A mouse target like every other button, and here it matters twice: the
        // banner is dismissed the moment this is pressed, so a focused one would
        // vanish holding the keyboard and leave it with nobody.
        _action = new Button().MouseOnly();
        _action.Pressed += () => ActionPressed?.Invoke();

        // The same glass every other surface is made of — see docs/panels.md.
        // The plaque is the one piece of chrome that must read as belonging to
        // the app while suspending it, so it wears the app's own material and
        // says "an agent has this" with its ring alone.
        _plaque.AddChild(new GlassBackdrop());
        _plaque.AddChild(Ui.Margin(BuildPlaqueContent(), 10));
        _plaque.SetAnchorsPreset(LayoutPreset.CenterTop);
        _plaque.Position = new Vector2(-PlaqueWidth / 2f, PlaqueTop);
        _plaque.CustomMinimumSize = new Vector2(PlaqueWidth, 0f);

        _plaqueSlot.SetAnchorsPreset(LayoutPreset.FullRect);
        _plaqueSlot.MouseFilter = MouseFilterEnum.Ignore;
        _plaqueSlot.AddChild(_plaque);
        AddChild(_plaqueSlot);

        Set(default);
    }

    /// <summary>Cancel during the countdown, or Stop while an agent works — the banner shows one at a time.</summary>
    public event Action? ActionPressed;

    public override void _Ready() => LoadTheme();

    /// <summary>
    /// Re-reads what was cached, because the look can change under it: the theme
    /// is repainted in place when the setting changes, and a colour held in a
    /// field is exactly what a repaint cannot reach. What follows it is the
    /// current state applied again, or the plaque would wear the new colours only
    /// after the next thing that happened to be reported.
    /// </summary>
    public override void _Notification(int what)
    {
        if ((long)what != NotificationThemeChanged || !IsInsideTree())
            return;

        LoadTheme();
        Set(_state);
    }

    private void LoadTheme()
    {
        _dimColor = GetThemeColor("dim_overlay", EditorThemeFactory.SemanticType);

        // Fetched once: the theme is set before anything is shown, and Set()
        // runs every second — it should compare and assign, not walk the theme.
        _subtitleColor = GetThemeColor("text_receded", EditorThemeFactory.SemanticType);
        _warningColor = GetThemeColor("warning", EditorThemeFactory.SemanticType);

        // One frame, drawn in a gold whose strength is the only thing that
        // changes between "somebody is asking" and "somebody has it".
        var ring = GetThemeStylebox("agent_plaque", EditorThemeFactory.SemanticType) as StyleBoxFlat;

        _ringStyle = ring?.Duplicate() as StyleBoxFlat;
        _ringColor = ring?.BorderColor ?? Colors.Transparent;

        if (_ringStyle is not null)
            _plaque.AddThemeStyleboxOverride("panel", _ringStyle);

        // The name is the title; everything under it is secondary and drawn smaller,
        // which is what buys the topic its two lines and the queue notice a third
        // without the plaque growing down into the viewport. PlaqueBudget is the
        // width this leaves the topic, in the characters the surface recommends.
        int fontSize = GetThemeDefaultFontSize();
        _title.AddThemeFontSizeOverride("font_size", fontSize + 2);
        _subtitle.AddThemeFontSizeOverride("font_size", fontSize - 2);
        _queued.AddThemeFontSizeOverride("font_size", fontSize - 2);
        _queued.AddThemeColorOverride("font_color", _subtitleColor);
        _clock.AddThemeFontSizeOverride("font_size", fontSize - 2);
        _clock.AddThemeColorOverride("font_color", _subtitleColor);
    }

    /// <summary>
    /// The single chokepoint for suspending the interface while an agent holds the
    /// editor — keyboard and pointer both. Everything is swallowed here, before
    /// GUI, focus navigation or any panel's own handler can read it, so the user
    /// drives nothing but the Stop button on the plaque.
    /// </summary>
    /// <remarks>
    /// What is suspended is <b>the user's</b> input, not input as such. That
    /// distinction did not exist while an agent could only reach the editor over
    /// the command surface, and it is load-bearing now that the editor can drive
    /// its own interface for testing: the right to inject exists precisely when
    /// input is blocked. Injected events carry a mark
    /// (<see cref="SyntheticInput"/>) and pass; physical ones do not and are
    /// swallowed, except over the plaque, which is how the user stops an agent.
    /// <para>
    /// The dimmer does stop the mouse — but only for what events cannot cover:
    /// the engine recomputes hover and the cursor shape from the machine's cursor
    /// each frame, past any handler, and covering the panels is the one way to
    /// keep a field from lighting up under a user who is only watching. It does
    /// not block the agent, because its hit test opens around the injected
    /// pointer; see <see cref="AgentDimmer"/>.
    /// </para>
    /// </remarks>
    public override void _Input(InputEvent @event)
    {
        bool isOverAction = TryGetPosition(@event, out Vector2 position)
            && _action.IsVisibleInTree()
            && _action.GetGlobalRect().HasPoint(position);

        if (InputSuspension.ShouldSuspend(_holdsControl, SyntheticInput.IsSynthetic(@event), isOverAction))
            GetViewport().SetInputAsHandled();
    }

    public void Set(ControlBannerState state)
    {
        _state = state;

        ControlBannerKind kind = state.Kind;

        // The interface is suspended for as long as the agent holds control,
        // whether it is working or has merely gone quiet: dimmer down, keyboard
        // off, gold ring on.
        bool holdsControl = kind is ControlBannerKind.AgentWorking or ControlBannerKind.AgentSilent;
        bool shown = kind != ControlBannerKind.None;

        ApplyControlHold(holdsControl);

        // Aimed, not assigned. Everything that appears and goes here does it
        // over a moment; what must not wait a moment is the suspension itself,
        // which is ApplyControlHold's and the dimmer's armed flag above.
        _plaqueFade.Retarget(shown ? 1f : 0f);
        _ringLit.Retarget(holdsControl ? 1f : shown ? PendingRing : 0f);
        _dimFade.Retarget(holdsControl ? 1f : 0f);

        _face.SetMood(kind switch
        {
            ControlBannerKind.AgentWorking => AgentMood.Working,
            ControlBannerKind.AgentSilent => AgentMood.Silent,
            _ => AgentMood.Waiting
        });

        _title.Text = string.IsNullOrEmpty(state.AgentName) ? "An agent" : state.AgentName;

        // The subtitle is the agent's own topic wherever there is room for it;
        // the phases that carry a message of their own — waiting, not
        // responding — say that instead, because "what is happening" outranks
        // "what the agent meant to do" exactly when the two disagree.
        _subtitle.Text = kind switch
        {
            ControlBannerKind.AgentWaiting => "Waiting for you to finish",
            ControlBannerKind.AgentCountdown => TopicOr(state, "Asks for control of the editor"),
            ControlBannerKind.AgentWorking => TopicOr(state, "Working in the editor"),
            ControlBannerKind.AgentSilent =>
                $"Not responding — control returns in {state.SecondsLeft}s",
            _ => string.Empty
        };

        // Who is queued behind the agent working, because otherwise the editor being
        // taken again the moment it comes back reads as this agent refusing to let go.
        _queued.Text = DescribeQueue(state);
        _queued.Visible = kind != ControlBannerKind.None && state.QueuedAgents > 0;

        bool isSilent = kind == ControlBannerKind.AgentSilent;
        _subtitle.AddThemeColorOverride("font_color", isSilent ? _warningColor : _subtitleColor);
        _face.SetTint(isSilent ? _warningColor : Colors.White);

        _clock.Text = kind switch
        {
            ControlBannerKind.AgentCountdown => $"in {state.SecondsLeft}",
            ControlBannerKind.AgentWorking or ControlBannerKind.AgentSilent => state.Elapsed,
            _ => string.Empty
        };
        _clock.Visible = _clock.Text.Length > 0;

        _action.Text = kind == ControlBannerKind.AgentCountdown ? "Cancel" : "Stop";
        _action.Visible = kind != ControlBannerKind.AgentWaiting;
    }

    /// <summary>
    /// Icon on the left, name over topic in the middle, clock and the button on
    /// the right. The text column is the one that stretches, and both its labels
    /// clip: a long topic shortens to an ellipsis rather than widening the plaque
    /// across the viewport.
    /// </summary>
    private Control BuildPlaqueContent()
    {
        Clip(_title);

        // The topic gets room to breathe: it wraps to a second line before it
        // resorts to the ellipsis, growing the plaque downwards — never
        // sideways, which is what wrapping instead of clipping buys.
        _subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _subtitle.MaxLinesVisible = 2;
        _subtitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;

        // A third line, shown only while somebody is queued: the plaque grows
        // downwards for it the way the topic already does, rather than competing
        // with the topic for the one line the topic owns.
        Clip(_queued);

        VBoxContainer identity = Ui.VBox(_title, _subtitle, _queued);
        identity.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        identity.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        identity.AddThemeConstantOverride("separation", 1);

        _clock.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _action.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        HBoxContainer row = Ui.HBox(_face, identity, _clock, _action);
        row.AddThemeConstantOverride("separation", 10);

        return row;
    }

    private static string TopicOr(ControlBannerState state, string fallback) =>
        string.IsNullOrEmpty(state.Topic) ? fallback : state.Topic;

    /// <summary>
    /// The line waiting for the editor, in one short line of its own: the next agent
    /// by name where it gave one, and how many are behind it. The name is guarded like
    /// the title — the constructor paints a default state, whose strings are null
    /// rather than empty — and an agent that never introduced itself is still waiting,
    /// so the count is what decides the wording.
    /// </summary>
    private static string DescribeQueue(ControlBannerState state)
    {
        var others = state.QueuedAgents - 1;
        bool named = !string.IsNullOrEmpty(state.WaitingAgent);

        if (others <= 0)
            return named ? $"{state.WaitingAgent} is waiting for a turn" : "An agent is waiting for a turn";

        return named
            ? $"{state.WaitingAgent} and {others} more are waiting for a turn"
            : $"{state.QueuedAgents} agents are waiting for a turn";
    }

    /// <summary>
    /// Clipping is what lets the plaque keep its width whatever the title says:
    /// without it a label's text is a minimum size, and a long name would push
    /// the plaque out sideways. The subtitle wraps instead — see the builder.
    /// </summary>
    private static void Clip(Label label)
    {
        label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }

    /// <summary>
    /// Where an event is aimed, when it is aimed anywhere. Gestures carry a
    /// position as much as clicks do, and a trackpad pinch over a suspended
    /// viewport is exactly as unwelcome as a drag on it.
    /// </summary>
    private static bool TryGetPosition(InputEvent @event, out Vector2 position)
    {
        switch (@event)
        {
            case InputEventMouse mouse:
                position = mouse.Position;

                return true;

            case InputEventGesture gesture:
                position = gesture.Position;

                return true;

            default:
                position = default;

                return false;
        }
    }

    /// <summary>
    /// Enters or leaves the suspended state on its edge. Going in, the keyboard
    /// is dropped from whatever held it — once is enough, because from then on
    /// <see cref="_Input"/> swallows the keys that could take it back. The ring
    /// is <see cref="ApplyRing"/>'s job, because it changes on more edges than
    /// this one.
    /// </summary>
    private void ApplyControlHold(bool holdsControl)
    {
        if (holdsControl == _holdsControl)
            return;

        _holdsControl = holdsControl;

        // What the dimmer *stops* turns on and off with the hold, never with the
        // fade: the shield is the reason it takes the mouse at all, and a user
        // whose editor has just come back must not spend the fade unable to
        // touch it. Paint eases; refusal does not.
        _dimmer.IsArmed = holdsControl;

        if (holdsControl)
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
    }

    /// <summary>
    /// Moves the three numbers on and hands them to what they paint. The plaque
    /// rises as it fades, which is one number doing both: a card that fades in
    /// place reads as a decal, and a card that arrives from somewhere reads as
    /// having been put there.
    /// </summary>
    public override void _Process(double delta)
    {
        var step = (float)delta;

        _plaqueFade.Tick(step);
        _ringLit.Tick(step);
        _dimFade.Tick(step);

        float fade = _plaqueFade.Value;

        _plaqueSlot.Modulate = new Color(1f, 1f, 1f, fade);
        _plaqueSlot.Position = new Vector2(0f, -((1f - fade) * PlaqueRise));

        // Hidden once it is gone, and not a moment before: the Stop button is
        // inside it, and a button at nought alpha that still answers a press is
        // a trap rather than a transition.
        _plaqueSlot.Visible = fade > 0.001f;

        if (_ringStyle is not null)
            _ringStyle.BorderColor = new Color(_ringColor, _ringColor.A * _ringLit.Value);

        _dimmer.Color = new Color(_dimColor, _dimColor.A * _dimFade.Value);
        _dimmer.Visible = _dimFade.Value > 0.001f;
    }
}
