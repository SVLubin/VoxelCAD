using Godot;

namespace VoxelCAD.View;

/// <summary>What the plaque's robot is doing, as the eyes it wears.</summary>
internal enum AgentMood
{
    /// <summary>Asking, or waiting for the user to finish. Small eyes.</summary>
    Waiting,

    /// <summary>Holding the editor and answering. Square eyes.</summary>
    Working,

    /// <summary>Stopped answering. Crosses, which nobody has to be told.</summary>
    Silent
}

/// <summary>
/// The plaque's robot: one head, four sets of eyes, and a cross-fade between
/// whichever two are changing places.
/// </summary>
/// <remarks>
/// Two stacked pictures rather than one whose texture is swapped. A swap is a
/// cut, and a cut on a face reads as a different robot arriving; the incoming
/// expression fades in over the outgoing one, which reads as the same robot
/// changing its mind. Both are the same head drawn at the same place, so only
/// the eyes appear to move.
/// <para>
/// It blinks, and that is the whole of the idle. A plaque can stand on screen
/// for minutes, and a face that never moves in all that time stops reading as a
/// face; a blink every few seconds is the least that keeps it alive. Not while
/// it is <see cref="AgentMood.Silent"/> — crossed eyes do not blink, and that
/// is the point of them — and not at all while animation is switched off, a
/// blink being motion for its own sake, which is exactly what the setting is
/// about.
/// </para>
/// </remarks>
internal sealed partial class AgentFace : Control
{
    /// <summary>How long an expression takes to become another one.</summary>
    private const float MoodFade = 0.18f;

    /// <summary>And how long an eye takes to shut. Quicker: a blink is a blink.</summary>
    private const float BlinkFade = 0.07f;

    /// <summary>How long they stay shut.</summary>
    private const float BlinkHold = 0.09f;

    /// <summary>
    /// And how long between blinks. Slow enough to be noticed only once, which
    /// is what "unobtrusive" means for something on screen for minutes.
    /// </summary>
    private const float BlinkInterval = 4.5f;

    private readonly MotionTiming _timing;
    private readonly TextureRect _under = new() { Name = "Under" };
    private readonly TextureRect _over = new() { Name = "Over" };
    private readonly Smoothed _blend;

    private AgentMood _mood = AgentMood.Waiting;
    private bool _blinking;
    private float _sinceBlink;
    private float _blinkHeld;
    private bool _iconsLoaded;

    public AgentFace(MotionTiming timing, float side)
    {
        _timing = timing;
        _blend = new Smoothed(MotionSpeed.Opacity, timing);

        Name = "AgentFace";
        CustomMinimumSize = new Vector2(side, side);
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;

        foreach (TextureRect layer in new[] { _under, _over })
        {
            layer.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            layer.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            layer.MouseFilter = MouseFilterEnum.Ignore;
            layer.SetAnchorsPreset(LayoutPreset.FullRect);

            AddChild(layer);
        }
    }

    /// <summary>
    /// The tint the whole face wears — the warning colour once the agent has
    /// stopped answering. Applied to both layers, or a cross-fade would change
    /// two things at once and read as a flicker.
    /// </summary>
    public void SetTint(Color tint)
    {
        _under.SelfModulate = tint;
        _over.SelfModulate = tint;
    }

    public void SetMood(AgentMood mood)
    {
        if (_mood == mood)
            return;

        _mood = mood;
        _blinking = false;
        _blinkHeld = 0f;
        _sinceBlink = 0f;

        SwapTo(FaceFor(mood), MoodFade);
    }

    public override void _Ready()
    {
        LoadIcons();
        SwapTo(FaceFor(_mood), 0f);
    }

    public override void _Notification(int what)
    {
        if ((long)what == NotificationThemeChanged && IsInsideTree())
            _iconsLoaded = false;
    }

    public override void _Process(double delta)
    {
        LoadIcons();

        var step = (float)delta;

        _blend.Tick(step);

        // The incoming face over the outgoing one, which stays whole underneath.
        // Cross-fading two opaque pictures by taking one down as the other comes
        // up dips in the middle; leaving the old one alone does not.
        _over.Modulate = new Color(1f, 1f, 1f, _blend.Value);

        TickBlink(step);
    }

    /// <summary>
    /// The eyes shut and open again, on a clock of its own rather than on the
    /// motion speed: a blink is not a transition between two states of the
    /// interface, it is a thing the face does.
    /// </summary>
    private void TickBlink(float delta)
    {
        if (_blinking)
        {
            _blinkHeld += delta;

            if (_blinkHeld < _timing.ScaleFixed(BlinkHold))
                return;

            _blinking = false;
            _blinkHeld = 0f;

            SwapTo(FaceFor(_mood), BlinkFade);

            return;
        }

        // Crossed eyes do not blink, and nothing blinks while the editor is set
        // to move at once: an idle is motion with nothing behind it, which is
        // the whole of what that setting turns off.
        if (!_timing.Animate || _mood == AgentMood.Silent)
            return;

        _sinceBlink += delta;

        if (_sinceBlink < BlinkInterval)
            return;

        _sinceBlink = 0f;
        _blinking = true;

        SwapTo(EditorIconCatalog.AgentFaceBlink, BlinkFade);
    }

    private void SwapTo(string face, float seconds)
    {
        if (!_iconsLoaded)
            return;

        Texture2D texture = GetThemeIcon(
            EditorIconFactory.PlaqueFace(face), EditorThemeFactory.SemanticType);

        if (_over.Texture == texture)
            return;

        _under.Texture = _over.Texture ?? texture;
        _over.Texture = texture;

        _blend.SnapTo(0f);
        _blend.RetargetOver(1f, _timing.ScaleFixed(seconds));
    }

    private void LoadIcons()
    {
        if (_iconsLoaded || !IsInsideTree())
            return;

        _iconsLoaded = true;

        // Re-fetched rather than kept: a theme change replaces every texture,
        // and the two layers would otherwise go on showing the old ones.
        _over.Texture = null;
        _under.Texture = null;

        SwapTo(FaceFor(_mood), 0f);
    }

    /// <summary>
    /// Which drawing a mood wears. Internal so a test can hold it: the plaque
    /// only shows one mood at a time and the silent one arrives a minute after
    /// an agent stops talking, which is a window nothing here can photograph
    /// reliably — the mapping is the half that can be checked, and it is also
    /// the half that could be wrong.
    /// </summary>
    internal static string FaceFor(AgentMood mood) => mood switch
    {
        AgentMood.Working => EditorIconCatalog.AgentFaceWorking,
        AgentMood.Silent => EditorIconCatalog.AgentFaceSilent,
        _ => EditorIconCatalog.AgentFaceWaiting
    };
}
