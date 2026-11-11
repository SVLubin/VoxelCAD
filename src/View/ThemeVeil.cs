using System;
using Godot;

namespace VoxelCAD.View;

/// <summary>
/// What a theme change happens behind: the window goes out of focus, the frame
/// is frozen there and the look swapped behind the still, and the new one
/// dissolves in and resolves back into focus.
/// </summary>
/// <remarks>
/// The seam it hides is real and cannot be eased away, because a repaint is one
/// call: a few hundred styleboxes and every icon are rewritten, and what the eye
/// catches afterwards is rows re-measuring and panels settling into their new
/// metrics. Covering it is the only honest fix — the alternative is animating a
/// theme, which would mean interpolating every stylebox in the editor. What was
/// tried before this and why each of them failed is
/// <see cref="ThemeVeilTimeline"/>'s doc.
/// <para>
/// <b>The still is taken while the veil is up, and that is what makes the
/// substitution exact.</b> The viewport's texture is the <em>composited</em>
/// window, so a capture taken at that moment is a picture of the blur this very
/// shader just drew — which is why it is then shown <b>raw</b>, at no mip level
/// at all. Blurring it again would double it; showing it as it came is the same
/// pixels the frame before, so the switch from the live window to the still
/// cannot be seen. An earlier cut captured on the first frame instead, sharp,
/// and had to blur the still and the screen through two different pipelines and
/// hope they matched.
/// </para>
/// <para>
/// <b>The held stretch breathes</b> — sine arches on their own clock, from the
/// instant the defocus lands, driving the swell (the picture inflating about
/// its centre), the lift (a blend toward white, visible at the centre where
/// the swell moves nothing) and, on the live side only, a little extra
/// softness. The still deliberately takes no extra blur: that half was
/// measured invisible — a mean pixel of 2.5 in 255 at full breath, blur over
/// blur being a dial that stops turning — and dropping it lets the freeze
/// shed the mip chain it once grew, which shortens the one stall the
/// breathing has.
/// </para>
/// <para>
/// The dissolve afterwards is between the still and the live window <b>at the
/// same mip level the still was frozen at</b>, so the two sides are the same
/// operation over different content and the cross-fade is a change of colour
/// rather than of sharpness.
/// </para>
/// <para>
/// <b>It carries its own <see cref="BackBufferCopy"/>, and without one it blurs
/// the scene and nothing else.</b> Godot takes the copy that backs
/// <c>hint_screen_texture</c> once for a run of canvas items rather than before
/// each of them, so a shader reading it late in the tree still sees the buffer
/// as it stood before the panels were drawn — which for the glass panels is
/// exactly right, each blurring the scene behind it, and for this is a window
/// that goes soft with the whole interface sitting sharp on top of it. Caught by
/// looking: the agent's plaque, a near-white card at the top of the window, was
/// simply not in the blurred read. The copy is a child drawn before the surface,
/// which is what makes it a copy of everything above it, and it is switched off
/// between runs because a whole-viewport copy every frame is not free.
/// </para>
/// <para>
/// Three things are decided rather than incidental:
/// </para>
/// <list type="bullet">
/// <item><b>It runs whatever the animation setting says.</b> Every other motion
/// in this chrome goes through <see cref="MotionTiming"/> and snaps when the
/// setting is off; this one does not, because switching it off would not remove
/// motion — it would put the seam back.</item>
/// <item><b>It swallows every event while it runs</b>, the user's and an
/// agent's alike, at the same <c>_Input</c> chokepoint the banner uses and for a
/// sharper reason: what is on screen is out of focus and half of it is a
/// photograph, so anything aimed at it is aimed at where a control <em>was</em>.
/// Not by hit test — a hit test would stop the mouse and let the keyboard
/// through.</item>
/// <item><b>It sits above <c>BannerLayer</c></b>, the one exception to the
/// shell's "everything the shell builds goes under the banner" rule. That rule
/// exists so the dimmer is what the machine's hover lands on while an agent
/// holds the editor; this refuses <em>everything</em>, so it is that refusal
/// widened rather than a hole in it — and the plaque is themed, so it has to be
/// covered or it flashes on its own.</item>
/// </list>
/// </remarks>
internal sealed partial class ThemeVeil : Control
{
    /// <summary>
    /// How far out of focus the window goes, as a mip level — each step doubles
    /// the smear. Five is past the point where any text is text, which is what
    /// the dissolve needs: what changes there has to be a field of colour.
    /// </summary>
    private const float MaximumLod = 5f;

    /// <summary>
    /// How much softer the held frame gets at the top of its one breath — the
    /// quiet half of it, riding along under the motion. Measured saturated as
    /// the whole of the effect: past lod five almost no gradient energy is left
    /// between the scales more blur removes, and a lod and a half moved the
    /// mean pixel 2.5 of 255 with the 99th percentile at 14 — working by every
    /// trace and invisible to every eye. Blur over blur is a dial that stops
    /// turning, which is why the breath's visible half is the swell below.
    /// </summary>
    private const float BreathLod = 1.5f;

    /// <summary>
    /// How far the picture inflates at the top of the breath. Motion is what
    /// the eye reads where softness has saturated — and the first cut of this
    /// was a third of it, and too quiet to read: at three
    /// percent the window's edges travel some fifty pixels and the middle
    /// barely stirs, which is an inhale rather than a zoom.
    /// </summary>
    private const float BreathSwell = 0.03f;

    /// <summary>
    /// How far the picture brightens at full breath. The swell is motion and
    /// motion is zero at the centre — where the user is looking — so the
    /// inhale also lifts the picture toward white by this much, which is
    /// visible everywhere at once.
    /// </summary>
    private const float BreathLift = 0.05f;

    private static readonly string ShaderCode =
        """
        shader_type canvas_item;

        uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear_mipmap;
        uniform sampler2D frozen : repeat_disable, filter_linear;
        uniform float lod = 0.0;
        uniform float blend = 0.0;
        uniform float held = 0.0;
        uniform float swell = 1.0;
        uniform float lift = 0.0;

        void fragment() {
            // The breath, as motion: the whole picture inflates about its centre
            // and settles back. Both sides wear the same swell, so the dissolve
            // stays one picture — and it is unity outside the held stretch, so
            // the sharp window is never touched.
            vec2 live_uv = (SCREEN_UV - 0.5) / swell + 0.5;
            vec2 still_uv = (UV - 0.5) / swell + 0.5;

            vec3 live = textureLod(screen_texture, live_uv, lod).rgb;

            // The still is already blurred — it is a picture of this shader's own
            // output — and is read flat: its share of the breath is the swell and
            // the lift, extra softness on it having been measured invisible.
            // Before there is one, the old side is the live window, which is the
            // same thing one frame earlier.
            vec3 still = mix(live, texture(frozen, still_uv).rgb, held);

            // The breath's centre-visible half: the swell moves nothing where
            // the eye actually is, so the picture also brightens a little as
            // it inhales — toward white, so nothing clips and dark and light
            // themes lift alike.
            vec3 colour = mix(still, live, blend);

            COLOR = vec4(colour + (vec3(1.0) - colour) * lift, 1.0);
        }
        """;

    private readonly ThemeVeilTimeline _timeline = new();
    private readonly ShaderMaterial _material =
        new() { Shader = new Shader { Code = ShaderCode } };

    private readonly BackBufferCopy _copy = new() { Name = "ThemeVeilCopy" };
    private readonly ColorRect _surface = new() { Name = "ThemeVeilSurface" };

    private ImageTexture? _frozen;
    private Action? _swap;

    public ThemeVeil()
    {
        Name = "ThemeVeil";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);

        // In this order: the copy is drawn first and so takes everything above
        // it, and the surface then reads what the copy left.
        _copy.CopyMode = BackBufferCopy.CopyModeEnum.Disabled;
        AddChild(_copy);

        _surface.Material = _material;
        _surface.MouseFilter = MouseFilterEnum.Ignore;
        _surface.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_surface);

        Push(0f, 0f);
    }

    public bool IsRunning => _timeline.IsRunning;

    /// <summary>
    /// Idle this costs nothing: no frame tick, and no events looked at. It has
    /// to be said here rather than in the constructor, because Godot turns
    /// processing <em>on</em> for a script that overrides <c>_Process</c>, and
    /// it does that as the node is set up — after any constructor. Said there,
    /// the veil ticked every frame of the session doing nothing, which a
    /// per-frame trace found and nothing else would have.
    /// </summary>
    public override void _Ready() => Idle();

    /// <summary>
    /// Takes the window out of focus, calls <paramref name="swap"/> behind the
    /// still, and brings the new look back in.
    /// </summary>
    public void Cover(Action swap)
    {
        // A second change arriving mid-run takes effect at once rather than
        // starting another. Nothing in the interface can ask for one — every
        // event is being swallowed — so what reaches here is the shell
        // re-pushing its display preferences, which the skin answers by doing
        // nothing at all.
        if (_timeline.IsRunning)
        {
            swap();

            return;
        }

        _swap = swap;
        _timeline.Start();

        Visible = true;
        _copy.CopyMode = BackBufferCopy.CopyModeEnum.Viewport;

        Idle();
        Release();

        // Sharp, and with no still yet, so the veil shows the live window
        // exactly as it is: becoming visible changes nothing at all.
        Push(0f, 0f);
    }

    /// <remarks>
    /// Everything, from wherever: while the window is out of focus and half of
    /// it is a photograph, a click is a click at where a control used to be.
    /// </remarks>
    public override void _Input(InputEvent @event)
    {
        if (_timeline.IsRunning)
            GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        ThemeVeilStep step = _timeline.Advance((float)delta);

        // Freeze and swap in that order and both before the uniforms are pushed:
        // the still has to be the blur as it stood, and the window behind it has
        // to be the new look by the time this frame draws.
        if (step.Swap)
        {
            TryFreeze();

            Action? swap = _swap;

            _swap = null;
            swap?.Invoke();
        }

        Push(step.Blur, step.Blend, step.Breath);

        if (!step.Finished)
            return;

        Visible = false;
        _copy.CopyMode = BackBufferCopy.CopyModeEnum.Disabled;

        Release();
        Idle();
    }

    /// <summary>
    /// Takes the frame as it stands — this shader's own blurred output — and
    /// hands it back as a flat picture. A window with no size answers nothing,
    /// and the run then carries on against the live screen, which is the whole
    /// transition minus the beat that hides the repaint rather than a failure.
    /// </summary>
    private void TryFreeze()
    {
        Image? image = GetViewport()?.GetTexture()?.GetImage();

        if (image is null || image.IsEmpty())
            return;

        Release();

        // No mip chain: the still's share of extra softness was measured
        // invisible (a mean pixel of 2.5 in 255 at full breath), and the swap
        // frame's stall is the one pause the breathing has — so the freeze
        // sheds every cost it can, and the still breathes by swell and lift.
        _frozen = ImageTexture.CreateFromImage(image);
        _material.SetShaderParameter("frozen", _frozen);
        _material.SetShaderParameter("held", 1f);

        image.Dispose();
    }

    /// <summary>
    /// Lets the photograph go. A window's worth of pixels is twenty megabytes,
    /// and holding one between theme changes would be holding it for the life of
    /// the session; the uniform is cleared first, so nothing is left pointing at
    /// it.
    /// </summary>
    private void Release()
    {
        _material.SetShaderParameter("held", 0f);

        if (_frozen is null)
            return;

        _material.SetShaderParameter("frozen", default(Variant));

        _frozen.Dispose();
        _frozen = null;
    }

    /// <summary>Ticks and listens exactly while there is something to do.</summary>
    private void Idle()
    {
        SetProcess(_timeline.IsRunning);
        SetProcessInput(_timeline.IsRunning);
    }

    private void Push(float blur, float blend, float breath = 0f)
    {
        float extra = breath * BreathLod;

        _material.SetShaderParameter("lod", blur * MaximumLod + extra);
        _material.SetShaderParameter("blend", blend);
        _material.SetShaderParameter("swell", 1f + breath * BreathSwell);
        _material.SetShaderParameter("lift", breath * BreathLift);
    }
}
